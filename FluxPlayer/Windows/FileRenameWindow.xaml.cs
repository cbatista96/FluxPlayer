using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class FileRenameWindow : FluentWindow
{
    private sealed record RenameViewRow(string Source, string Destination, string Status, string? Root = null)
    {
        public string SourceName => Path.GetFileName(Source);
        public string DestinationName => Root == null ? Path.GetFileName(Destination) : Path.GetRelativePath(Root, Destination);
    }
    private readonly MainViewModel _vm;
    private readonly List<RenameInput> _inputs = new();
    private ImmutableArray<RenameEntry> _plan = ImmutableArray<RenameEntry>.Empty;
    private CancellationTokenSource? _operation;
    private bool _closed;
    private readonly bool _organize;
    private string? _planRoot;
    public FileRenameWindow(Window owner, MainViewModel vm, bool organize = false, IEnumerable<RenameInput>? initial = null)
    {
        InitializeComponent(); Owner = owner; _vm = vm; _organize = organize;
        _inputs.AddRange(initial ?? vm.Playlist.Where(item => !item.IsNetwork).Select(item => new RenameInput(item.FilePath, item.Title, item.Artist, item.Album)));
        if (organize)
        {
            Title = "Organize media files · FluxPlayer"; Heading.Text = "Organize media files";
            Introduction.Text = "Preview metadata folders before moving files. Same drive/share only; loaded files are skipped and existing destinations are never replaced. Completed moves remain after cancellation; there is no undo.";
            OrganizationOptions.Visibility = Visibility.Visible; Pattern.Text = "{filename}";
            ApplyButton.Content = "Move previewed files"; Height = 780; MinHeight = 650;
        }
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
        Status.Text = $"{_inputs.Count:N0} selected file(s). Preview before applying.";
    }
    private void Invalidate()
    { _plan = ImmutableArray<RenameEntry>.Empty; _planRoot = null; if (ApplyButton != null) ApplyButton.IsEnabled = false; if (Files != null) Files.ItemsSource = null; }
    private void Destination_Click(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog { Title = "Choose organization destination" }; if (dialog.ShowDialog(this) == true) DestinationRoot.Text = dialog.FolderName; }
    private void Pattern_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => Invalidate();
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = MediaFormats.FileDialogFilter, Title = _organize ? "Choose files to organize" : "Choose files to rename" };
        if (dialog.ShowDialog(this) != true) return;
        var paths = _inputs.Select(input => input.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int omitted = 0;
        foreach (string path in dialog.FileNames)
        {
            if (!paths.Add(path)) continue;
            if (_inputs.Count < FileRenameService.MaximumFiles) _inputs.Add(new(path));
            else omitted++;
        }
        Invalidate(); Status.Text = $"{_inputs.Count:N0} files. Create a new preview."
            + (omitted == 0 ? "" : $" {omitted:N0} additional files exceeded the 10,000-file limit.");
    }
    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        string pattern = Pattern.Text;
        string root = DestinationRoot.Text, folders = FolderPattern.Text;
        Invalidate();
        await RunAsync(async token =>
        {
            var organized = _organize ? await FileOrganizationService.PreviewAsync(_inputs, root, folders, pattern, token) : null;
            var preview = organized?.Entries ?? await FileRenameService.PreviewAsync(_inputs, pattern, token);
            if (_closed) return;
            _plan = preview; _planRoot = organized?.Root;
            Files.ItemsSource = preview.Select(entry => new RenameViewRow(entry.Source, entry.Destination, "Ready", _planRoot)).ToArray();
            Status.Text = $"{preview.Length:N0} destinations validated. Review the paths before applying.";
        });
    }
    private sealed class RenameProgress(Action<RenameResult> report) : IProgress<RenameResult>
    { public void Report(RenameResult value) => report(value); }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null || _plan.IsDefaultOrEmpty) return;
        var plan = _plan;
        string? root = _planRoot;
        await RunAsync(async token =>
        {
            var progress = new RenameProgress(result =>
            {
                if (!result.Renamed || Dispatcher.HasShutdownStarted) return;
                Dispatcher.Invoke(() => _vm.RelocateQueuedSource(result.Entry.Source, result.Entry.Destination));
            });
            string? selected = _vm.CurrentItem?.FilePath;
            bool IsLoaded(string path) => _vm.Engine.IsDisposed || string.Equals(path, selected, StringComparison.OrdinalIgnoreCase) || _vm.Engine.IsSourceInUse(path);
            var summary = root == null ? await FileRenameService.ApplyAsync(plan, _vm.Library, IsLoaded, progress, token)
                : await FileOrganizationService.ApplyAsync(new(root, plan), _vm.Library, IsLoaded, progress, token);
            foreach (var result in summary.Results.Where(result => result.Renamed)) _vm.RelocateQueuedSource(result.Entry.Source, result.Entry.Destination);
            _vm.SaveSettings();
            if (_closed) return;
            int renamed = summary.Results.Count(result => result.Renamed);
            Status.Text = $"{renamed:N0} {(_organize ? "moved" : "renamed")}; {summary.Results.Length - renamed:N0} skipped or failed. "
                + (summary.Canceled ? "Canceled; completed changes remain. " : "") + (summary.IndexError == null ? "" : "Library save failed: " + summary.IndexError)
                + string.Join(" ", summary.Results.Where(result => !result.Renamed).Take(3).Select(result => Path.GetFileName(result.Entry.Source) + ": " + result.Message));
            var results = summary.Results.ToDictionary(result => result.Entry.Source, StringComparer.OrdinalIgnoreCase);
            Files.ItemsSource = plan.Select(entry => new RenameViewRow(entry.Source, entry.Destination,
                results.TryGetValue(entry.Source, out var result) ? result.Message : "Not processed", root)).ToArray();
            _inputs.Clear();
            _inputs.AddRange(plan.Select(entry => new RenameInput(results.TryGetValue(entry.Source, out var result) && result.Renamed ? entry.Destination : entry.Source)));
            _plan = ImmutableArray<RenameEntry>.Empty;
            _planRoot = null;
        });
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        _operation = new(); Pattern.IsEnabled = AddButton.IsEnabled = PreviewButton.IsEnabled = ApplyButton.IsEnabled = false; CancelButton.IsEnabled = true;
        OrganizationOptions.IsEnabled = false;
        Status.Text = "Working…";
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { if (!_closed) Status.Text = "Canceled before completion."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (!_closed) Status.Text = ex.Message; }
        finally
        {
            _operation.Dispose(); _operation = null;
            if (!_closed) { OrganizationOptions.IsEnabled = Pattern.IsEnabled = AddButton.IsEnabled = PreviewButton.IsEnabled = true; ApplyButton.IsEnabled = !_plan.IsDefaultOrEmpty; CancelButton.IsEnabled = false; }
        }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
}
