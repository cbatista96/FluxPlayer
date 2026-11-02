using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class PersonalServerWindow : RemoteSourceWorkbench
{
    private PersonalMediaServer? _server;
    private RemoteMediaRelay _relay = new();
    public PersonalServerWindow(Window owner, MainViewModel vm, PersonalServerKind kind) : base(owner, vm, kind + " library", "Connect to your server with an existing user token. Tokens stay in this panel session; keep the panel open for relayed playback. Direct media only; no DRM or adaptive-transcode negotiation.")
    {
        var connection = Section("Session connection", () => (Root: Text("Server API root (include reverse-proxy /emby path when required)", "https://", 2048), User: Text("User ID (empty uses current user; not used by Plex)", "", 128), Token: Field("Session API token", new PasswordBox { MaxLength = 2048 })), true);
        var query = Text("Server search", "", 500); CinemaUi.Compact(Form, 2); var table = Table(("Kind", "Kind", 1), ("Title", "Title", 3), ("Artist", "Artist", 2), ("Album", "Album", 2));
        PersonalMediaServer Server() => _server ?? throw new ArgumentException("Connect first.");
        ServerMediaRow Selected() => table.SelectedItem as ServerMediaRow ?? throw new ArgumentException("Select an item.");
        async Task Browse(string view, string id = "") { var rows = await Server().Browse(view, id, query.Text.Trim(), OperationToken); if (!IsClosed) { table.ItemsSource = rows; Status.Text = view + $": {rows.Length} rows (bounded view; use a narrower collection when needed)."; } }
        Action("Connect", async () => { await _relay.DisposeAsync(); _relay = new(); _server?.Dispose(); _server = null; var server = new PersonalMediaServer(kind, connection.Root.Text.Trim(), connection.User.Text, connection.Token.Password); try { await server.Connect(OperationToken); if (IsClosed) { server.Dispose(); return; } _server = server; await Browse("Roots"); } catch { server.Dispose(); _server = null; throw; } });
        Action("Library roots", () => Browse("Roots"));
        Action("Open selected collection", () => { var row = Selected(); if (!row.Folder) throw new ArgumentException("Select a collection, series, season, album or playlist."); return Browse(row.Kind.Equals("playlist", StringComparison.OrdinalIgnoreCase) ? "Playlist tracks" : "Children", row.Id); });
        Action("Search", () => Browse("Search")); Action("Continue watching / listening", () => Browse("Resume"));
        if (kind == PersonalServerKind.Plex) { Action("Recently added", () => Browse("Recent")); Action("Server playlists", () => Browse("Playlists")); }
        else { Action("Movies", () => Browse("Movies")); Action("Series and seasons", () => Browse("Series")); Action("Music albums", () => Browse("Music")); Action("Server favorites", () => Browse("Favorites")); }
        Action("Play selected media", () => { var row = Selected(); if (row.Folder) throw new ArgumentException("Open a collection first."); var server = Server(); string address = _relay.Register((range, token) => server.Stream(row, range, false, token)); vm.AddCatalogTracks([new(address, row.Title)], true); Status.Text = "Opened a session relay URL. Closing/reconnecting ends its availability; upstream token is not stored in the queue."; return Task.CompletedTask; });
        Action("Download original copy…", async () => { var row = Selected(); if (row.Folder) throw new ArgumentException("Select media."); string ext = row.Extension.Length is > 0 and <= 10 && row.Extension.All(char.IsAsciiLetterOrDigit) ? "." + row.Extension : ".media"; string? target = SavePath(this, RemoteMediaIO.SafeName(row.Title) + ext, ext); if (target != null) { await Server().Download(row, target, OperationToken); if (!IsClosed) Status.Text = "Saved a new original-media copy. Access depends on server permissions."; } });
        CinemaUi.GroupActions(Actions, ("Library", kind == PersonalServerKind.Plex ? ["Library roots", "Open selected collection", "Continue watching / listening", "Recently added", "Server playlists"] : ["Library roots", "Open selected collection", "Movies", "Series and seasons", "Music albums", "Continue watching / listening", "Server favorites"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += async (_, _) => { connection.Token.Clear(); await _relay.DisposeAsync(); _server?.Dispose(); };
    }
}
