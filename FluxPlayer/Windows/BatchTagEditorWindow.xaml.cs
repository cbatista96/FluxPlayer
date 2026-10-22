using CommunityToolkit.Mvvm.ComponentModel;
using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class TagEditRow : ObservableObject
{
    public string Path { get; }
    public string Name => MediaFormats.IsNetwork(Path) ? Path : System.IO.Path.GetFileName(Path);
    [ObservableProperty] private string _status = "Pending";
    [ObservableProperty] private string _details = "";
    public TagEditRow(string path) => Path = path;
}

public partial class BatchTagEditorWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<TagEditRow> _files = new();
    private CancellationTokenSource? _operation;
    private bool _closed;

    public BatchTagEditorWindow(Window owner, MainViewModel vm, IEnumerable<string> initialFiles)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        FilesGrid.ItemsSource = _files; AddFiles(initialFiles);
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var existing = _files.Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (_files.Count >= BatchTagService.MaximumFiles) { StatusText.Text = "This job supports up to 10,000 files."; return; }
            if (existing.Add(path)) _files.Add(new TagEditRow(path));
        }
        StatusText.Text = $"{_files.Count:N0} files. Choose the fields to change. Loaded files and network sources are skipped.";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var dialog = new OpenFileDialog { Title = "Choose files for batch tag editing", Filter = MediaFormats.FileDialogFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        foreach (var row in FilesGrid.SelectedItems.Cast<TagEditRow>().ToArray()) _files.Remove(row);
        StatusText.Text = $"{_files.Count:N0} files in this job. Media files have not been changed.";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var patch = new MetadataPatch(ApplyTitle.IsChecked == true ? TitleBox.Text : null,
            ApplyArtist.IsChecked == true ? ArtistBox.Text : null, ApplyAlbum.IsChecked == true ? AlbumBox.Text : null,
            ApplyGenre.IsChecked == true ? GenreBox.Text : null);
        try { patch.Validate(); }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
        if (_files.Count == 0) { StatusText.Text = "Add at least one file to this job."; return; }
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        var rows = _files.ToDictionary(row => row.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var row in _files) { row.Status = "Pending"; row.Details = ""; }
        FieldsPanel.IsEnabled = FileActions.IsEnabled = StartButton.IsEnabled = false; CancelButton.IsEnabled = true;
        JobProgress.Maximum = _files.Count; JobProgress.Value = 0;
        StatusText.Text = "Applying checked fields. The current write finishes safely before cancellation.";
        try
        {
            var progress = new Progress<TagEditProgress>(p =>
            {
                if (_closed || _operation != cancellation) return;
                JobProgress.Value = p.Completed;
                if (rows.TryGetValue(p.Latest.Path, out var row)) { row.Status = p.Latest.Outcome.ToString(); row.Details = p.Latest.Message; }
                StatusText.Text = $"{p.Completed:N0}/{p.Total:N0} · {System.IO.Path.GetFileName(p.Latest.Path)}";
            });
            var result = await BatchTagService.ApplyAsync(rows.Keys, patch, _vm.Engine, _vm.Library,
                _vm.CurrentItem?.FilePath, progress, cancellation.Token);
            if (!_closed)
            {
                foreach (var item in result.Results)
                    if (rows.TryGetValue(item.Path, out var row)) { row.Status = item.Outcome.ToString(); row.Details = item.Message; }
                StatusText.Text = $"{(result.Canceled ? "Canceled" : "Completed")}: {result.Saved:N0} saved · {result.Skipped:N0} skipped · {result.Failed:N0} failed. Completed changes have been retained.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or VLCException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally
        {
            _operation = null;
            if (!_closed) { FieldsPanel.IsEnabled = FileActions.IsEnabled = StartButton.IsEnabled = true; CancelButton.IsEnabled = false; }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
}
