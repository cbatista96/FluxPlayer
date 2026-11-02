using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class MediaCatalogChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Refused(Func<Task> action)
    {
        bool refused = false;
        try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Xml.XmlException or OperationCanceledException) { refused = true; }
        Check(refused, "Expected invalid, protected, stale, canceled or conflicting operation to be rejected.");
    }
    private static async Task Wait(Func<bool> condition)
    { var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > 7000) throw new TimeoutException("Catalog native playback timed out."); await Task.Delay(50); } }
    public static async Task Catalog(string root, string wav)
    {
        string directory = Path.Combine(root, "catalog-" + Guid.NewGuid().ToString("N")), path = Path.Combine(directory, "media-catalog.json");
        var service = new MediaCatalogService(directory);
        Check(!Directory.Exists(directory) && (await service.SnapshotAsync()).Playlists.Length == 0 && !Directory.Exists(directory), "Construction and empty reads create no files");
        string id = Guid.NewGuid().ToString("N"); var tracks = new[] { new SavedMediaTrack(wav, "First ñ"), new SavedMediaTrack(wav, "Repeated"), new SavedMediaTrack(Path.Combine(root, "missing.wav"), "Unavailable") };
        await service.SavePlaylistAsync(id, "List ñ", tracks); var first = (await service.SnapshotAsync()).Playlists.Single();
        Check(first.Revision == 1 && first.Tracks.Length == 3 && first.Tracks.Select(item => item.Title).SequenceEqual(tracks.Select(item => item.Title)), "Order, duplicates, missing paths and Unicode persist");
        await service.SetProtectionAsync(id, 1, true);
        await Refused(() => service.SavePlaylistAsync(id, "Changed", tracks, 2)); await Refused(() => service.RemovePlaylistAsync(id, 2));
        await service.SetProtectionAsync(id, 2, false); await service.SavePlaylistAsync(id, "List renamed", tracks.Reverse(), 3);
        await Refused(() => service.SavePlaylistAsync(id, "Stale", tracks, 3));
        string second = Guid.NewGuid().ToString("N"); await service.SavePlaylistAsync(second, "Other", new[] { new SavedMediaTrack(wav, "First ñ") });
        Check(MediaCatalogService.FindTracks(await service.SnapshotAsync(), "FIRST Ñ").Length == 2, "Search spans every saved playlist and ignores case");
        await Refused(() => service.SavePlaylistAsync(Guid.NewGuid().ToString("N"), "LIST RENAMED", tracks));
        await Refused(() => service.SavePlaylistAsync(Guid.NewGuid().ToString("N"), "Excess", Enumerable.Repeat(tracks[0], 10_001)));
        var before = await service.SnapshotAsync(); byte[] bytes = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Refused(() => service.SavePlaylistAsync(id, "Blocked", tracks, 4));
        Check(File.ReadAllBytes(path).SequenceEqual(bytes) && ReferenceEquals(before, await service.SnapshotAsync()) && !Directory.EnumerateFiles(directory, "*.tmp").Any(), "Failed publication keeps memory and disk intact");
        using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); await Refused(() => service.RemovePlaylistAsync(id, 4, canceled.Token)); }
        Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Cancellation preserves catalog bytes");
        var radio = new RadioStation(Guid.NewGuid().ToString("N"), "Radio ñ", "https://example.test/live", "Jazz", "Test", true);
        await service.SaveStationAsync(radio);
        await service.ImportStationsAsync(new[] { new SavedMediaTrack(radio.Url, "Duplicate") });
        Check((await service.SnapshotAsync()).Stations.Single() == radio, "Station import deduplicates without erasing favorite or metadata");
        var caseSensitive = radio with { Id = Guid.NewGuid().ToString("N"), Url = "https://example.test/LIVE" };
        await service.SaveStationAsync(caseSensitive); Check((await service.SnapshotAsync()).Stations.Length == 2, "URL path case can identify different streams");
        await service.RemoveStationAsync(caseSensitive.Id);
        foreach (string invalid in new[] { "file:///C:/test.wav", "javascript:alert(1)", "https://user:password@example.test/live", "not a URL" })
            await Refused(() => service.SaveStationAsync(radio with { Url = invalid }));
        var reopened = await new MediaCatalogService(directory).SnapshotAsync();
        Check(reopened.Playlists.Length == 2 && reopened.Stations.Single() == radio && reopened.Playlists.Single(list => list.Id == id).Tracks.Select(item => item.Title).SequenceEqual(tracks.Reverse().Select(item => item.Title)), "Independent lists and stations survive restart");
        var current = reopened.Playlists.Single(list => list.Id == id);
        await service.RemovePlaylistAsync(id, current.Revision); Check((await service.SnapshotAsync()).Playlists.Length == 1 && File.Exists(wav), "Removing a list leaves media and other lists intact");
        string corruptDirectory = Path.Combine(directory, "corrupt"); Directory.CreateDirectory(corruptDirectory); string corrupt = Path.Combine(corruptDirectory, "media-catalog.json"); File.WriteAllText(corrupt, "{invalid");
        await Refused(() => new MediaCatalogService(corruptDirectory).SaveStationAsync(radio)); Check(File.ReadAllText(corrupt) == "{invalid", "Corrupt originals are preserved");
        string limits = Path.Combine(directory, "limits"); Directory.CreateDirectory(limits);
        var sample = first with { Tracks = ImmutableArray<SavedMediaTrack>.Empty };
        var excessive = Enumerable.Range(0, 201).Select(index => sample with { Id = Guid.NewGuid().ToString("N"), Name = "List " + index }).ToImmutableArray();
        File.WriteAllText(Path.Combine(limits, "media-catalog.json"), JsonSerializer.Serialize(new { Version = 1, Data = new MediaCatalogSnapshot(excessive, ImmutableArray<RadioStation>.Empty) }));
        await Refused(async () => { await new MediaCatalogService(limits).SnapshotAsync(); });
    }
    public static async Task Playlists(string root, string wav)
    {
        string directory = Path.Combine(root, "named-playlists-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var tracks = new[] { new SavedMediaTrack(wav, "Local ñ & title"), new SavedMediaTrack("https://example.test/live?x=1&y=2", "Station ñ, special"), new SavedMediaTrack(Path.Combine(root, "Missing ñ.flac"), "Missing") };
        foreach (string extension in new[] { ".m3u8", ".pls", ".xspf" })
        {
            string path = Path.Combine(directory, "List" + extension); await PlaylistService.WriteCopyAsync(path, tracks);
            Check(PlaylistService.ReadEntries(path).SequenceEqual(tracks), "Names, order, URLs, missing local paths and Unicode round trip: " + extension);
            Check(PlaylistService.Read(path).Length == 2, "Legacy opening still skips missing local sources");
            byte[] original = File.ReadAllBytes(path); await Refused(() => PlaylistService.WriteCopyAsync(path, tracks)); Check(File.ReadAllBytes(path).SequenceEqual(original), "No-overwrite export preserves existing file");
        }
        string relative = Path.Combine(directory, "relative.m3u8"); File.WriteAllText(relative, "#EXTM3U\n#EXTINF:-1,Future ñ\nfuture.flac\n");
        Check(PlaylistService.ReadEntries(relative).Single() == new SavedMediaTrack(Path.Combine(directory, "future.flac"), "Future ñ"), "Missing relative paths remain editable rather than disappearing from saved lists");
        string numeric = Path.Combine(directory, "order.pls"); File.WriteAllText(numeric, "Title10=Tenth\nFile10=https://example.test/10\nFile2=https://example.test/2\nTitle2=Second");
        Check(PlaylistService.ReadEntries(numeric).Select(item => item.Title).SequenceEqual(new[] { "Second", "Tenth" }), "PLS titles match numeric indexes regardless of field order");
        string xspf = Path.Combine(directory, "entity.xspf"); File.WriteAllText(xspf, "<!DOCTYPE playlist [<!ENTITY x SYSTEM 'file:///C:/private'>]><playlist><track><location>&x;</location></track></playlist>");
        await Refused(() => Task.Run(() => PlaylistService.ReadEntries(xspf)));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); string output = Path.Combine(directory, "canceled.m3u8");
        await Refused(() => PlaylistService.WriteCopyAsync(output, tracks, canceled.Token)); Check(!File.Exists(output), "Canceled export creates no output");
    }
    public static async Task Facets(string root)
    {
        var entries = new[] {
            new LibraryEntry { Path = Path.Combine(root,"1991.flac"), Title="One", Genre=" Jazz ", Year=1991, Artist="A" },
            new LibraryEntry { Path = Path.Combine(root,"1999.flac"), Title="Two", Genre="jazz", Year=1999, Artist="B" },
            new LibraryEntry { Path = Path.Combine(root,"2001.flac"), Title="Three", Genre="Rock", Year=2001, Artist="C" },
            new LibraryEntry { Path = Path.Combine(root,"unknown.flac"), Title="Unknown", Genre="", Year=0 } };
        var all = LibraryBrowser.Browse(entries, "", 0, 0);
        Check(all.Genres.Single(item => item.Value?.Equals("Jazz", StringComparison.OrdinalIgnoreCase) == true).Count == 2 && all.Decades.Single(item => item.Value == "1990").Count == 2, "Exact genre and decade facets carry correct counts");
        var jazz = LibraryBrowser.Browse(entries, "", 1, 0, genre: "jazz", decade: 1990);
        Check(jazz.Entries.Length == 2 && jazz.Artists.Length == 3, "Genre/decade scope artist and album browsing");
        Check(LibraryBrowser.Browse(entries, "", 0, 0, genre: "", decade: -1).Entries.Single().Title == "Unknown", "Missing genres and unknown year remain distinguishable");
        Check(LibraryBrowser.Browse(entries, "title:One", 0, 0, genre: "Rock", decade: 2010).Decades.Any(item => item.Value == "2010" && item.Count == 0), "Absent selected decade remains selectable with zero results");
        Check(entries.Where(LibraryQuery.Compile("year:>=1990 -decade:2000s")).Count() == 2, "Year/decade query rules compose and exclude");
        using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); await Refused(() => Task.Run(() => LibraryBrowser.Browse(entries, "", 0, 0, token: canceled.Token))); }
        string directory = Path.Combine(root, "genre-years-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(directory, "backup.json");
        await using (var library = new LibraryService(directory))
        {
            foreach (var entry in entries) await library.UpdateMetadataAsync(entry.Path, new(entry.Title, entry.Artist, "Album", entry.Genre, "", 8000, true, false, "", entry.Year));
            await library.SaveSmartListAsync(new("90s jazz", "", 1, 0, Genre: "jazz", Decade: 1990));
            await library.ExportBackupAsync(backup);
        }
        await using var restored = new LibraryService(Path.Combine(directory, "restored")); await restored.ImportBackupAsync(backup);
        var snapshot = await restored.SnapshotAsync(); var list = (await restored.SmartListsAsync()).Single();
        Check(snapshot.Single(item => item.Title == "One").Year == 1991 && list is { Genre: "jazz", Decade: 1990 }, "Years and saved browsing criteria survive backup merging");
        Check(!new SmartList("Bad", "", 0, 0, Decade: 1991).IsValid && new SmartList("Legacy", "", 0, 0).IsValid, "Malformed decades rejected; old lists stay compatible");
    }
    public static async Task Native(string root, string wav)
    {
        string directory = Path.Combine(root, "native-catalog-" + Guid.NewGuid().ToString("N")); var store = new SettingsService(directory); store.Save(new AppSettings { Volume = 0, AutoPlay = false });
        await using var library = new LibraryService(Path.Combine(directory, "library")); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); var vm = new MainViewModel(engine, store, library: library);
        try
        {
            var tracks = new[] { new SavedMediaTrack(wav, "Queue label ñ"), new SavedMediaTrack(Path.Combine(root, "Missing.wav"), "Missing") };
            string id = Guid.NewGuid().ToString("N"); await vm.MediaCatalog.SavePlaylistAsync(id, "List", tracks); await vm.MediaCatalog.SetProtectionAsync(id, 1, true);
            Check(vm.Playlist.Count == 0 && !engine.IsPlaying, "Catalog edits do not alter the queue or start playback");
            Check(vm.AddCatalogTracks(tracks, false) == 1 && !engine.IsPlaying && vm.Playlist.Single().Title == "Queue label ñ", "Enqueue skips missing files and preserves imported label without autoplay");
            int first = vm.Playlist.Count; Check(vm.AddCatalogTracks(tracks, true) == 1, "Explicit catalog play adds an available source");
            await Wait(() => vm.IsPlaying && engine.GetDuration() == 8000); Check(vm.CurrentIndex == first && engine.IsPlaying, "Explicit play works with autoplay disabled");
            Check((await vm.MediaCatalog.SnapshotAsync()).Playlists.Single().Protected, "Playing a protected list never edits its contents or protection");
            if (ConversionService.FindExecutable() != null)
            {
                string flac = Path.Combine(directory, "Dated jazz ñ.flac");
                await ConversionService.ConvertAsync(wav, flac, ConversionService.Profiles.First(profile => profile.Encoder == "flac"), 8000, false, null, default);
                using (var file = TagLib.File.Create(flac)) { file.Tag.Year = 1997; file.Tag.Genres = new[] { "Jazz ñ" }; file.Save(); }
                var metadata = await engine.ReadMetadataAsync(flac); Check(metadata is { Year: 1997, Genre: "Jazz ñ" }, "Year and genre are read from an actual tagged FLAC container");
                await library.UpdateMetadataAsync(flac, metadata!);
                Check(LibraryBrowser.Browse(await library.SnapshotAsync(), "", 1, 0, genre: "Jazz ñ", decade: 1990).Entries.Single().Path == flac, "Decoded container year reaches actual library decade navigation");
            }
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static async Task Radio(string root, string wav)
    {
        await using var server = new WaveServer(await File.ReadAllBytesAsync(wav));
        string directory = Path.Combine(root, "native-radio-" + Guid.NewGuid().ToString("N")); var settings = new SettingsService(directory); settings.Save(new AppSettings { Volume = 0, AutoPlay = false });
        await using var library = new LibraryService(Path.Combine(directory, "library")); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); var vm = new MainViewModel(engine, settings, library: library);
        try
        {
            var station = new RadioStation(Guid.NewGuid().ToString("N"), "Loopback radio ñ", server.Url, "Test", "Local fixture", true);
            await vm.MediaCatalog.SaveStationAsync(station); await vm.MediaCatalog.SnapshotAsync();
            var tracks = new[] { new SavedMediaTrack(station.Url, station.Name) }; vm.AddCatalogTracks(tracks, false); await Task.Delay(120);
            Check(server.Requests == 0 && !engine.IsPlaying, "Saving/browsing/enqueueing a station performs no network request");
            vm.AddCatalogTracks(tracks, true); await Wait(() => vm.IsPlaying && engine.GetTime() > 0 && engine.ReadCurrentMetadata()?.HasAudio == true);
            Check(server.Requests > 0 && vm.IsAudioMode && vm.CurrentItem?.FilePath == station.Url, "Actual HTTP audio stream decodes and selects the audio workspace");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    private sealed class WaveServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly byte[] _wave;
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public string Url { get; }
        public WaveServer(byte[] wave)
        {
            _wave = wave; _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/radio"; _loop = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token); using var stream = client.GetStream();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(3000);
                    try
                    {
                        var request = new List<byte>(); var b = new byte[1];
                        while (request.Count < 32768 && await stream.ReadAsync(b, timeout.Token) > 0)
                        { request.Add(b[0]); if (request.Count >= 4 && request.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break; }
                        Interlocked.Increment(ref _requests);
                        string text = Encoding.ASCII.GetString(request.ToArray()); long offset = 0;
                        string? range = text.Split("\r\n").FirstOrDefault(line => line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase));
                        bool partial = range != null && long.TryParse(range[13..].Split('-')[0], out offset); offset = Math.Clamp(offset, 0, _wave.Length - 1);
                        string header = $"HTTP/1.1 {(partial ? "206 Partial Content" : "200 OK")}\r\nContent-Type: audio/wav\r\nContent-Length: {_wave.Length - offset}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n";
                        if (partial) header += $"Content-Range: bytes {offset}-{_wave.Length - 1}/{_wave.Length}\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(header + "\r\n"), timeout.Token); await stream.WriteAsync(_wave.AsMemory((int)offset), timeout.Token);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; _stop.Dispose(); }
    }
}
