using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class MediaCatalogWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly bool _radio;
    private MediaCatalogSnapshot? _data;
    private bool _ready, _closed, _updating, _busy;
    private string? _selectedId;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly CancellationTokenSource _lifetime = new();
    private int _filterGeneration;
    private sealed record Choice(string Id, string Display);
    private sealed record TrackRow(int Index, string Title, string Path) { public int Number => Index + 1; }
    public MediaCatalogWindow(Window owner, MainViewModel vm, bool radio)
    {
        InitializeComponent(); Owner = owner; _vm = vm; _radio = radio;
        Heading.Text = radio ? "Internet radio" : "Saved playlists"; Title = Heading.Text + " · FluxPlayer";
        Description.Text = radio ? "Organize your own stream addresses. Stations connect only when you choose Play; availability depends on the source."
            : "Keep several ordered lists, search across their contents, and lock lists against accidental edits. Changes save immediately.";
        NewButton.Content = radio ? "New station" : "New playlist…"; SaveButton.Content = radio ? "Save station" : "Save name";
        RadioFields.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        PlaylistActions.Visibility = ProtectButton.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        FavoritesOnly.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        if (radio) Tracks.Visibility = Visibility.Collapsed;
        var content = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, content);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        _ready = true; Loaded += async (_, _) => await Run(() => RefreshAsync());
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await FilterAsync(); };
        Closed += (_, _) => { _closed = true; _debounce.Stop(); _lifetime.Cancel(); };
    }
    private SavedMediaPlaylist? SelectedList => _data?.Playlists.FirstOrDefault(item => item.Id == _selectedId);
    private RadioStation? SelectedStation => _data?.Stations.FirstOrDefault(item => item.Id == _selectedId);
    private async Task RefreshAsync(string? select = null)
    {
        _data = await _vm.MediaCatalog.SnapshotAsync(_lifetime.Token); if (_closed) return;
        _selectedId = select ?? _selectedId; await FilterAsync();
    }
    private async Task FilterAsync()
    {
        if (_data == null || _closed) return;
        var data = _data; string query = Search.Text.Trim(); bool favorites = FavoritesOnly.IsChecked == true; int generation = ++_filterGeneration;
        try
        {
            var choices = await Task.Run(() => _radio ? data.Stations.Where(item => (!favorites || item.Favorite)
                    && (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Url.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Genre.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Country.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(item => item.Favorite).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => new Choice(item.Id, (item.Favorite ? "★ " : "") + item.Name)).ToArray()
                : data.Playlists.Where(list => list.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || list.Tracks.Any(track => Matches(track, query)))
                    .OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase).Select(list => new Choice(list.Id, list.Display)).ToArray(), _lifetime.Token);
            if (_closed || generation != _filterGeneration) return;
            _updating = true; CatalogList.ItemsSource = choices; CatalogList.SelectedItem = choices.FirstOrDefault(item => item.Id == _selectedId); _updating = false;
            if (CatalogList.SelectedItem == null) _selectedId = null;
            ShowSelection(); Status.Text = $"{choices.Length:N0} matching {(_radio ? "stations" : "lists")} · {(_radio ? data.Stations.Length : data.Playlists.Length):N0} saved";
        }
        catch (OperationCanceledException) { }
    }
    private static bool Matches(SavedMediaTrack track, string query) => track.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || track.Path.Contains(query, StringComparison.OrdinalIgnoreCase);
    private void ShowSelection()
    {
        if (_radio)
        {
            var station = SelectedStation; NameInput.Text = station?.Name ?? ""; UrlInput.Text = station?.Url ?? ""; GenreInput.Text = station?.Genre ?? "";
            CountryInput.Text = station?.Country ?? ""; StationFavorite.IsChecked = station?.Favorite ?? false;
            UpdateEditState(); return;
        }
        var list = SelectedList; NameInput.Text = list?.Name ?? "";
        string query = Search.Text.Trim();
        Tracks.ItemsSource = list?.Tracks.Select((track, index) => new TrackRow(index, track.Title, track.Path))
            .Where(row => query.Length == 0 || list.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || Matches(new(row.Path, row.Title), query)).ToArray();
        UpdateEditState();
    }
    private void UpdateEditState()
    {
        if (_radio) { SaveButton.IsEnabled = !_busy; NameInput.IsReadOnly = false; RemoveButton.IsEnabled = SelectedStation != null && !_busy; return; }
        var list = SelectedList; bool editable = list != null && !list.Protected && !_busy;
        NameInput.IsReadOnly = !editable; SaveButton.IsEnabled = PlaylistActions.IsEnabled = editable;
        ProtectButton.IsEnabled = list != null && !_busy; ProtectButton.Content = list?.Protected == true ? "Unlock playlist" : "Lock playlist";
        RemoveButton.IsEnabled = editable;
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && !_updating) { _selectedId = (CatalogList.SelectedItem as Choice)?.Id; ShowSelection(); } }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) { _debounce.Stop(); _debounce.Start(); } }
    private async void FavoriteFilter_Changed(object sender, RoutedEventArgs e) { if (_ready) await FilterAsync(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(() => RefreshAsync());
    private async Task Run(Func<Task> work)
    {
        if (_busy || _closed) return; _busy = true; Body.IsEnabled = false;
        try { await work(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or System.Xml.XmlException or OperationCanceledException)
        { if (!_closed) Status.Text = ex.Message; }
        finally { _busy = false; if (!_closed) { Body.IsEnabled = true; UpdateEditState(); } }
    }
    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (_radio) { _selectedId = null; _updating = true; CatalogList.SelectedItem = null; _updating = false; ShowSelection(); NameInput.Focus(); return; }
        string? name = MediaToolsWindows.RequestText(this, _vm, "New saved playlist", "Playlist name"); if (string.IsNullOrWhiteSpace(name)) return;
        await Run(async () => { string id = Guid.NewGuid().ToString("N"); await _vm.MediaCatalog.SavePlaylistAsync(id, name, Array.Empty<SavedMediaTrack>(), token: _lifetime.Token); await RefreshAsync(id); });
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var list = SelectedList; var station = SelectedStation;
        // Capture the entire draft before awaiting or refreshing selection.
        string name = NameInput.Text, url = UrlInput.Text, genre = GenreInput.Text, country = CountryInput.Text; bool favorite = StationFavorite.IsChecked == true;
        await Run(async () =>
        {
            if (_radio) { var revised = new RadioStation(station?.Id ?? Guid.NewGuid().ToString("N"), name, url, genre, country, favorite); await _vm.MediaCatalog.SaveStationAsync(revised, _lifetime.Token); await RefreshAsync(revised.Id); }
            else if (list != null) { await _vm.MediaCatalog.SavePlaylistAsync(list.Id, name, list.Tracks, list.Revision, _lifetime.Token); await RefreshAsync(list.Id); }
        });
    }
    private async void Protect_Click(object sender, RoutedEventArgs e)
    { var list = SelectedList; if (list != null) await Run(async () => { await _vm.MediaCatalog.SetProtectionAsync(list.Id, list.Revision, !list.Protected, _lifetime.Token); await RefreshAsync(list.Id); }); }
    private async Task ChangeTracks(Func<SavedMediaPlaylist, IEnumerable<SavedMediaTrack>> change)
    {
        var list = SelectedList; if (list == null) { Status.Text = "Choose a saved playlist first."; return; }
        await Run(async () => { var tracks = change(list); await _vm.MediaCatalog.SavePlaylistAsync(list.Id, list.Name, tracks, list.Revision, _lifetime.Token); await RefreshAsync(list.Id); });
    }
    private async void AppendQueue_Click(object sender, RoutedEventArgs e)
    { var tracks = _vm.Playlist.Select(item => new SavedMediaTrack(item.FilePath, item.Title)).ToArray(); await ChangeTracks(list => list.Tracks.Concat(tracks)); }
    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add files to saved playlist", Filter = MediaFormats.FileDialogFilter, Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        var tracks = dialog.FileNames.Select(path => new SavedMediaTrack(path, Path.GetFileNameWithoutExtension(path))).ToArray(); await ChangeTracks(list => list.Tracks.Concat(tracks));
    }
    private async void RemoveTracks_Click(object sender, RoutedEventArgs e)
    { var indexes = Tracks.SelectedItems.Cast<TrackRow>().Select(row => row.Index).ToHashSet(); await ChangeTracks(list => list.Tracks.Where((_, index) => !indexes.Contains(index))); }
    private async void Up_Click(object sender, RoutedEventArgs e) => await Move(-1);
    private async void Down_Click(object sender, RoutedEventArgs e) => await Move(1);
    private async Task Move(int direction)
    {
        if (Search.Text.Length != 0 || Tracks.SelectedItems.Count != 1) { Status.Text = "Clear search and select one entry to reorder it."; return; }
        if (Tracks.SelectedItems[0] is not TrackRow selected) return;
        int index = selected.Index;
        await ChangeTracks(list => { var items = list.Tracks.ToList(); int target = Math.Clamp(index + direction, 0, items.Count - 1); (items[index], items[target]) = (items[target], items[index]); return items; });
    }
    private async void Sort_Click(object sender, RoutedEventArgs e) => await ChangeTracks(list => list.Tracks.OrderBy(track => track.Title, StringComparer.OrdinalIgnoreCase));
    private async void Deduplicate_Click(object sender, RoutedEventArgs e) => await ChangeTracks(list => list.Tracks.DistinctBy(track => track.Path, StringComparer.Ordinal));
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Media playlists|*.m3u;*.m3u8;*.pls;*.xspf" }; if (dialog.ShowDialog(this) != true) return;
        string? name = _radio ? null : MediaToolsWindows.RequestText(this, _vm, "Import saved playlist", "New playlist name"); if (!_radio && string.IsNullOrWhiteSpace(name)) return;
        await Run(async () =>
        {
            var tracks = await Task.Run(() => PlaylistService.ReadEntries(dialog.FileName), _lifetime.Token);
            if (_radio) { await _vm.MediaCatalog.ImportStationsAsync(tracks, _lifetime.Token); await RefreshAsync(); }
            else { string id = Guid.NewGuid().ToString("N"); await _vm.MediaCatalog.SavePlaylistAsync(id, name!, tracks, token: _lifetime.Token); await RefreshAsync(id); }
        });
    }
    private IEnumerable<SavedMediaTrack> PlaybackTracks()
    {
        if (_radio) return SelectedStation is { } station ? new[] { new SavedMediaTrack(station.Url, station.Name) } : Array.Empty<SavedMediaTrack>();
        var selected = Tracks.SelectedItems.Cast<TrackRow>().OrderBy(row => row.Index).Select(row => new SavedMediaTrack(row.Path, row.Title)).ToArray();
        return selected.Length > 0 ? selected : SelectedList?.Tracks ?? ImmutableArray<SavedMediaTrack>.Empty;
    }
    private void Play_Click(object sender, RoutedEventArgs e) => AddPlayback(true);
    private void Enqueue_Click(object sender, RoutedEventArgs e) => AddPlayback(false);
    private void AddPlayback(bool play) { int count = _vm.AddCatalogTracks(PlaybackTracks(), play); Status.Text = count == 0 ? "Choose available entries or a station. Missing local files were skipped." : $"Added {count:N0} entries to the playback queue."; }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var visibleIds = CatalogList.Items.Cast<Choice>().Select(item => item.Id).ToHashSet();
        SavedMediaTrack[] entries = _radio ? (_data?.Stations.Where(item => visibleIds.Contains(item.Id)).Select(item => new SavedMediaTrack(item.Url, item.Name)).ToArray() ?? Array.Empty<SavedMediaTrack>())
            : SelectedList?.Tracks.ToArray() ?? Array.Empty<SavedMediaTrack>();
        var dialog = new SaveFileDialog { Title = "Export a new playlist copy", Filter = "UTF-8 playlist|*.m3u8|PLS|*.pls|XSPF|*.xspf", FileName = _radio ? "My radio.m3u8" : "My playlist.m3u8" };
        if (dialog.ShowDialog(this) != true) return;
        await Run(async () => { await PlaylistService.WriteCopyAsync(dialog.FileName, entries, _lifetime.Token); if (!_closed) Status.Text = "New playlist copy saved."; });
    }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var list = SelectedList; var station = SelectedStation; if (list == null && station == null) return;
        if (MessageBox.Show(this, "Remove this saved item? Media files and the playback queue will remain unchanged.", "Remove saved item", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Run(async () => { if (_radio) await _vm.MediaCatalog.RemoveStationAsync(station!.Id, _lifetime.Token); else await _vm.MediaCatalog.RemovePlaylistAsync(list!.Id, list.Revision, _lifetime.Token); _selectedId = null; await RefreshAsync(); });
    }
}
