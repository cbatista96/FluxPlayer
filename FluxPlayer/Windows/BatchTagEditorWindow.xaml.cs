using CommunityToolkit.Mvvm.ComponentModel;
using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using System.Collections.Immutable;
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
    private readonly Dictionary<string, (CheckBox Apply, System.Windows.Controls.TextBox Value)> _advanced = new();

    public BatchTagEditorWindow(Window owner, MainViewModel vm, IEnumerable<string> initialFiles)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        AdvancedFields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        AdvancedFields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        int fieldIndex = 0;
        foreach (var field in AdvancedTagFields.All)
        {
            int row = fieldIndex / 2, column = fieldIndex % 2;
            if (column == 0) AdvancedFields.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var panel = new StackPanel { Margin = new Thickness(0, 0, column == 0 ? 12 : 0, 12) };
            var apply = new CheckBox { Content = "Change " + field.Label.ToLowerInvariant(), Margin = new Thickness(0, 0, 0, 7) };
            var value = new System.Windows.Controls.TextBox
            {
                MaxLength = 4096,
                Padding = new Thickness(10, 7, 10, 7),
                AcceptsReturn = field.Key is "comment" or "lyrics",
                MinHeight = 34,
                MaxHeight = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            value.SetBinding(IsEnabledProperty, new Binding("IsChecked") { Source = apply });
            AutomationProperties.SetName(value, "Batch " + field.Label);
            panel.Children.Add(apply); panel.Children.Add(value); Grid.SetRow(panel, row); Grid.SetColumn(panel, column); AdvancedFields.Children.Add(panel);
            _advanced[field.Key] = (apply, value); fieldIndex++;
        }
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
        MetadataPatch patch;
        try { patch = CapturePatch(); }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
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

    private MetadataPatch CapturePatch() => new(ApplyTitle.IsChecked == true ? TitleBox.Text : null,
        ApplyArtist.IsChecked == true ? ArtistBox.Text : null, ApplyAlbum.IsChecked == true ? AlbumBox.Text : null,
        ApplyGenre.IsChecked == true ? GenreBox.Text : null,
        _advanced.Where(pair => pair.Value.Apply.IsChecked == true).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Value.Text),
        ApplyCustom.IsChecked == true ? CustomTagFields.Parse(CustomBox.Text) : null);

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        MetadataPatch patch;
        try { patch = CapturePatch(); patch.Validate(); } catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
        var dialog = new SaveFileDialog { Title = "Save tag field template", Filter = "Tag field template|*.json", FileName = "Tag field template.json", OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return;
        await TemplateOperationAsync(async token => { await TagEditTemplate.WriteAsync(dialog.FileName, patch, token); if (!_closed) StatusText.Text = "Template saved. No media files were changed."; });
    }
    private async void LoadTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var dialog = new OpenFileDialog { Title = "Load tag field template", Filter = "Tag field template|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        await TemplateOperationAsync(async token =>
        {
            var patch = await TagEditTemplate.ReadAsync(dialog.FileName, token);
            if (_closed) return;
            ApplyTitle.IsChecked = patch.Title != null; TitleBox.Text = patch.Title ?? "";
            ApplyArtist.IsChecked = patch.Artist != null; ArtistBox.Text = patch.Artist ?? "";
            ApplyAlbum.IsChecked = patch.Album != null; AlbumBox.Text = patch.Album ?? "";
            ApplyGenre.IsChecked = patch.Genre != null; GenreBox.Text = patch.Genre ?? "";
            foreach (var pair in _advanced) { pair.Value.Apply.IsChecked = patch.Extra?.ContainsKey(pair.Key) == true; pair.Value.Value.Text = patch.Extra?.GetValueOrDefault(pair.Key, "") ?? ""; }
            ApplyCustom.IsChecked = patch.Custom?.Count > 0;
            CustomBox.Text = string.Join(Environment.NewLine, (patch.Custom ?? ImmutableDictionary<string, string>.Empty).OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
            StatusText.Text = "Template loaded. Review the checked fields, then apply explicitly. No media files were changed.";
        });
    }
    private async Task TemplateOperationAsync(Func<CancellationToken, Task> action)
    {
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        FieldsPanel.IsEnabled = FileActions.IsEnabled = StartButton.IsEnabled = false; CancelButton.IsEnabled = true;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Template operation canceled."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally { _operation = null; if (!_closed) { FieldsPanel.IsEnabled = FileActions.IsEnabled = StartButton.IsEnabled = true; CancelButton.IsEnabled = false; } }
    }

    private async void ReadTags_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var row = FilesGrid.SelectedItems.Count == 1 ? FilesGrid.SelectedItems.Cast<TagEditRow>().Single() : null;
        if (row == null) { StatusText.Text = "Select one file to read its tags. Reading does not mark fields for saving."; return; }
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        FieldsPanel.IsEnabled = FileActions.IsEnabled = StartButton.IsEnabled = false; CancelButton.IsEnabled = true;
        try
        {
            var tags = await _vm.Engine.ReadTagDetailsAsync(row.Path, cancellation.Token);
            if (_closed) return;
            if (tags == null) { StatusText.Text = "Tags could not be read from this file."; return; }
            TitleBox.Text = tags.Title; ArtistBox.Text = tags.Artist; AlbumBox.Text = tags.Album; GenreBox.Text = tags.Genre;
            ApplyTitle.IsChecked = ApplyArtist.IsChecked = ApplyAlbum.IsChecked = ApplyGenre.IsChecked = ApplyCustom.IsChecked = false;
            foreach (var pair in _advanced) { pair.Value.Apply.IsChecked = false; pair.Value.Value.Text = tags.Extra?.GetValueOrDefault(pair.Key, "") ?? ""; }
            CustomBox.Text = string.Join(Environment.NewLine, (tags.Custom ?? ImmutableDictionary<string, string>.Empty).OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
            StatusText.Text = "Read " + row.Name + ". Check only the fields you want to write; unchecked fields remain untouched.";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Tag reading canceled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally { _operation = null; if (!_closed) { FieldsPanel.IsEnabled = FileActions.IsEnabled = StartButton.IsEnabled = true; CancelButton.IsEnabled = false; } }
    }
}
