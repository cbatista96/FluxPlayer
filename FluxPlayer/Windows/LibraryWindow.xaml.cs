using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Controls;
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
    private int _refreshGeneration;
    private bool _columnViewApplied;
    private DuplicateFilesWindow? _duplicatesWindow;

    public LibraryWindow(MainViewModel vm)
    {
        InitializeComponent(); _vm = vm; _ready = true; Height = 740; MinHeight = 620;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await RefreshAsync();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await ApplyFilterAsync(); };
        vm.Library.Changed += Library_Changed;
        Closed += (_, _) =>
        {
            _closed = true; _operation?.Cancel(); _browseOperation?.Cancel(); _searchTimer.Stop(); vm.Library.Changed -= Library_Changed;
            if (_columnViewApplied) _vm.SaveLibraryColumnView(LibraryGridLayout.Capture(MediaGrid));
        };
    }

    private async void Library_Changed(object? sender, EventArgs e)
    { if (!_closed) await Dispatcher.InvokeAsync(RefreshAsync).Task.Unwrap(); }

    private async Task RefreshAsync()
    {
        int generation = ++_refreshGeneration;
        var entries = await _vm.Library.SnapshotAsync();
        var lists = await _vm.Library.SmartListsAsync();
        var collections = await _vm.Library.CollectionsAsync();
        var collectionChoices = await Task.Run(() => LibraryCollections.Choices(collections));
        if (_closed || generation != _refreshGeneration) return;
        _entries = entries;
        RefreshCollections(collections, collectionChoices);
        SmartListCombo.ItemsSource = lists;
        await ApplyFilterAsync();
        if (_vm.Library.LastError != null) StatusText.Text = _vm.Library.LastError;
    }

    private string? _artist, _album, _label, _genre;
    private int? _decade;
    private bool _updatingBrowser;
    private CancellationTokenSource? _browseOperation;

    private async Task ApplyFilterAsync()
    {
        int generation = ++_filterGeneration;
        _browseOperation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _browseOperation = cancellation;
        string query = SearchBox.Text.Trim(); int kind = KindFilter.SelectedIndex, collection = CollectionFilter.SelectedIndex;
        string? artist = _artist, album = _album, label = _label, genre = _genre; int? decade = _decade;
        var source = _entries;
        var collections = _userCollections; string? collectionId = _collectionId; bool includeChildren = IncludeChildCollections.IsChecked == true;
        var resumePositions = _vm.CaptureResumePositions();
        try
        {
            var result = await Task.Run(() =>
            {
                var members = collectionId == null ? null : LibraryCollections.Members(collections, collectionId, includeChildren);
                var view = LibraryBrowser.Browse(members == null ? source : source.Where(entry => members.Contains(entry.Path)), query, kind, collection, artist, album, label, cancellation.Token, genre, decade);
                var rows = view.Entries.Select(entry => entry with { RemainingMs = MainViewModel.RemainingTime(entry, resumePositions) }).ToArray();
                return (View: view, Rows: rows, Favorites: source.Count(entry => entry.Favorite));
            }, cancellation.Token);
            if (_closed || generation != _filterGeneration) return;
            var columnView = _columnViewApplied ? LibraryGridLayout.Capture(MediaGrid) : _vm.LibraryColumnView;
            _filtered = result.Rows; MediaGrid.ItemsSource = _filtered;
            LibraryGridLayout.Apply(MediaGrid, columnView); _columnViewApplied = true;
            _updatingBrowser = true;
            try
            {
                GenreBrowser.ItemsSource = result.View.Genres;
                GenreBrowser.SelectedItem = result.View.Genres.First(item => string.Equals(item.Value, genre == null ? null : LibraryBrowser.Key(genre), StringComparison.OrdinalIgnoreCase));
                DecadeBrowser.ItemsSource = result.View.Decades;
                DecadeBrowser.SelectedItem = result.View.Decades.First(item => item.Value == decade?.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ArtistBrowser.ItemsSource = result.View.Artists;
                ArtistBrowser.SelectedItem = result.View.Artists.First(item => string.Equals(item.Value, artist == null ? null : LibraryBrowser.Key(artist), StringComparison.OrdinalIgnoreCase));
                AlbumBrowser.ItemsSource = result.View.Albums;
                AlbumBrowser.SelectedItem = result.View.Albums.First(item => string.Equals(item.Value, album == null ? null : LibraryBrowser.Key(album), StringComparison.OrdinalIgnoreCase));
                LabelBrowser.ItemsSource = result.View.Labels;
                LabelBrowser.SelectedItem = result.View.Labels.First(item => string.Equals(item.Value, label, StringComparison.OrdinalIgnoreCase));
            }
            finally { _updatingBrowser = false; }
            EmptyText.Text = source.Length == 0 ? "Add a folder to get started. Files you play will also appear here."
                : "No files match this view. Change the search, filters or browsing selection.";
            EmptyText.Visibility = _filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = string.Format(Services.Localizer.T("{0} of {1} files · {2} favorites"), _filtered.Length.ToString("N0"), source.Length.ToString("N0"), result.Favorites.ToString("N0"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { if (_browseOperation == cancellation) _browseOperation = null; }
    }

    private async void ArtistBrowser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingBrowser || ArtistBrowser.SelectedItem is not LibraryFacet item) return;
        _artist = item.Value; _album = _label = null;
        await ApplyFilterAsync();
    }

    private async void AlbumBrowser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingBrowser || AlbumBrowser.SelectedItem is not LibraryFacet item) return;
        _album = item.Value; _label = null;
        await ApplyFilterAsync();
    }

    private async void ResetBrowser_Click(object sender, RoutedEventArgs e)
    { _artist = _album = _label = _genre = null; _decade = null; await ApplyFilterAsync(); }

    private async void GenreBrowser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingBrowser || GenreBrowser.SelectedItem is not LibraryFacet item) return;
        _genre = item.Value; _decade = null; _artist = _album = _label = null; await ApplyFilterAsync();
    }
    private async void DecadeBrowser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingBrowser || DecadeBrowser.SelectedItem is not LibraryFacet item) return;
        _decade = item.Value == null ? null : int.Parse(item.Value, System.Globalization.CultureInfo.InvariantCulture); _artist = _album = _label = null; await ApplyFilterAsync();
    }

    private async void LabelBrowser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingBrowser || LabelBrowser.SelectedItem is not LibraryFacet item) return;
        _label = item.Value;
        await ApplyFilterAsync();
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

    private void PersonalLabels_Click(object sender, RoutedEventArgs e)
    {
        var selected = MediaGrid.SelectedItems.Cast<LibraryEntry>().Take(10_001).ToArray();
        if (selected.Length == 0) { StatusText.Text = "Select library files to edit their personal labels."; return; }
        if (selected.Length > 10_000) { StatusText.Text = "Select up to 10,000 files per label edit."; return; }
        new LibraryLabelsWindow(this, _vm, selected).Show();
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
        var columns = new MenuItem { Header = "Columns" };
        foreach (var definition in LibraryColumnViews.Definitions)
        {
            var column = MediaGrid.Columns.First(item => item.SortMemberPath == definition.Key);
            var choice = new MenuItem { Header = definition.Name, IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible, IsEnabled = definition.Key != "Title" };
            choice.Click += (_, _) => { column.Visibility = choice.IsChecked ? Visibility.Visible : Visibility.Collapsed; SaveColumnView(); };
            columns.Items.Add(choice);
        }
        menu.Items.Add(columns);
        Add("Save column view…", (_, _) =>
        {
            string? name = MediaToolsWindows.RequestText(this, _vm, "Save library column view", "View name (columns, widths, order and sorting)");
            if (!string.IsNullOrWhiteSpace(name)) SaveColumnView(name);
        });
        var views = new MenuItem { Header = "Restore column view" };
        var removeViews = new MenuItem { Header = "Remove saved column view" };
        foreach (var pair in _vm.SavedLibraryColumnViews.OrderBy(pair => pair.Key))
        {
            var choice = new MenuItem { Header = pair.Key };
            choice.Click += (_, _) => { LibraryGridLayout.Apply(MediaGrid, pair.Value); SaveColumnView(); }; views.Items.Add(choice);
            var remove = new MenuItem { Header = pair.Key };
            remove.Click += (_, _) => SaveColumnView(removeName: pair.Key); removeViews.Items.Add(remove);
        }
        views.IsEnabled = views.Items.Count > 0; removeViews.IsEnabled = removeViews.Items.Count > 0;
        menu.Items.Add(views); menu.Items.Add(removeViews);
        Add("Reset columns and sorting", (_, _) => { LibraryGridLayout.Apply(MediaGrid, LibraryColumnViews.Default); SaveColumnView(); });
        menu.Items.Add(new Separator());
        Add("Manage collections…", ManageCollections_Click);
        Add("Add selected to current collection", async (_, _) => await EditCollectionMembersAsync(true));
        Add("Remove selected from current collection", async (_, _) => await EditCollectionMembersAsync(false));
        menu.Items.Add(new Separator());
        Add("Export library backup…", Backup_Click);
        Add("Organize selected files…", (_, _) =>
        {
            var selected = MediaGrid.SelectedItems.Cast<LibraryEntry>().Select(entry => new RenameInput(entry.Path, entry.Title, entry.Artist, entry.Album)).ToArray();
            if (selected.Length == 0) { StatusText.Text = "Select files to preview organization into metadata folders."; return; }
            new FileRenameWindow(this, _vm, true, selected).Show();
        });
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

    private void SaveColumnView(string? name = null, string? removeName = null)
    {
        try
        {
            bool saved = _vm.SaveLibraryColumnView(LibraryGridLayout.Capture(MediaGrid), name, removeName);
            StatusText.Text = saved ? "Column view saved. Search and collection rules remain separate smart-playlist criteria."
                : "Column view could not be saved: " + _vm.LibraryViewSaveError;
        }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
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
        _ready = false; SearchBox.Text = list.Query; KindFilter.SelectedIndex = list.Kind; CollectionFilter.SelectedIndex = list.Collection;
        _artist = list.Artist; _album = list.Album; _label = list.Label; _genre = list.Genre; _decade = list.Decade;
        _collectionId = list.CollectionId; UserCollectionCombo.SelectedValue = _collectionId;
        IncludeChildCollections.IsChecked = list.IncludeChildCollections; _ready = true;
        await ApplyFilterAsync();
    }
    private async void SaveSmartList_Click(object sender, RoutedEventArgs e)
    {
        string? name = MediaToolsWindows.RequestText(this, _vm, "Save smart playlist", "Playlist name");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { await _vm.Library.SaveSmartListAsync(new SmartList(name, SearchBox.Text, KindFilter.SelectedIndex, CollectionFilter.SelectedIndex, _artist, _album, _label, _collectionId, IncludeChildCollections.IsChecked == true, _genre, _decade)); }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
        SmartListCombo.ItemsSource = await _vm.Library.SmartListsAsync(); StatusText.Text = "Playlist saved. Opening it applies its rules to the current library.";
    }
    private async void RemoveSmartList_Click(object sender, RoutedEventArgs e)
    {
        if (SmartListCombo.SelectedItem is not SmartList list) return;
        await _vm.Library.RemoveSmartListAsync(list.Name); SmartListCombo.ItemsSource = await _vm.Library.SmartListsAsync();
    }
}
