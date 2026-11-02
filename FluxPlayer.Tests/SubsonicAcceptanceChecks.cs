using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class SubsonicAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action)
    { try { await action(); } catch (Exception ex) when (ex is IOException or ArgumentException or OperationCanceledException) { return; } throw new InvalidOperationException("Expected refusal."); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string executable = Environment.GetEnvironmentVariable("FLUXPLAYER_TEST_NAVIDROME") ?? Path.GetFullPath(Path.Combine(root, "../tools/navidrome-0.64.2/navidrome.exe")); if (!File.Exists(executable)) return;
        string folder = Path.Combine(root, "subsonic-" + Guid.NewGuid().ToString("N")), music = Path.Combine(folder, "music"), data = Path.Combine(folder, "backend"), address = "", artist = "", album = "", playlist = ""; RemoteMusicRow[] songs = []; Process? backend = null; Task<string>? errors = null, output = null; SubsonicSource? source = null; using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Dictionary<string, string> Args(params (string Key, string Value)[] values) => values.ToDictionary(p => p.Key, p => p.Value);
        Task<ServerActionRow[]> Action(string key, params (string Key, string Value)[] values) => source!.ExecuteAction(key, Args(values), CancellationToken.None);
        async Task<JsonElement> Oracle(string method, params (string Key, string Value)[] values)
        {
            string query = string.Join('&', new[] { ("u", "fixture"), ("p", "Fixture-password-2026"), ("v", "1.16.1"), ("c", "FixtureOracle"), ("f", "json") }.Concat(values).Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
            using var reply = await http.GetAsync(address + "rest/" + method + ".view?" + query); reply.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await reply.Content.ReadAsByteArrayAsync()); var body = json.RootElement.GetProperty("subsonic-response"); Check(body.GetProperty("status").GetString() == "ok", "Independent fixture-server oracle accepts request."); return body.Clone();
        }
        async Task Prepare()
        {
            if (source != null) return; Directory.CreateDirectory(music); Directory.CreateDirectory(data);
            foreach (int number in new[] { 1, 2 }) await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=4", "-c:a", "flac", "-metadata", "title=Synthetic " + number, "-metadata", "artist=Fixture Artist", "-metadata", "album_artist=Fixture Artist", "-metadata", "album=Fixture Album", "-metadata", "genre=Fixture", "-metadata", "date=2024", "-metadata", "track=" + number, "-metadata", "lyrics=Authored fixture lyric", Path.Combine(music, "Track" + number + ".flac")], CancellationToken.None);
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop(); address = $"http://127.0.0.1:{port}/";
            var start = new ProcessStartInfo(executable) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var setting in Args(("ADDRESS", "127.0.0.1"), ("PORT", port.ToString()), ("DATAFOLDER", data), ("CACHEFOLDER", Path.Combine(folder, "backend-cache")), ("MUSICFOLDER", music), ("FFMPEGPATH", ConversionService.FindExecutable()!), ("ENABLEEXTERNALSERVICES", "false"), ("ENABLEINSIGHTSCOLLECTOR", "false"), ("ENABLEGRAVATAR", "false"), ("ENABLESTARRATING", "true"), ("LOGLEVEL", "error"), ("PLUGINS_ENABLED", "false"))) start.Environment["ND_" + setting.Key] = setting.Value;
            backend = Process.Start(start)!; errors = backend.StandardError.ReadToEndAsync(); output = backend.StandardOutput.ReadToEndAsync(); var clock = Stopwatch.StartNew();
            while (true) { try { using var ping = await http.GetAsync(address + "ping"); if (ping.IsSuccessStatusCode) break; } catch (HttpRequestException) { } if (backend.HasExited || clock.Elapsed.TotalSeconds > 20) throw new IOException("Isolated Navidrome failed to initialize: " + (backend.HasExited ? await errors : "timeout")); await Task.Delay(80); }
            using (var admin = await http.PostAsync(address + "auth/createAdmin", new StringContent(JsonSerializer.Serialize(new { username = "fixture", password = "Fixture-password-2026" }), Encoding.UTF8, "application/json"))) admin.EnsureSuccessStatusCode();
            source = new(address, "fixture", "Fixture-password-2026"); await source.Ping(CancellationToken.None); await Oracle("startScan"); clock.Restart(); while ((songs = await source.Search("Synthetic", CancellationToken.None)).Length != 2) { if (clock.Elapsed.TotalSeconds > 30) throw new IOException("Isolated Navidrome did not scan its two synthetic tracks."); await Task.Delay(150); }
            artist = (await source.Browse("Artists", "", CancellationToken.None)).Single().Id; album = (await source.Browse("Artist albums", artist, CancellationToken.None)).Single().Id;
            playlist = (await Action("playlist-create", ("name", "Fixture playlist ñ"), ("songs", songs[0].Id + "\n" + songs[0].Id + "\n" + songs[1].Id))).Single().Id;
            await Action("star", ("id", songs[0].Id)); await Action("bookmark-save", ("id", songs[0].Id), ("position", "500"), ("comment", "Fixture bookmark"));
        }
        try
        {
            var supported = ServerActionCatalog.Subsonic.Select((action, index) => (Id: 581 + index, Action: action)).Where(p => p.Action.Key is not ("top-songs" or "similar-songs" or "artist-info" or "album-info"));
            foreach (int id in Enumerable.Range(321, 12).Concat(supported.Select(p => p.Id))) await test($"F{id:0000} Subsonic native acceptance: real isolated Navidrome library", async () =>
            {
                await Prepare(); var active = source!;
                switch (id)
                {
                    case 321: await active.Ping(CancellationToken.None); using (var wrong = new SubsonicSource(address, "fixture", "incorrect")) await Reject(() => wrong.Ping(CancellationToken.None)); break;
                    case 322: Check((await active.Browse("Artists", "", CancellationToken.None)).Single().Title == "Fixture Artist", "Real artist index metadata."); break;
                    case 323: Check((await active.Browse("Artist albums", artist, CancellationToken.None)).Single().Title == "Fixture Album", "Selected artist album navigation."); break;
                    case 324: Check((await active.Browse("Album tracks", album, CancellationToken.None)).Select(s => s.Title).SequenceEqual(new[] { "Synthetic 1", "Synthetic 2" }), "Album tracks retain backend order and titles."); break;
                    case 325: Check((await active.Search("Synthetic 2", CancellationToken.None)).Single().Title == "Synthetic 2", "Track search filters actual remote metadata."); break;
                    case 326: foreach (string type in new[] { "newest", "recent", "random" }) Check((await active.Albums(type, CancellationToken.None)).All(a => a.Kind == "Album"), "Each supported discovery view returns album rows."); break;
                    case 327: Check((await active.Browse("Favorites", "", CancellationToken.None)).Any(s => s.Id == songs[0].Id), "Real starred-track browsing."); break;
                    case 328: Check((await active.Browse("Playlists", "", CancellationToken.None)).Any(p => p.Id == playlist) && (await active.Browse("Playlist tracks", playlist, CancellationToken.None)).Length == 3, "Playlist browsing preserves duplicate song occurrences."); break;
                    case 329:
                        await using (var relay = new RemoteMediaRelay()) { string url = relay.Register((range, token) => active.Stream(songs[0].Id, 0, range, token)); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); engine.SetVolume(0); engine.PlayFile(url); var clock = Stopwatch.StartNew(); while (!engine.IsPlaying || engine.GetTime() < 200) { if (clock.Elapsed.TotalSeconds > 6) throw new IOException("Real original-format Subsonic relay does not play."); await Task.Delay(30); } engine.Stop(); Check(!url.Contains("fixture", StringComparison.OrdinalIgnoreCase), "Queue URL conceals upstream credentials."); } break;
                    case 330:
                        using (var stream = await active.Stream(songs[0].Id, 128, null, CancellationToken.None)) { Check(stream.IsSuccessStatusCode, "Server accepts requested transcoding."); string transcoded = Path.Combine(folder, "Transcoded.mp3"); await File.WriteAllBytesAsync(transcoded, await stream.Content.ReadAsByteArrayAsync()); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); var metadata = await engine.ReadMetadataAsync(transcoded); Check(metadata is { HasAudio: true } && metadata.DurationMs >= 3900, "Requested transcoded output independently decodes as real audio."); } break;
                    case 331:
                        Check(SubsonicSource.DownloadSelection([songs[0], songs[0], songs[1]]).Select(r => r.Id).SequenceEqual(songs.Select(r => r.Id)), "Selected duplicate playlist occurrences download each source once, preserving first-selection order.");
                        string copy = Path.Combine(folder, "Downloaded.flac"); await active.Download(songs[0], copy, CancellationToken.None); Check((await File.ReadAllBytesAsync(copy)).SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(music, songs[0].Title.EndsWith('1') ? "Track1.flac" : "Track2.flac"))), "Original-format download preserves bytes."); await Reject(() => active.Download(songs[0], copy, CancellationToken.None)); break;
                    case 332:
                        var paths = new List<SavedMediaTrack>(); foreach (var song in songs) { string destination = Path.Combine(folder, "Offline-" + song.Title + ".flac"); await active.Download(song, destination, CancellationToken.None); paths.Add(new(destination, song.Title)); } string list = Path.Combine(folder, "Offline.m3u8"); await PlaylistService.WriteCopyAsync(list, paths, CancellationToken.None); Check(PlaylistService.Read(list).SequenceEqual(paths.Select(p => p.Path)) && !File.ReadAllText(list).Contains("127.0.0.1", StringComparison.Ordinal), "Offline M3U contains local downloaded paths without session URLs."); break;
                    default:
                        var definition = ServerActionCatalog.Subsonic[id - 581]; string key = definition.Key; var input = definition.Fields.ToDictionary(f => f.Key, f => f.Default); foreach (string field in input.Keys.ToArray()) if (input[field] == "") input[field] = field switch { "genre" => "Fixture", "query" => "Fixture", "artist" => "Fixture Artist", "title" => "Synthetic 1", "name" => "Changed ñ", "comment" => "Fixture bookmark", "songs" => songs[1].Id, "indices" => "1", "id" => songs[0].Id, _ => throw new InvalidOperationException("Missing fixture field: " + field) };
                        if (key == "year-albums") { input["from"] = "2020"; input["to"] = "2026"; }
                        if (key == "playlist-visibility") input["public"] = "true";
                        if (key == "scrobble") input["time"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds().ToString();
                        string scenarioPlaylist = ""; if (key.StartsWith("playlist-", StringComparison.Ordinal) && key != "playlist-create") { scenarioPlaylist = (await Action("playlist-create", ("name", "Scenario " + id), ("songs", songs[0].Id + "\n" + songs[0].Id + "\n" + songs[1].Id))).Single().Id; input["id"] = scenarioPlaylist; }
                        if (key == "playlist-remove") { await Reject(async () => await active.ExecuteAction(key, input, CancellationToken.None)); var review = await Action("playlist-entries", ("id", scenarioPlaylist)); Check(review.Select(r => r.Occurrence).SequenceEqual(new[] { "0", "1", "2" }), "Remote removal requires reviewed zero-based occurrences."); }
                        if (key is "bookmarks" or "bookmark-remove") await Action("bookmark-save", ("id", songs[0].Id), ("position", "500"), ("comment", "Fixture bookmark"));
                        var rows = await active.ExecuteAction(key, input, CancellationToken.None);
                        if (key is "genres" or "genre-songs" or "random-songs" or "song-info" or "search-albums" or "search-artists" or "playlist-entries" or "year-albums" or "genre-albums" or "bookmarks" or "music-folders") Check(rows.Length > 0, "Real backend returns expected nonempty " + key + " data.");
                        if (key == "playlist-create") Check((await active.Browse("Playlist tracks", rows.Single().Id, CancellationToken.None)).Single().Id == songs[1].Id, "Created playlist has selected song.");
                        if (key == "playlist-add") Check((await active.Browse("Playlist tracks", scenarioPlaylist, CancellationToken.None)).Length == 4, "Append preserves three existing occurrences.");
                        if (key == "playlist-rename") Check((await active.Browse("Playlists", "", CancellationToken.None)).Single(p => p.Id == scenarioPlaylist).Title == "Changed ñ", "Server playlist rename persists Unicode.");
                        if (key == "playlist-visibility") Check((await Oracle("getPlaylist", ("id", scenarioPlaylist))).GetProperty("playlist").GetProperty("public").GetBoolean() == bool.Parse(input["public"]), "Independent server response confirms visibility.");
                        if (key == "playlist-remove") Check((await active.Browse("Playlist tracks", scenarioPlaylist, CancellationToken.None)).Select(s => s.Id).SequenceEqual(new[] { songs[0].Id, songs[1].Id }), "Removal deletes exactly reviewed duplicate occurrence.");
                        if (key is "star" or "unstar") Check((await active.Browse("Favorites", "", CancellationToken.None)).Any(s => s.Id == songs[0].Id) == (key == "star"), "Favorite change is observable at server.");
                        if (key == "rating") Check((await Oracle("getSong", ("id", songs[0].Id))).GetProperty("song").GetProperty("userRating").GetInt32() == 3, "Independent server response confirms rating.");
                        if (key == "lyrics") Check(rows.Any(r => r.Detail.Contains("Authored fixture lyric", StringComparison.Ordinal)), "Authored local lyric is returned by the real server.");
                        if (key == "scrobble") Check((await Oracle("getSong", ("id", songs[0].Id))).GetProperty("song").GetProperty("playCount").GetInt32() >= 1, "Fixture listen increments real server history.");
                        if (key is "bookmark-save" or "bookmark-remove") Check((await Action("bookmarks")).Any(b => b.Id == songs[0].Id) == (key == "bookmark-save"), "Bookmark change is observable at server.");
                        break;
                }
            });
        }
        finally { source?.Dispose(); if (backend != null) { if (!backend.HasExited) backend.Kill(true); await backend.WaitForExitAsync(); if (errors != null && output != null) { await Task.WhenAll(errors, output); await File.WriteAllTextAsync(Path.Combine(folder, "backend.log"), await errors + await output); } backend.Dispose(); } }
    }
}
