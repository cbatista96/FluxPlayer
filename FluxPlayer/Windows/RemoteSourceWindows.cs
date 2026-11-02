using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public class RemoteSourceWorkbench : FeatureWorkbench
{
    public RemoteSourceWorkbench(Window owner, MainViewModel vm, string title, string description) : base(owner, vm, title, description) { Width = 1120; Height = 800; }
    protected T Section<T>(string name, Func<T> create, bool open = false)
    {
        int start = Form.Children.Count; T result = create(); var content = new StackPanel { Margin = new(12, 12, 12, 4) };
        foreach (var child in Form.Children.Cast<UIElement>().Skip(start).ToArray()) { Form.Children.Remove(child); content.Children.Add(child); }
        Form.Children.Add(new Expander { Header = name, Content = content, IsExpanded = open, Margin = new(0, 0, 0, 10), Padding = new(8) }); return result;
    }
    protected static string? SavePath(Window owner, string name, string extension)
    { var picker = new SaveFileDialog { Filter = "New copy|*" + extension, DefaultExt = extension, FileName = name }; return picker.ShowDialog(owner) == true ? picker.FileName : null; }
    protected static string? OpenPath(Window owner, string filter)
    { var picker = new OpenFileDialog { Filter = filter }; return picker.ShowDialog(owner) == true ? picker.FileName : null; }
    protected static string? Folder(Window owner)
    { var picker = new OpenFolderDialog(); return picker.ShowDialog(owner) == true ? picker.FolderName : null; }
}

public sealed class SubsonicWindow : RemoteSourceWorkbench
{
    private SubsonicSource? _source;
    private RemoteMediaRelay _relay = new();
    public SubsonicWindow(Window owner, MainViewModel vm) : base(owner, vm, "Subsonic music library", "Connect to your HTTPS Subsonic-compatible server. Browse and play explicitly; credentials are session-only. Keep this panel open while relay playback is active.")
    {
        var connection = Section("Connection — credentials are not saved", () => (Server: Text("Server root", "https://", 2048), User: Text("Username", "", 256), Password: Field("Password", new PasswordBox { MaxLength = 1024 })), true);
        var query = Text("Track search", "", 500); var albums = Choice("Album discovery", ["newest", "recent", "random"]); var bitrate = Choice("Transcoded stream bitrate (kb/s)", ["64", "128", "192", "320"], 1); CinemaUi.Compact(Form, 2);
        var table = Table(("Type", "Kind", 1), ("Title", "Title", 3), ("Artist", "Artist", 2), ("Album", "Album", 2));
        var offline = new Dictionary<string, SavedMediaTrack>(StringComparer.Ordinal);
        SubsonicSource Source() => _source ?? throw new ArgumentException("Connect first.");
        RemoteMusicRow Selected(bool track = false) { var row = table.SelectedItem as RemoteMusicRow ?? throw new ArgumentException("Select an entry."); if (track && row.Kind != "Track") throw new ArgumentException("Open an album or playlist, then select tracks."); return row; }
        async Task Browse(string action, string id = "") { table.ItemsSource = await Source().Browse(action, id, OperationToken); if (!IsClosed) Status.Text = action + " loaded (up to 500 entries)."; }
        Action("Connect", async () => { await _relay.DisposeAsync(); _relay = new(); _source?.Dispose(); _source = null; var source = new SubsonicSource(connection.Server.Text.Trim(), connection.User.Text, connection.Password.Password); try { await source.Ping(OperationToken); if (IsClosed) { source.Dispose(); return; } _source = source; offline.Clear(); table.ItemsSource = null; Status.Text = "Connected. Choose Artists, Albums, Favorites, Playlists or Search."; } catch { source.Dispose(); throw; } });
        Action("Artists", () => Browse("Artists"));
        Action("Open selected", async () => { var row = Selected(); if (row.Kind == "Artist") await Browse("Artist albums", row.Id); else if (row.Kind == "Album") await Browse("Album tracks", row.Id); else if (row.Kind == "Playlist") await Browse("Playlist tracks", row.Id); else throw new ArgumentException("Use Play on a track."); });
        Action("Search tracks", async () => { table.ItemsSource = await Source().Search(query.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Search results loaded (up to 100 tracks)."; });
        Action("Discover albums", async () => { table.ItemsSource = await Source().Albums((string)albums.SelectedItem, OperationToken); if (!IsClosed) Status.Text = "Album view loaded (up to 100 albums)."; });
        Action("Favorites", () => Browse("Favorites")); Action("Playlists", () => Browse("Playlists"));
        void Play(int rate, bool queue)
        {
            var source = Source(); var rows = queue ? table.SelectedItems.Cast<RemoteMusicRow>().Where(r => r.Kind == "Track").Take(100).ToArray() : [Selected(true)]; if (rows.Length == 0) throw new ArgumentException("Select tracks.");
            var tracks = rows.Select(row => new SavedMediaTrack(_relay.Register((range, token) => source.Stream(row.Id, rate, range, token)), row.Title)).ToArray();
            vm.AddCatalogTracks(tracks, !queue); Status.Text = "Session stream links added. Closing/reconnecting this panel ends their availability.";
        }
        Action("Play original", () => { Play(0, false); return Task.CompletedTask; });
        Action("Play transcoded", () => { Play(int.Parse((string)bitrate.SelectedItem, CultureInfo.InvariantCulture), false); return Task.CompletedTask; });
        Action("Queue selected", () => { Play(0, true); return Task.CompletedTask; });
        Action("Download selected…", async () =>
        {
            var source = Source(); var rows = SubsonicSource.DownloadSelection(table.SelectedItems.Cast<RemoteMusicRow>()); string? folder = Folder(this); if (folder == null) return;
            int done = 0; foreach (var row in rows)
            {
                OperationToken.ThrowIfCancellationRequested(); string suffix = row.Extension.Length is > 0 and <= 10 && row.Extension.All(char.IsAsciiLetterOrDigit) ? "." + row.Extension : ".media";
                string target = Path.Combine(folder, RemoteMediaIO.SafeName(row.Title) + "-" + RemoteMediaIO.Key(row.Id)[..8] + suffix);
                await source.Download(row, target, OperationToken); offline[row.Id] = new(target, row.Title); if (!IsClosed) Status.Text = $"Downloaded {++done}/{rows.Length}. Completed files are retained on cancellation.";
            }
        });
        Action("Export selected offline playlist…", async () =>
        {
            var rows = table.SelectedItems.Cast<RemoteMusicRow>().Where(r => r.Kind == "Track").Take(100).ToArray(); if (rows.Length == 0 || rows.Any(r => !offline.ContainsKey(r.Id) || !File.Exists(offline[r.Id].Path))) throw new ArgumentException("Download the selected tracks in this session before exporting an offline list.");
            string? path = SavePath(this, "Subsonic offline.m3u8", ".m3u8"); if (path != null) { await PlaylistService.WriteCopyAsync(path, rows.Select(r => offline[r.Id]), OperationToken); if (!IsClosed) Status.Text = "Saved an offline M3U8 with local paths and no server credentials."; }
        });
        CinemaUi.GroupActions(Actions, ("Browse", ["Artists", "Discover albums", "Favorites", "Playlists", "Open selected"]), ("Playback", ["Play original", "Play transcoded", "Queue selected"]), ("Offline", ["Download selected…", "Export selected offline playlist…"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Closed += async (_, _) => { connection.Password.Clear(); await _relay.DisposeAsync(); _source?.Dispose(); };
    }
}

public sealed class WebDavWindow : RemoteSourceWorkbench
{
    private WebDavSource? _source;
    private RemoteMediaRelay _relay = new();
    public WebDavWindow(Window owner, MainViewModel vm) : base(owner, vm, "WebDAV media files", "Connect to a user-provided HTTPS WebDAV root. Downloads create new files. Uploads and collection creation occur only through their explicit commands; keep this panel open for relayed playback.")
    {
        var connection = Section("Connection — session credentials", () => (Root: Text("WebDAV root", "https://", 2048), User: Text("Username (empty for anonymous)", "", 256), Password: Field("Password", new PasswordBox { MaxLength = 1024 })), true);
        var folder = Text("Current collection URL", "", 4096); var name = Text("New remote filename / collection name", "", 100); CinemaUi.Compact(Form, 2);
        var table = Table(("Folder", "Folder", 1), ("Name", "Name", 3), ("Bytes", "Bytes", 1), ("ETag", "ETag", 2));
        WebDavSource Source() => _source ?? throw new ArgumentException("Connect first.");
        DavRow Selected() => table.SelectedItem as DavRow ?? throw new ArgumentException("Select a remote entry.");
        async Task Load(string address) { var rows = await Source().List(address, OperationToken); if (!IsClosed) { folder.Text = address; table.ItemsSource = rows; Status.Text = $"{rows.Length} entries loaded."; } }
        Action("Connect / browse root", async () => { await _relay.DisposeAsync(); _relay = new(); _source?.Dispose(); _source = new(connection.Root.Text.Trim(), connection.User.Text, connection.Password.Password, vm.ProfileDirectory); await Load(_source.Root); });
        Action("Open collection", async () => { var row = Selected(); if (!row.Folder) throw new ArgumentException("Select a collection."); await Load(row.Address); });
        Action("Parent collection", async () => { var current = new Uri(folder.Text.TrimEnd('/') + "/"); await Load(current.AbsoluteUri == Source().Root ? Source().Root : new Uri(current, "../").AbsoluteUri); });
        Action("Refresh collection", () => Load(folder.Text.Trim()));
        Action("Recursive file view", async () => { table.ItemsSource = await Source().Scan(folder.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Recursive view loaded: at most 500 files, 100 folders and depth 3."; });
        Action("Play selected", () => { var row = Selected(); if (row.Folder) throw new ArgumentException("Select a media file."); var source = Source(); string address = _relay.Register((range, token) => source.Stream(row.Address, range, token)); vm.AddCatalogTracks([new(address, row.Name)], true); Status.Text = "Playing through a session-only authenticated relay."; return Task.CompletedTask; });
        Action("Download selected file…", async () => { var row = Selected(); if (row.Folder) throw new ArgumentException("Select a file."); string? target = SavePath(this, RemoteMediaIO.SafeName(row.Name), Path.GetExtension(new Uri(row.Address).AbsolutePath)); if (target != null) { await Source().Download(row, target, OperationToken); if (!IsClosed) Status.Text = "Saved a new local copy."; } });
        Action("Download selected batch…", async () =>
        {
            var rows = table.SelectedItems.Cast<DavRow>().Where(r => !r.Folder).Take(51).ToArray(); if (rows.Length is < 1 or > 50) throw new ArgumentException("Select 1–50 files."); string? target = Folder(this); if (target == null) return;
            int done = 0; foreach (var row in rows) { await Source().Download(row, Path.Combine(target, RemoteMediaIO.SafeName(row.Name)), OperationToken); if (!IsClosed) Status.Text = $"Downloaded {++done}/{rows.Length}. Completed files remain available."; }
        });
        Action("Resume download to new file…", async () => { var row = Selected(); if (row.Folder) throw new ArgumentException("Select a file."); string? target = SavePath(this, RemoteMediaIO.SafeName(row.Name), Path.GetExtension(new Uri(row.Address).AbsolutePath)); if (target != null) { await Source().ResumeDownload(row, target, OperationToken); if (!IsClosed) Status.Text = "Completed the download and published a new file. Partial state is profile-local until completion."; } });
        Action("Refresh offline cache / play", async () => { var row = Selected(); if (row.Folder) throw new ArgumentException("Select a file."); string path = await Source().CachedCopy(row, OperationToken); if (!IsClosed) { vm.AddCatalogTracks([new(path, row.Name)], true); Status.Text = "Opened the cached local copy; ETag revalidation was requested when available."; } });
        Action("Read selected remote M3U", async () => { table.ItemsSource = await Source().ReadPlaylist(Selected(), OperationToken); if (!IsClosed) Status.Text = "Loaded remote playlist entries inside the connected root. Select an item to play/download."; });
        Action("Upload new remote file…", async () => { string? local = OpenPath(this, "Local file|*.*"); if (local == null) return; string filename = name.Text.Trim() == "" ? Path.GetFileName(local) : name.Text.Trim(); await Source().Upload(local, folder.Text.Trim(), filename, OperationToken); if (!IsClosed) Status.Text = "Uploaded a new remote file with a no-replace precondition."; });
        Action("Create remote collection", async () => { await Source().CreateFolder(folder.Text.Trim(), name.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Created the requested remote collection."; });
        CinemaUi.GroupActions(Actions, ("Browse", ["Open collection", "Parent collection", "Refresh collection", "Recursive file view", "Read selected remote M3U"]), ("Local copies", ["Download selected file…", "Download selected batch…", "Resume download to new file…", "Refresh offline cache / play"]), ("Remote writes", ["Upload new remote file…", "Create remote collection"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Closed += async (_, _) => { connection.Password.Clear(); await _relay.DisposeAsync(); _source?.Dispose(); };
    }
}
