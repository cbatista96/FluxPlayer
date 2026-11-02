using FluxPlayer.Services;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Emby and Plex browsing, playback relay and downloads against a local fixture
/// server. No real Emby or Plex is installed here, so this proves the routes,
/// credentials, parsing and bounds the player sends and accepts, not that a
/// particular server version answers them the same way.
/// </summary>
internal static class PersonalServerBrowseLoopbackChecks
{
    private const string User = "fixture-user";
    private const string Token = "fixture-session-token";

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerRequest, (string Type, byte[] Body)> _answer;
        public readonly List<string> Paths = new();
        public readonly List<string?> Ranges = new();
        public readonly List<string?> EmbyTokens = new();
        public readonly List<string?> PlexTokens = new();
        public readonly List<string?> Authorizations = new();
        public string Address { get; }

        public Fixture(Func<HttpListenerRequest, (string, byte[])> answer)
        {
            _answer = answer;
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            Address = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Address); _listener.Start(); _ = Loop();
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { return; }
                try
                {
                    lock (Paths)
                    {
                        Paths.Add(context.Request.Url!.PathAndQuery);
                        Ranges.Add(context.Request.Headers["Range"]);
                        EmbyTokens.Add(context.Request.Headers["X-Emby-Token"]);
                        PlexTokens.Add(context.Request.Headers["X-Plex-Token"]);
                        Authorizations.Add(context.Request.Headers["Authorization"]);
                    }
                    var (type, body) = _answer(context.Request);
                    context.Response.ContentType = type;
                    if (context.Request.Headers["Range"] != null)
                    {
                        context.Response.StatusCode = 206;
                        context.Response.Headers["Content-Range"] = $"bytes 0-{body.Length - 1}/{body.Length}";
                    }
                    context.Response.ContentLength64 = body.Length;
                    await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception error) when (error is not OutOfMemoryException) { try { context.Response.Abort(); } catch (ObjectDisposedException) { } }
            }
        }

        public string Path(int index) { lock (Paths) return Paths[index]; }
        public int Count { get { lock (Paths) return Paths.Count; } }
        public void Dispose() { _listener.Close(); }
    }

    private static Fixture Json(string body) => new(_ => ("application/json", Encoding.UTF8.GetBytes(body)));
    private static Fixture Xml(string body) => new(_ => ("application/xml", Encoding.UTF8.GetBytes(body)));

    private const string Items = """
        {"Items":[{"Id":"item-1","Name":"Película ñ","Type":"Movie","MediaSources":[{"Container":"mkv"}]},
                  {"Id":"item-2","Name":"Carpeta","Type":"CollectionFolder","IsFolder":true},
                  {"Id":"item-3","Name":"Canción","Type":"Audio","Album":"Disco","Artists":["Artista"]}]}
        """;

    private static string Directory(string tag, string extra = "") => $"""
        <MediaContainer size="2">
          <Directory key="5" ratingKey="r-5" title="Sección ñ" type="movie" {extra} />
          <{tag} ratingKey="r-9" title="Vídeo" type="movie" grandparentTitle="Serie" parentTitle="Temporada">
            <Media container="mkv"><Part key="/library/parts/9/file.mkv" container="mkv" /></Media>
          </{tag}>
        </MediaContainer>
        """;

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "personal-server-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);

        await test("F0391 Emby loopback: session token header and explicit user identification", async () =>
        {
            using var fixture = Json("""{"Id":"discovered-user","Name":"Fixture"}""");
            using var server = new PersonalMediaServer(PersonalServerKind.Emby, fixture.Address, User, Token);
            await server.Connect(CancellationToken.None);
            Check(fixture.Path(0).Contains("Users/" + User, StringComparison.Ordinal), "A named user is identified through its own route: " + fixture.Path(0));
            lock (fixture.Paths)
            {
                Check(fixture.EmbyTokens[0] == Token, "The session token travels in the Emby header");
                Check(fixture.Authorizations[0] == null, "The Jellyfin authorization scheme is not sent to Emby");
            }

            using var discover = Json("""{"Id":"discovered-user","Name":"Fixture"}""");
            using var anonymous = new PersonalMediaServer(PersonalServerKind.Emby, discover.Address, "", Token);
            await anonymous.Connect(CancellationToken.None);
            Check(discover.Path(0).Contains("Users/Me", StringComparison.Ordinal), "Without a user the current session is asked for");
            var rows = await anonymous.Browse("Roots", "", "", CancellationToken.None);
            Check(discover.Path(1).Contains("Users/discovered-user/Views", StringComparison.Ordinal), "The identified user is reused for later requests: " + discover.Path(1));

            using var refusing = Json("""{"Name":"No identity"}""");
            using var unidentified = new PersonalMediaServer(PersonalServerKind.Emby, refusing.Address, "", Token);
            bool refused = false;
            try { await unidentified.Connect(CancellationToken.None); } catch (IOException) { refused = true; }
            Check(refused, "A server that identifies no user is refused instead of browsed");
        });

        foreach (var (id, view, fragment, name) in new[]
        {
            ("F0392", "Roots", "Users/" + User + "/Views", "library roots"),
            ("F0393", "Movies", "IncludeItemTypes=Movie", "movies"),
            ("F0394", "Series", "IncludeItemTypes=Series", "series"),
            ("F0395", "Music", "IncludeItemTypes=MusicAlbum", "music albums"),
            ("F0397", "Resume", "Items/Resume", "resumable items"),
            ("F0398", "Favorites", "Filters=IsFavorite", "favorites"),
        })
        {
            await test($"{id} Emby loopback: {name} route, bounds and parsed rows", async () =>
            {
                using var fixture = Json(Items);
                using var server = new PersonalMediaServer(PersonalServerKind.Emby, fixture.Address, User, Token);
                var rows = await server.Browse(view, "", "", CancellationToken.None);
                string path = fixture.Path(0);
                Check(path.Contains(fragment, StringComparison.Ordinal), $"Expected {fragment} in {path}");
                if (view is not ("Roots" or "Resume")) Check(path.Contains("Limit=200", StringComparison.Ordinal) || path.Contains("Limit=100", StringComparison.Ordinal), "Browsing stays bounded: " + path);
                Check(rows.Length == 3 && rows[0] is { Id: "item-1", Title: "Película ñ", Kind: "Movie", Folder: false, Extension: "mkv" }, "Items parse with their identity, kind and container");
                Check(rows[1].Folder && rows[2] is { Artist: "Artista", Album: "Disco" }, "Folders and track credits are preserved");
            });
        }

        await test("F0396 Emby loopback: explicit text search with escaped terms", async () =>
        {
            using var fixture = Json(Items);
            using var server = new PersonalMediaServer(PersonalServerKind.Emby, fixture.Address, User, Token);
            var rows = await server.Browse("Search", "", "canción ñ", CancellationToken.None);
            string path = fixture.Path(0);
            Check(path.Contains("SearchTerm=" + Uri.EscapeDataString("canción ñ"), StringComparison.Ordinal), "The search term is escaped into the query: " + path);
            Check(path.Contains("Limit=100", StringComparison.Ordinal) && rows.Length == 3, "Search results stay bounded");
            bool refused = false;
            try { await server.Browse("Search", "", new string('x', 501), CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "An oversized search term is refused before any request");
            refused = false;
            try { await server.Browse("Unknown view", "", "", CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "An unsupported view is refused");
        });

        await test("F0394/F0395 Emby loopback: selected parent children carry the parent identity", async () =>
        {
            using var fixture = Json(Items);
            using var server = new PersonalMediaServer(PersonalServerKind.Emby, fixture.Address, User, Token);
            await server.Browse("Children", "parent-7", "", CancellationToken.None);
            Check(fixture.Path(0).Contains("ParentId=parent-7", StringComparison.Ordinal), "Children are requested for the selected parent: " + fixture.Path(0));
            bool refused = false;
            try { await server.Browse("Children", new string('i', 513), "", CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "An oversized identifier is refused before any request");
        });

        await test("F0399 Emby loopback: original playback relay forwards ranges without upstream tokens", async () =>
        {
            using var fixture = new Fixture(_ => ("video/x-matroska", Encoding.UTF8.GetBytes("media-bytes")));
            using var server = new PersonalMediaServer(PersonalServerKind.Emby, fixture.Address, User, Token);
            var audio = new ServerMediaRow("item-3", "Canción", "Audio", false);
            using (var response = await server.Stream(audio, "bytes=0-3", false, CancellationToken.None))
            {
                Check(fixture.Path(0).Contains("Audio/item-3/stream", StringComparison.Ordinal) && fixture.Path(0).Contains("static=true", StringComparison.Ordinal), "Audio streams directly: " + fixture.Path(0));
                lock (fixture.Paths) Check(fixture.Ranges[0] == "bytes=0-3", "The client range reaches the server");
                Check((int)response.StatusCode == 206 && response.Content.Headers.ContentRange != null, "The partial response and its range are returned to the caller");
            }
            bool refused = false;
            try { _ = await server.Stream(new ServerMediaRow("f", "Carpeta", "Folder", true), null, false, CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "A folder cannot be streamed");
        });

        await test("F0400 Emby loopback: selected downloads publish a new copy and never overwrite", async () =>
        {
            using var fixture = new Fixture(_ => ("application/octet-stream", Encoding.UTF8.GetBytes("payload ñ")));
            using var server = new PersonalMediaServer(PersonalServerKind.Emby, fixture.Address, User, Token);
            var row = new ServerMediaRow("item-1", "Película ñ", "Movie", false, Extension: "mkv");
            string destination = Path.Combine(folder, "Descarga ñ.mkv");
            await server.Download(row, destination, CancellationToken.None);
            Check(fixture.Path(0).Contains("Items/item-1/Download", StringComparison.Ordinal), "Downloads use the explicit download route: " + fixture.Path(0));
            Check(File.ReadAllText(destination, Encoding.UTF8) == "payload ñ", "The published copy holds the served bytes");
            bool refused = false;
            try { await server.Download(row, destination, CancellationToken.None); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(destination, Encoding.UTF8) == "payload ñ", "An existing destination is never overwritten");
        });

        await test("F0401 Plex loopback: server identity and token-authenticated session headers", async () =>
        {
            using var fixture = Xml("""<MediaContainer machineIdentifier="fixture" version="1.40" />""");
            using var server = new PersonalMediaServer(PersonalServerKind.Plex, fixture.Address, "", Token);
            await server.Connect(CancellationToken.None);
            Check(fixture.Path(0).Contains("identity", StringComparison.Ordinal), "The server identity is read first: " + fixture.Path(0));
            lock (fixture.Paths)
            {
                Check(fixture.PlexTokens[0] == Token, "The Plex token travels in its own header");
                Check(fixture.EmbyTokens[0] == null && fixture.Authorizations[0] == null, "No other provider credential is sent to Plex");
            }
        });

        foreach (var (id, view, selected, fragment, name) in new[]
        {
            ("F0402", "Roots", "", "library/sections/all", "library sections"),
            ("F0404", "Search", "", "search?query=", "text search"),
            ("F0405", "Resume", "", "library/onDeck", "on-deck items"),
            ("F0406", "Recent", "", "library/recentlyAdded", "recently added"),
            ("F0407", "Playlists", "", "playlists", "server playlists"),
            ("F0408", "Playlist tracks", "pl-3", "playlists/pl-3/items", "playlist tracks"),
        })
        {
            await test($"{id} Plex loopback: {name} route and bounded parsing", async () =>
            {
                using var fixture = Xml(Directory("Video"));
                using var server = new PersonalMediaServer(PersonalServerKind.Plex, fixture.Address, "", Token);
                var rows = await server.Browse(view, selected, "serie ñ", CancellationToken.None);
                Check(fixture.Path(0).Contains(fragment, StringComparison.Ordinal), $"Expected {fragment} in {fixture.Path(0)}");
                if (view == "Search") Check(fixture.Path(0).Contains(Uri.EscapeDataString("serie ñ"), StringComparison.Ordinal), "The query is escaped: " + fixture.Path(0));
                Check(rows.Length == 2, "Both container rows are returned");
                Check(rows[0].Folder && rows[0].Id == (view == "Roots" ? "section:5" : "r-5"), "Section rows keep a stable identifier for the next request");
                Check(rows[1] is { Folder: false, Artist: "Serie", Album: "Temporada", Resource: "/library/parts/9/file.mkv", Extension: "mkv" }, "Media rows keep their credits and exposed part");
            });
        }

        await test("F0403 Plex loopback: sections and metadata children use their own routes", async () =>
        {
            using var fixture = Xml(Directory("Track"));
            using var server = new PersonalMediaServer(PersonalServerKind.Plex, fixture.Address, "", Token);
            await server.Browse("Children", "section:5", "", CancellationToken.None);
            Check(fixture.Path(0).Contains("library/sections/5/all", StringComparison.Ordinal), "A section browses its own contents: " + fixture.Path(0));
            await server.Browse("Children", "r-9", "", CancellationToken.None);
            Check(fixture.Path(1).Contains("library/metadata/r-9/children", StringComparison.Ordinal), "A metadata item browses its children: " + fixture.Path(1));
            bool refused = false;
            try { await server.Browse("Playing now", "", "", CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check(refused, "An unsupported Plex view is refused");
        });

        await test("F0409/F0410 Plex loopback: exposed part playback and downloaded copies", async () =>
        {
            using var fixture = new Fixture(_ => ("video/x-matroska", Encoding.UTF8.GetBytes("plex-bytes")));
            using var server = new PersonalMediaServer(PersonalServerKind.Plex, fixture.Address, "", Token);
            var row = new ServerMediaRow("r-9", "Vídeo", "movie", false, Resource: "/library/parts/9/file.mkv", Extension: "mkv");
            using (var response = await server.Stream(row, "bytes=2-5", false, CancellationToken.None))
            {
                Check(fixture.Path(0).Contains("library/parts/9/file.mkv", StringComparison.Ordinal), "Playback uses the exposed part key: " + fixture.Path(0));
                lock (fixture.Paths) Check(fixture.Ranges[0] == "bytes=2-5" && fixture.PlexTokens[0] == Token, "The range is forwarded with the session token");
            }
            bool refused = false;
            try { _ = await server.Stream(row with { Resource = "" }, null, false, CancellationToken.None); } catch (IOException) { refused = true; }
            Check(refused, "An item without an exposed part cannot be played");

            string destination = Path.Combine(folder, "Plex ñ.mkv");
            await server.Download(row, destination, CancellationToken.None);
            Check(File.ReadAllText(destination, Encoding.UTF8) == "plex-bytes", "The Plex download publishes a new local copy");
            refused = false;
            try { await server.Download(row, destination, CancellationToken.None); } catch (IOException) { refused = true; }
            Check(refused, "An existing Plex destination is never overwritten");
        });
    }
}
