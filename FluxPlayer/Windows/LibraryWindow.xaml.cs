using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace FluxPlayer.Windows;

public partial class LibraryWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private LibraryEntry[] _entries = Array.Empty<LibraryEntry>();
    private LibraryEntry[] _filtered = Array.Empty<LibraryEntry>();
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private CancellationTokenSource? _operation;
    private bool _ready, _closed;
    private int _filterGeneration;
    private DuplicateFilesWindow? _duplicatesWindow;

    public LibraryWindow(MainViewModel vm)
    {
        InitializeComponent(); _vm = vm; _ready = true;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await RefreshAsync();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await ApplyFilterAsync(); };
        vm.Library.Changed += Library_Changed;
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); _searchTimer.Stop(); vm.Library.Changed -= Library_Changed; };
    }

    private async void Library_Changed(object? sender, EventArgs e)
    { if (!_closed) await Dispatcher.InvokeAsync(RefreshAsync).Task.Unwrap(); }

    private async Task RefreshAsync()
    {
        _entries = await _vm.Library.SnapshotAsync();
        if (_closed) return;
        SmartListCombo.ItemsSource = await _vm.Library.SmartListsAsync();
        await ApplyFilterAsync();
        if (_vm.Library.LastError != null) StatusText.Text = _vm.Library.LastError;
    }

    private async Task ApplyFilterAsync()
    {
        int generation = ++_filterGeneration;
        string query = SearchBox.Text.Trim(); int kind = KindFilter.SelectedIndex, collection = CollectionFilter.SelectedIndex;
        var source = _entries;
        var filtered = await Task.Run(() =>
        {
            IEnumerable<LibraryEntry> result = source;
            if (kind > 0) result = result.Where(e => e.Kind == (kind == 1 ? "Audio" : "Video"));
            result = collection switch
            {
                1 => result.Where(e => e.Favorite),
                2 => result.Where(e => e.LastPlayedUtc != null).OrderByDescending(e => e.LastPlayedUtc),
                3 => result.Where(e => e.PlayCount > 0).OrderByDescending(e => e.PlayCount),
                4 => result.Where(e => e.PlayCount == 0),
                5 => result.Where(e => !e.MetadataRead),
                _ => result
            };
            if (query.Length > 0)
            {
                result = result.Where(LibraryQuery.Compile(query));
            }
            if (collection is not (2 or 3)) result = result.OrderBy(e => e.Artist).ThenBy(e => e.Album).ThenBy(e => e.Title);
            return result.ToArray();
        });
        if (_closed || generation != _filterGeneration) return;
        _filtered = filtered; MediaGrid.ItemsSource = filtered;
        EmptyText.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"{filtered.Length:N0} of {_entries.Length:N0} files · {source.Count(e => e.Favorite):N0} favorites";
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) { _searchTimer.Stop(); _searchTimer.Start(); } }
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) await ApplyFilterAsync(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var dialog = new OpenFolderDialog { Title = "Add folder to library" };
        if (dialog.ShowDialog(this) != true) return;
        _operation = new(); CancelButton.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<int>(n => { if (!_closed) StatusText.Text = $"Found {n:N0} files…"; });
            await _vm.Library.ScanFolderAsync(dialog.FolderName, WatchNewFolder.IsChecked == true, progress, _operation.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException) { StatusText.Text = "Scan canceled. Files already found have been retained."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = ex.Message; }
        finally { _operation.Dispose(); _operation = null; CancelButton.Visibility = Visibility.Collapsed; }
    }

    private string[] SelectedPaths() => MediaGrid.SelectedItems.Cast<LibraryEntry>().Select(e => e.Path).ToArray();
    private void Play_Click(object sender, RoutedEventArgs e)
    { var paths = SelectedPaths(); if (paths.Length > 0) _vm.AddFilesToPlaylist(paths, true); }
    private void Enqueue_Click(object sender, RoutedEventArgs e) => _vm.AddFilesToPlaylist(SelectedPaths());
    private void Grid_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Play_Click(sender, e); e.Handled = true; } }
    private async void Favorite_Click(object sender, RoutedEventArgs e)
    { foreach (string path in SelectedPaths()) await _vm.Library.ToggleFavoriteAsync(path); await RefreshAsync(); }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var selected = MediaGrid.SelectedItems.Cast<LibraryEntry>().ToArray();
        var candidates = (selected.Length > 0 ? selected : _filtered.Where(x => !x.MetadataRead)).Take(500).ToArray();
        _operation = new(); CancelButton.Visibility = Visibility.Visible;
        try
        {
            int count = 0;
            foreach (var entry in candidates)
            {
                _operation.Token.ThrowIfCancellationRequested();
                var metadata = entry.Path == _vm.CurrentItem?.FilePath ? _vm.Engine.ReadCurrentMetadata()
                    : await _vm.Engine.ReadMetadataAsync(entry.Path, _operation.Token);
                if (metadata != null) await _vm.Library.UpdateMetadataAsync(entry.Path, metadata);
                StatusText.Text = $"Reading tags {++count}/{candidates.Length} (up to 500 per task)…";
            }
            await RefreshAsync();
        }
        catch (OperationCanceledException) { StatusText.Text = "Tag reading canceled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = ex.Message; }
        finally { _operation.Dispose(); _operation = null; CancelButton.Visibility = Visibility.Collapsed; }
    }

    private void EditTags_Click(object sender, RoutedEventArgs e)
    {
        var paths = SelectedPaths();
        if (paths.Length > 1) new BatchTagEditorWindow(this, _vm, paths).Show();
        else if (paths.Length == 1) MediaToolsWindows.ShowTagEditor(this, _vm, paths[0]);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "UTF-8 playlist|*.m3u8|PLS|*.pls|XSPF|*.xspf", FileName = "My library.m3u8" };
        if (dialog.ShowDialog(this) != true) return;
        var selected = MediaGrid.SelectedItems.Cast<LibraryEntry>().ToArray();
        try { PlaylistService.Write(dialog.FileName, (selected.Length > 0 ? selected : _filtered).Select(x => new MediaItem { FilePath = x.Path, Title = x.Title })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = ex.Message; }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Library backup|*.json", FileName = "FluxPlayer-library.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { await _vm.Library.ExportBackupAsync(dialog.FileName); StatusText.Text = "Library backup saved."; }
        catch (IOException ex) { StatusText.Text = ex.Message; }
    }

    private void LibraryTools_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Add(string text, RoutedEventHandler handler)
        { var item = new MenuItem { Header = text }; item.Click += handler; menu.Items.Add(item); }
        Add("Export library backup…", Backup_Click);
        Add("Restore backup (merge)…", RestoreBackup_Click);
        menu.Items.Add(new Separator());
        Add("Find exact duplicates…", (_, _) =>
        {
            if (_duplicatesWindow == null)
            {
                _duplicatesWindow = new DuplicateFilesWindow(this, _vm);
                _duplicatesWindow.Closed += (_, _) => _duplicatesWindow = null;
                _duplicatesWindow.Show();
            }
            else _duplicatesWindow.Activate();
        });
        Add("Remove selected from library", async (_, _) =>
        {
            int removed = await _vm.Library.RemoveEntriesAsync(SelectedPaths());
            await RefreshAsync();
            StatusText.Text = $"Removed {removed:N0} index entries. Files remain on disk; watched folders may add them again.";
        });
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        var dialog = new OpenFileDialog { Title = "Restore library backup", Filter = "FluxPlayer library backup|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var options = new FluentWindow
        {
            Owner = this,
            Title = "Restore library backup",
            Width = 520,
            Height = 250,
            MinHeight = 250,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Merge files and smart playlists into this library. Current favorites and playback counts are preserved.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var watches = new CheckBox { Content = "Also restore monitoring for available folders", IsChecked = false };
        panel.Children.Add(watches);
        var restore = new System.Windows.Controls.Button
        {
            Content = "Merge backup",
            IsDefault = true,
            Padding = new Thickness(20, 8, 20, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        restore.Click += (_, _) => options.DialogResult = true; panel.Children.Add(restore);
        options.SourceInitialized += (_, _) => ThemeService.Apply(_vm.CurrentTheme, options);
        options.Content = ToolWindowChrome.Wrap(options, panel);
        if (options.ShowDialog() != true) return;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation; CancelButton.Visibility = Visibility.Visible;
        try
        {
            StatusText.Text = "Validating and merging backup…";
            var result = await _vm.Library.ImportBackupAsync(dialog.FileName, watches.IsChecked == true, cancellation.Token);
            await RefreshAsync();
            StatusText.Text = $"Backup merged: {result.AddedFiles:N0} new files, {result.MergedFiles:N0} existing files, {result.SmartLists:N0} smart playlists, {result.WatchedFolders:N0} folder watches.";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Backup restore canceled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally { _operation = null; CancelButton.Visibility = Visibility.Collapsed; }
    }

    private async void WatchedFolders_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (string folder in await _vm.Library.FoldersAsync())
        {
            var item = new MenuItem { Header = $"Stop watching: {folder}" };
            item.Click += async (_, _) => { await _vm.Library.StopWatchingAsync(folder); StatusText.Text = "Folder monitoring removed; its files remain in the library."; };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No watched folders", IsEnabled = false });
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async void SmartList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || SmartListCombo.SelectedItem is not SmartList list) return;
        _ready = false; SearchBox.Text = list.Query; KindFilter.SelectedIndex = list.Kind; CollectionFilter.SelectedIndex = list.Collection; _ready = true;
        await ApplyFilterAsync();
    }
    private async void SaveSmartList_Click(object sender, RoutedEventArgs e)
    {
        string? name = MediaToolsWindows.RequestText(this, _vm, "Save smart playlist", "Playlist name");
        if (string.IsNullOrWhiteSpace(name)) return;
        await _vm.Library.SaveSmartListAsync(new SmartList(name, SearchBox.Text, KindFilter.SelectedIndex, CollectionFilter.SelectedIndex));
        SmartListCombo.ItemsSource = await _vm.Library.SmartListsAsync(); StatusText.Text = "Playlist saved. Opening it applies its rules to the current library.";
    }
    private async void RemoveSmartList_Click(object sender, RoutedEventArgs e)
    {
        if (SmartListCombo.SelectedItem is not SmartList list) return;
        await _vm.Library.RemoveSmartListAsync(list.Name); SmartListCombo.ItemsSource = await _vm.Library.SmartListsAsync();
    }
}
