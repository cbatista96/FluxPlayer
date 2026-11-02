using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;

internal static class ExtendedMediaChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Refused(Func<Task> action)
    {
        bool refused = false; try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException or System.Xml.XmlException or System.Net.Http.HttpRequestException) { refused = true; }
        Check(refused, "Invalid, canceled or conflicting action must be rejected.");
    }
    private static async Task Wait(Func<bool> condition, string message = "Native transport condition")
    { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.ElapsedMilliseconds > 8000) throw new TimeoutException(message); await Task.Delay(60); } }
    private sealed class InlineProgress(Action<long> report) : IProgress<long> { public void Report(long value) => report(value); }
    internal sealed class HttpFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentBag<Task> _clients = new();
        private readonly Task _accept;
        public readonly ConcurrentDictionary<string, int> Requests = new();
        public readonly ConcurrentDictionary<string, byte[]> Documents = new();
        public string Address { get; }
        public HttpFixture(string wav)
        {
            _listener.Start(); Address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            Documents["/episode.wav"] = File.ReadAllBytes(wav);
            Documents["/feed"] = Encoding.UTF8.GetBytes("<rss version='2.0' xmlns:i='urn:itunes'><channel><title>Podcast ñ</title><item><title>Episode ñ</title><enclosure url='/episode.wav' type='audio/wav'/><i:duration>0:08</i:duration><pubDate>Fri, 23 Oct 2026 12:00:00 GMT</pubDate></item></channel></rss>");
            _accept = Accept();
        }
        private async Task Accept()
        {
            try { while (!_lifetime.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_lifetime.Token); _clients.Add(Serve(client)); } }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
        private async Task Serve(TcpClient client)
        {
            using (client)
            try
            {
                using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                string line = await reader.ReadLineAsync(_lifetime.Token) ?? ""; string path = line.Split(' ').ElementAtOrDefault(1)?.Split('?')[0] ?? "/";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_lifetime.Token))) { }
                Requests.AddOrUpdate(path, 1, (_, count) => count + 1);
                if (path == "/redirect") { await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: /feed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _lifetime.Token); return; }
                if (path == "/slow.wav")
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 104857600\r\nConnection: close\r\n\r\n"), _lifetime.Token);
                    var block = new byte[64 * 1024]; for (int i = 0; i < 1600; i++) { await stream.WriteAsync(block, _lifetime.Token); await Task.Delay(60, _lifetime.Token); } return;
                }
                bool found = Documents.TryGetValue(path, out var body); body ??= Encoding.UTF8.GetBytes("Unavailable");
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: application/octet-stream\r\nContent-Length: {body.Length}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n\r\n"), _lifetime.Token); await stream.WriteAsync(body, _lifetime.Token);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync() { _lifetime.Cancel(); _listener.Stop(); await _accept; await Task.WhenAll(_clients.ToArray()); _lifetime.Dispose(); }
    }
    public static async Task Podcasts(string root, string wav)
    {
        string folder = Path.Combine(root, "podcasts-" + Guid.NewGuid().ToString("N"));
        var service = new SpokenMediaService(folder); await using var server = new HttpFixture(wav);
        Check((await service.Snapshot()).Feeds.Length == 0 && !Directory.Exists(folder) && server.Requests.IsEmpty, "Lazy startup has no profile writes or network work");
        await service.AddFeeds(new[] { server.Address + "/redirect" }); await service.AddFeeds(new[] { server.Address + "/redirect" });
        Check(server.Requests.IsEmpty && (await service.Snapshot()).Feeds.Length == 1, "Subscribing deduplicates without unsolicited retrieval");
        var feed = (await service.Snapshot()).Feeds.Single(); await service.RefreshFeed(feed.Id); var data = await service.Snapshot(); var episode = data.Episodes.Single();
        Check(data.Feeds.Single().Title == "Podcast ñ" && episode.Title == "Episode ñ" && episode.Url == server.Address + "/episode.wav" && episode.DurationMs == 8000 && episode.PublishedUtc?.Hour == 12, "Redirects, RSS metadata, relative enclosures and duration parsed");
        await service.UpdateEpisode(episode.Id, 2300, 8000, false);
        string download = await service.Download(episode.Id); Check(File.ReadAllBytes(download).SequenceEqual(File.ReadAllBytes(wav)) && Path.GetFullPath(download).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar), "Offline copy publishes exact completed bytes inside the profile");
        int requests = server.Requests["/episode.wav"]; Check(await service.Download(episode.Id) == download && server.Requests["/episode.wav"] == requests, "Completed copy is reused without another HTTP request");
        await service.RefreshFeed(feed.Id); var retained = (await service.Snapshot()).Episodes.Single(); Check(retained.PositionMs == 2300 && retained.LocalPath == download, "Refresh preserves progress and offline copy");
        await Refused(() => service.RemoveFeed(feed.Id)); Check(File.Exists(download), "Unsubscribe cannot orphan downloaded copies");
        await service.AddFeeds(new[] { server.Address + "/slow-feed" }); var slowFeed = (await service.Snapshot()).Feeds.Single(item => item.Url.EndsWith("slow-feed"));
        server.Documents["/slow-feed"] = Encoding.UTF8.GetBytes("<feed xmlns='http://www.w3.org/2005/Atom'><title>Atom</title><entry><title>Slow</title><updated>2026-10-23T12:00:00Z</updated><link rel='enclosure' href='/slow.wav' type='audio/wav'/></entry></feed>");
        await service.RefreshFeed(slowFeed.Id); var slow = (await service.Snapshot()).Episodes.Single(item => item.FeedId == slowFeed.Id);
        using (var cancel = new CancellationTokenSource()) { await Refused(() => service.Download(slow.Id, new InlineProgress(_ => cancel.Cancel()), cancel.Token)); }
        Check((await service.Snapshot()).Episodes.Single(item => item.Id == slow.Id).LocalPath == null && !Directory.EnumerateFiles(folder, "*.partial", SearchOption.AllDirectories).Any(), "Canceled streaming download clears temporary data and leaves no offline pointer");
        byte[] before = File.ReadAllBytes(Path.Combine(folder, "spoken-media.json")); server.Documents["/feed"] = Encoding.UTF8.GetBytes("not XML"); await Refused(() => service.RefreshFeed(feed.Id)); Check(File.ReadAllBytes(Path.Combine(folder, "spoken-media.json")).SequenceEqual(before), "Malformed refresh preserves committed state");
        await service.RemoveDownload(episode.Id); Check(!File.Exists(download) && (await service.Snapshot()).Episodes.Single(item => item.Id == episode.Id).LocalPath == null, "Only the owned episode copy is removed"); await service.RemoveFeed(feed.Id);
        Check((await new SpokenMediaService(folder).Snapshot()).Feeds.Length == 1 && File.Exists(wav), "Subscription removal survives restart and original media remains");
        foreach (string address in new[] { "file:///C:/x", "https://user:pass@example.test/feed", "javascript:alert(1)", "" }) await Refused(() => service.AddFeeds(new[] { address }));
        string path = Path.Combine(folder, "spoken-media.json"); var snapshot = await service.Snapshot(); byte[] bytes = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) await Refused(() => service.AddFeeds(new[] { server.Address + "/new" }));
        Check(ReferenceEquals(snapshot, await service.Snapshot()) && File.ReadAllBytes(path).SequenceEqual(bytes), "Failed disk publication cannot mutate the cached document");
    }
    public static async Task OpmlAndBooks(string root, string wav)
    {
        string folder = Path.Combine(root, "spoken-documents-" + Guid.NewGuid().ToString("N")); var service = new SpokenMediaService(folder);
        byte[] opml = Encoding.UTF8.GetBytes("<opml version='2.0'><body><outline text='Folder'><outline text='Feed ñ' xmlUrl='https://example.test/feed'/><outline xmlUrl='https://example.test/feed'/><outline xmlUrl='https://example.test/other'/></outline></body></opml>");
        await service.AddFeeds(PodcastDocuments.ReadOpml(opml)); Check((await service.Snapshot()).Feeds.Length == 2, "Nested OPML flattens subscriptions and deduplicates URLs without fetching");
        string output = Path.Combine(folder, "subscriptions.opml"); var feeds = (await service.Snapshot()).Feeds; await PodcastDocuments.WriteOpmlCopy(output, feeds); Check(PodcastDocuments.ReadOpml(File.ReadAllBytes(output)).Length == 2, "OPML export/import round trip"); byte[] before = File.ReadAllBytes(output); await Refused(() => PodcastDocuments.WriteOpmlCopy(output, feeds)); Check(File.ReadAllBytes(output).SequenceEqual(before), "OPML never overwrites a copy");
        foreach (string invalid in new[] { "<rss><outline xmlUrl='https://example.test/feed'/></rss>", "<!DOCTYPE opml [<!ENTITY x SYSTEM 'file:///C:/secret'>]><opml><body>&x;</body></opml>", "<opml><body><outline xmlUrl='file:///C:/test'/></body></opml>" }) await Refused(() => Task.Run(() => PodcastDocuments.ReadOpml(Encoding.UTF8.GetBytes(invalid))));
        await Refused(() => Task.Run(() => PodcastDocuments.ReadOpml(new byte[PodcastDocuments.MaximumBytes + 1])));
        await service.AddBooks(new[] { wav, wav }); var book = (await service.Snapshot()).Books.Single(); await service.UpdateBook(book.Id, 2600, 8000, 1.5f, false); await service.Bookmark(book.Id, "Chapter ñ", 4500);
        book = (await new SpokenMediaService(folder).Snapshot()).Books.Single(); Check(book.PositionMs == 2600 && book.Rate == 1.5f && book.Bookmarks.Single().Name == "Chapter ñ", "Audiobook progress, individual rate and named bookmarks survive restart");
        await service.UpdateBook(book.Id, 8000, 8000, 1.5f, true); Check((await service.Snapshot()).Books.Single().Completed, "Completion is durable");
        await service.RemoveBookmark(book.Id, 0); Check((await service.Snapshot()).Books.Single().Bookmarks.Length == 0, "Bookmark removal persists"); await service.RemoveBook(book.Id); Check(File.Exists(wav) && (await service.Snapshot()).Books.Length == 0, "Shelf removal retains original audio");
        string corruptFolder = Path.Combine(folder, "corrupt"); Directory.CreateDirectory(corruptFolder); File.WriteAllText(Path.Combine(corruptFolder, "spoken-media.json"), "{invalid"); await Refused(() => new SpokenMediaService(corruptFolder).AddBooks(new[] { wav })); Check(File.ReadAllText(Path.Combine(corruptFolder, "spoken-media.json")) == "{invalid", "Malformed originals are preserved");
    }
    public static async Task Ratings(string root, string wav)
    {
        string folder = Path.Combine(root, "ratings-" + Guid.NewGuid().ToString("N")), second = Path.Combine(root, "rating-other-" + Guid.NewGuid().ToString("N") + ".wav"); File.Copy(wav, second);
        await using var library = new LibraryService(folder); foreach (string source in new[] { wav, second }) await library.UpdateMetadataAsync(source, new("Track", "Artist", "Album", "Genre", "", 8000, true, false, "")); Check(await library.SetRatings(new[] { wav, second }, 4) == 2, "Batch ratings set"); Check(await library.SetRatings(new[] { wav }, 4) == 0, "Unchanged ratings avoid rewriting");
        var rows = await library.SnapshotAsync(); Check(rows.Count(LibraryQuery.Compile("rating:>=4")) == 2 && rows.Count(LibraryQuery.Compile("-rating:5")) == 2, "Ratings participate in numeric smart queries");
        using (var locked = new FileStream(Path.Combine(folder, "library.json"), FileMode.Open, FileAccess.Read, FileShare.Read)) await Refused(() => library.SetRatings(new[] { wav, second }, 2));
        Check((await library.SnapshotAsync()).All(item => item.Rating == 4), "Failed batch publication rolls back every rating");
        string backup = Path.Combine(folder, "backup.json"); await library.ExportBackupAsync(backup);
        await using (var imported = new LibraryService(Path.Combine(folder, "imported"))) { await imported.ImportBackupAsync(backup); Check((await imported.SnapshotAsync()).All(item => item.Rating == 4), "Personal ratings survive library backup/import"); }
        await Refused(() => library.SetRatings(new[] { wav }, 6)); using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); await Refused(() => library.SetRatings(new[] { wav }, 1, cancel.Token)); }
        await using var reopened = new LibraryService(folder); Check((await reopened.SnapshotAsync()).All(item => item.Rating == 4), "Ratings survive profile restart");
        await library.SetRatings(new[] { wav }, 0); Check((await library.SnapshotAsync()).Single(item => item.Path == wav).Rating == 0 && File.Exists(wav), "Clearing a personal rating leaves media intact");
    }
    public static async Task Statistics(string root)
    {
        var date = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        PlaybackHistoryEntry Entry(string title, string artist, long played, string state, int day = 0, string kind = "Audio") => new(Guid.NewGuid().ToString("N"), "https://example.test/" + title, title, artist, kind, date.AddDays(day), date.AddDays(day).AddMinutes(1), 8000, 4000, played, state);
        var history = new[] { Entry("Track", "Artist ñ", 2000, "Paused"), Entry("Track", "ARTIST Ñ", 3000, "Completed"), Entry("Other", "", 1000, "Stopped", -9), Entry("Video", "Artist ñ", 9000, "Completed", kind: "Video") };
        var artists = ListeningStatistics.Calculate(history, "Artists"); Check(artists.Length == 2 && artists[0] is { Sessions: 2, PlayedMs: 5000, Completed: 1 }, "Artist totals merge casing, sum listening time, count completions and exclude video");
        Check(ListeningStatistics.Calculate(history, "Tracks").First().Sessions == 2 && ListeningStatistics.Calculate(history, "Days").Length == 2, "Track and UTC-day grouping");
        Check(ListeningStatistics.Calculate(history, "Artists", date.AddDays(-7), date.AddDays(1)).Sum(item => item.PlayedMs) == 5000, "Explicit date window uses session start");
        string path = Path.Combine(root, "statistics-" + Guid.NewGuid().ToString("N") + ".csv"); await ListeningStatistics.ExportCopy(path, new[] { new ListeningSummary("=formula,\"ñ\"", 2, 1, 5000) }); string text = File.ReadAllText(path); Check(text.Contains("\"'=formula,\"\"ñ\"\"\"") && text.Contains(",2,1,5000"), "CSV quotes Unicode, commas and quotes and prevents spreadsheet formula evaluation"); await Refused(() => ListeningStatistics.ExportCopy(path, artists));
    }
    public static async Task Sessions(string root, string wav)
    {
        string folder = Path.Combine(root, "sessions-" + Guid.NewGuid().ToString("N")); var store = new PlaybackSessions(folder);
        Check((await store.Snapshot()).Sessions.Length == 0 && !Directory.Exists(folder), "Empty session reads are lazy");
        var session = new PlaybackSession(Guid.NewGuid().ToString("N"), "Evening ñ", new[] { new SavedMediaTrack(wav, "First"), new SavedMediaTrack(wav, "Repeated"), new SavedMediaTrack(Path.Combine(root, "missing.wav"), "Missing") }.ToImmutableArray(), 1, 3200, 1.25f, RepeatMode.All, true);
        await store.Save(session); Check(JsonSerializer.Serialize((await new PlaybackSessions(folder).Snapshot()).Sessions.Single()) == JsonSerializer.Serialize(session), "Queue order, duplicate labels and transport state persist");
        await Refused(() => store.Save(session with { Id = Guid.NewGuid().ToString("N"), Name = "EVENING Ñ" })); await Refused(() => store.Save(session with { Index = 100 })); await Refused(() => store.Save(session with { Rate = float.NaN }));
        var snapshot = await store.Snapshot(); using (var locked = new FileStream(Path.Combine(folder, "playback-sessions.json"), FileMode.Open, FileAccess.Read, FileShare.Read)) await Refused(() => store.Save(session with { Name = "Blocked" })); Check(ReferenceEquals(snapshot, await store.Snapshot()), "Failed session replacement preserves memory");
        await store.Remove(session.Id); Check((await store.Snapshot()).Sessions.Length == 0 && File.Exists(wav), "Session removal only changes the session document");
    }
    public static async Task NativeSpoken(string root, string wav)
    {
        string folder = Path.Combine(root, "native-spoken-" + Guid.NewGuid().ToString("N"));
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(Path.Combine(folder, "library")); var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false, DefaultPlaybackOnEnd = "Stop" }, library);
        try
        {
            await vm.SpokenMedia.AddBooks(new[] { wav }); var book = (await vm.SpokenMedia.Snapshot()).Books.Single(); await vm.SpokenMedia.UpdateBook(book.Id, 2200, 8000, 1.25f, false); book = (await vm.SpokenMedia.Snapshot()).Books.Single(); vm.PlayBook(book);
            await Wait(() => vm.IsPlaying && vm.IsAudioMode && engine.GetTime() >= 2100 && Math.Abs(engine.MediaPlayer.Rate - 1.25f) < .02, "Book must resume at individual position and speed despite autoplay off");
            vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying && engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused); await vm.SaveSpokenProgress(); book = (await vm.SpokenMedia.Snapshot()).Books.Single(); Check(book.PositionMs >= 2100 && book.Rate == 1.25f, "Pause checkpoints real playback");
            vm.StopCommand.Execute(null); await vm.SaveSpokenProgress(); vm.PlayBook(book, 6500); await Wait(() => vm.IsPlaying && engine.GetTime() >= 6400); await Wait(() => !vm.IsPlaying, "End of audiobook must stop"); await vm.SaveSpokenProgress(); Check((await vm.SpokenMedia.Snapshot()).Books.Single().Completed, "Native end marks audiobook complete");
            string offline;
            await using (var server = new HttpFixture(wav)) { await vm.SpokenMedia.AddFeeds(new[] { server.Address + "/feed" }); var feed = (await vm.SpokenMedia.Snapshot()).Feeds.Single(); await vm.SpokenMedia.RefreshFeed(feed.Id); var episode = (await vm.SpokenMedia.Snapshot()).Episodes.Single(); offline = await vm.SpokenMedia.Download(episode.Id); await vm.SpokenMedia.UpdateEpisode(episode.Id, 2500, 8000, false); }
            var cached = (await vm.SpokenMedia.Snapshot()).Episodes.Single(); vm.PlayEpisode(cached); await Wait(() => vm.IsPlaying && vm.CurrentItem?.FilePath == offline && engine.GetTime() >= 2400, "Episode must decode offline with server disconnected and resume");
            vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying); await vm.SaveSpokenProgress(); Check((await vm.SpokenMedia.Snapshot()).Episodes.Single().PositionMs >= 2400, "Episode checkpoints real native position");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static async Task NativeSessions(string root, string wav)
    {
        string folder = Path.Combine(root, "native-session-" + Guid.NewGuid().ToString("N")); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(Path.Combine(folder, "library")); var vm = new MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        try
        {
            var session = new PlaybackSession(Guid.NewGuid().ToString("N"), "Queue", new[] { new SavedMediaTrack(Path.Combine(root, "missing.wav"), "Missing"), new SavedMediaTrack(wav, "First label"), new SavedMediaTrack(wav, "Second label") }.ToImmutableArray(), 2, 3100, 1.5f, RepeatMode.One, true);
            Check(vm.RestoreSession(session, false) == 2 && !vm.HasMedia && !engine.IsPlaying && vm.CurrentIndex == 1 && vm.Playlist[0].Title == "First label" && vm.Playlist[1].Title == "Second label", "Restore waits for explicit play, skips missing source and preserves duplicate labels/selection");
            var captured = vm.CaptureSession("Queue-only"); Check(captured.PositionMs == 3100 && captured.Rate == 1.5f && captured.Repeat == RepeatMode.One && captured.Shuffle, "Saving a restored queue retains staged position and transport options");
            vm.PlayPauseCommand.Execute(null); await Wait(() => vm.IsPlaying && engine.GetTime() >= 3000 && Math.Abs(engine.MediaPlayer.Rate - 1.5f) < .02, "Explicit play must restore saved position and speed");
            vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying); await vm.SavedSessions.Save(vm.CaptureSession("Captured")); Check((await vm.SavedSessions.Snapshot()).Sessions.Single().PositionMs >= 3000, "Saving captures real native position");
            vm.RestoreSession(session, true); await Wait(() => vm.IsPlaying && engine.GetTime() >= 3000, "Restore and play must be explicit");
            vm.RestoreSession(session, false); vm.ClearPlaylistCommand.Execute(null); vm.AddFilesToPlaylist(new[] { wav }, false); Check(vm.CaptureSession("Cleared").PositionMs == 0, "Clearing the queue discards the staged session position");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static async Task NativeVideo(string root, string wav)
    {
        string folder = Path.Combine(root, "native-picture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string path = Path.Combine(folder, "pattern.mkv");
        var start = new ProcessStartInfo(ConversionService.FindExecutable()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-f", "lavfi", "-i", "color=gray:size=320x180:rate=10,drawbox=x=0:y=0:w=80:h=180:color=red:t=fill,drawbox=x=240:y=0:w=80:h=180:color=blue:t=fill", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "30", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", path }) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!) { var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Check(process.ExitCode == 0, await error); }
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); using var renderer = new VlcWpfRenderer(engine, new Image()); await using var library = new LibraryService(Path.Combine(folder, "library")); var store = new SettingsService(Path.Combine(folder, "settings")); Check(store.Save(new AppSettings { Volume = 0 }), "Initial preset profile"); var vm = new MainViewModel(engine, store, library: library);
        try
        {
            vm.AddFilesToPlaylist(new[] { path }, true); await Wait(() => vm.IsPlaying && engine.GetTime() > 300 && renderer.CurrentBitmap != null); Check(engine.VideoSize() == (320u, 180u), "Native source dimensions");
            var pixels = new byte[32 * 18 * 3]; double Mean() { Check(renderer.TrySampleFrame(pixels, 32, 18), "Read displayed pixels"); return pixels.Average(item => (double)item); }
            double neutral = Mean(); vm.SavePicturePreset("Dim ñ", new VideoPicture(Brightness: .4f));
            await Wait(() => Mean() < neutral - 20, "Picture preset must change real decoded/displayed pixels"); Check(store.Load().ActivePicturePreset == "Dim ñ" && store.Load().PicturePresets["Dim ñ"].Brightness == .4f, "Picture preset survives restart");
            vm.ResetPicture(); await Wait(() => Math.Abs(Mean() - neutral) < 6, "Neutral reset must restore actual picture");
            var combined = new VideoPicture(1, .8f, .6f, 120, 1.4f); vm.SavePicturePreset("Combined", combined);
            await Wait(() => Math.Abs(engine.MediaPlayer.AdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Gamma) - 1.4f) < .01 && Math.Abs(engine.MediaPlayer.AdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Hue) - 120) < .01);
            Check(Math.Abs(engine.MediaPlayer.AdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Contrast) - .8f) < .01 && Math.Abs(engine.MediaPlayer.AdjustFloat(LibVLCSharp.Shared.VideoAdjustOption.Saturation) - .6f) < .01, "All picture preset controls use the native float adjustment API");
            vm.ResetPicture(); await Wait(() => Math.Abs(Mean() - neutral) < 6);
            using (var locked = new FileStream(Path.Combine(store.DataDirectory, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read)) await Refused(() => Task.Run(() => vm.SavePicturePreset("Blocked", new VideoPicture(Brightness: .3f))));
            Check(!store.Load().PicturePresets.ContainsKey("Blocked"), "A failed settings publication cannot activate or retain a new preset");
            vm.UsePicturePreset("Dim ñ"); await Wait(() => Mean() < neutral - 20); vm.ResetPicture(); await Wait(() => Math.Abs(Mean() - neutral) < 6);
            await Refused(() => Task.Run(() => engine.SetCrop(new VideoCrop(300, 0, 80, 180)))); engine.SetCrop(new VideoCrop(240, 0, 80, 180));
            await Task.Delay(600); Mean(); double red = pixels.Where((_, index) => index % 3 == 2).Average(item => (double)item), blue = pixels.Where((_, index) => index % 3 == 0).Average(item => (double)item);
            string cropSnapshot = Path.Combine(folder, "cropped-native.png"); await engine.CaptureSnapshotAsync(cropSnapshot);
            using (var input = File.OpenRead(cropSnapshot)) { var decoded = System.Windows.Media.Imaging.BitmapDecoder.Create(input, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad); Check(decoded.Frames[0].PixelWidth == 80 && decoded.Frames[0].PixelHeight == 180, "Exported snapshots use the same crop rectangle"); }
            var frame = System.Windows.Media.Imaging.BitmapSource.Create(32, 18, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, pixels, 32 * 3);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame)); using (var output = File.Create(Path.Combine(folder, "cropped-display.png"))) encoder.Save(output);
            Console.WriteLine($"Picture probe: neutral mean={neutral:0.0}; crop={engine.MediaPlayer.CropGeometry}; artifacts={folder}");
            Check(blue > 180 && red < 35, $"Crop must display the blue source rectangle (mean R={red:0.0}, B={blue:0.0}, bitmap={renderer.CurrentBitmap?.PixelWidth}x{renderer.CurrentBitmap?.PixelHeight})"); engine.SetCrop(null); await Wait(() => Math.Abs(Mean() - neutral) < 6);
            int before = engine.GetAudioTracks().Count(item => item.Id >= 0); vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying && engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused); long position = engine.GetTime(); Check(vm.LoadExternalAudio(wav), "Native external audio accepted");
            await Wait(() => vm.AudioTracks.Count(item => item.Id >= 0) > before && vm.SelectedAudioTrack?.Id == engine.GetAudioTrack(), "Attached audio track is enumerated and selected"); Check(engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused && Math.Abs(engine.GetTime() - position) < 1000, "Attaching audio preserves pause and position");
            Check(vm.LoadExternalAudio(wav), "Already attached track can be selected without another slave"); await Task.Delay(200); Check(engine.GetAudioTracks().Count(item => item.Id >= 0) == before + 1, "Repeated attachments do not accumulate duplicate native tracks");
            vm.PlayPauseCommand.Execute(null); await Wait(() => vm.IsPlaying && engine.GetTime() > position); vm.PlayAt(0); await Wait(() => vm.IsPlaying && engine.GetTime() > 100 && engine.GetAudioTracks().Count(item => item.Id >= 0) == before); Check(engine.CurrentCrop == null && string.IsNullOrEmpty(engine.MediaPlayer.CropGeometry), "Opening another source resets crop and external tracks");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
}
