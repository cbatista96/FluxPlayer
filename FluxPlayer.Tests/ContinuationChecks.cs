using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

internal static class ContinuationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e) when (e is ArgumentException or IOException or OperationCanceledException or JsonException or UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Expected rejection.");
    }
    public static Task Touch()
    {
        var gesture = new TouchGestures();
        gesture.Begin(1, 0, 0, 1000, 500); gesture.Move(1, 500, 0);
        Check(gesture.End(1, 500, 0) is { SeekSeconds: 60, Tap: false }, "Horizontal swipe seeks proportionally.");
        gesture.Begin(1, 100, 200, 1000, 500);
        Check(gesture.End(1, 100, 100) is { VolumeDelta: 20 }, "Upward swipe increases volume.");
        gesture.Begin(1, 10, 10, 1000, 500); Check(gesture.End(1, 20, 10)?.Tap == true, "Small movement is a tap.");
        gesture.Begin(1, 100, 100, 1000, 500); gesture.Begin(2, 200, 100, 1000, 500);
        gesture.Move(1, 50, 100); gesture.Move(2, 250, 100);
        Check(gesture.End(1, 50, 100) == null, "Pinch waits for both fingers.");
        Check(gesture.End(2, 250, 100) is { ZoomFactor: 2, Tap: false, PanX: 0 }, "Pinch changes zoom without a transport tap.");
        gesture.Begin(1, 100, 100, 1000, 500); gesture.Begin(2, 200, 100, 1000, 500);
        gesture.Move(1, 200, 100); gesture.Move(2, 300, 100); gesture.End(1, 200, 100);
        Check(Math.Abs(gesture.End(2, 300, 100)!.PanX + .2) < .00001, "Two fingers pan together.");
        gesture.Begin(1, 10, 10, 1000, 500); gesture.Begin(2, 100, 10, 1000, 500); gesture.Begin(3, 200, 10, 1000, 500);
        gesture.End(1, 10, 10); gesture.End(2, 100, 10); Check(gesture.End(3, 200, 10) == null, "Third finger cancels the sequence.");
        gesture.Begin(1, 0, 0, 1000, 500); gesture.Cancel(); Check(gesture.End(1, 100, 100) == null, "Lost capture cancels.");
        gesture.Begin(1, 0, 0, 1000, 500); gesture.Move(1, double.NaN, 0); Check(gesture.End(1, 100, 100) == null, "Nonfinite input cancels.");
        gesture.Begin(1, 10, 10, 1000, 500); gesture.Move(1, 200, 10); Check(gesture.End(1, 10, 10)?.Tap == false, "A returning swipe cannot become an accidental tap.");
        gesture.Begin(1, 100, 100, 1000, 500); gesture.Begin(2, 200, 100, 1000, 500); gesture.End(1, 100, 100);
        gesture.Begin(3, 300, 100, 1000, 500); gesture.End(2, 200, 100); Check(gesture.End(3, 300, 100) == null, "A replacement third contact cancels the sequence.");
        return Task.CompletedTask;
    }
    public static async Task Duplicates(string root, string wav)
    {
        string folder = Path.Combine(root, "duplicate-merge-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string a = Path.Combine(folder, "a.wav"), b = Path.Combine(folder, "b.wav"), c = Path.Combine(folder, "c.wav");
        File.Copy(wav, a); File.Copy(wav, b); File.Copy(wav, c);
        string profile = Path.Combine(folder, "profile");
        await using (var library = new LibraryService(profile))
        {
            await library.UpdateMetadataAsync(a, new("Keep", "", "Album", "", "", 8000, true, false, ""), true);
            await library.UpdateMetadataAsync(b, new("Other", "Artist", "", "Jazz", "", 8000, true, false, ""), true);
            await library.ToggleFavoriteAsync(b); await library.SetRatings(new[] { b }, 4);
            await library.UpdateLabelsAsync(new[] { a }, new[] { "One" }, LibraryLabelEdit.Add);
            await library.UpdateLabelsAsync(new[] { b }, new[] { "Two" }, LibraryLabelEdit.Add);
            var collection = await library.SaveCollectionAsync("Duplicates"); await library.UpdateCollectionMembersAsync(collection.Id, new[] { a, b }, true);
            var preview = await library.PreviewDuplicateMerge(a, new[] { b }, CancellationToken.None);
            Check(preview.Merged is { Title: "Keep", Artist: "Artist", Album: "Album", Genre: "Jazz", Rating: 4, Favorite: true, PlayCount: 2 } && preview.Merged.Labels.Length == 2, "Preview preserves keeper fields and combines library metadata.");
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Reject(() => library.ApplyDuplicateMerge(preview, cancel.Token));
            Check((await library.SnapshotAsync()).Length == 2, "Canceled merge leaves the index intact.");
            string store = Path.Combine(profile, "library.json"), saved = store + ".saved"; File.Move(store, saved); Directory.CreateDirectory(store);
            await Reject(() => library.ApplyDuplicateMerge(preview, CancellationToken.None));
            Check((await library.SnapshotAsync()).Length == 2 && (await library.CollectionsAsync()).Single().Paths.Length == 2, "Failed write restores entries and memberships.");
            Directory.Delete(store); File.Move(saved, store);
            await library.UpdateLabelsAsync(new[] { b }, new[] { "Changed" }, LibraryLabelEdit.Add);
            await Reject(() => library.ApplyDuplicateMerge(preview, CancellationToken.None));
            preview = await library.PreviewDuplicateMerge(a, new[] { b }, CancellationToken.None);
            Check(await library.ApplyDuplicateMerge(preview, CancellationToken.None) == 1, "Merge commits once.");
            Check((await library.SnapshotAsync()).Single().Artist == "Artist" && (await library.CollectionsAsync()).Single().Paths.SequenceEqual(new[] { a }), "Merged entry and collections use keeper.");
            Check(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)), "Consolidation preserves original files.");
            await Reject(() => library.ApplyDuplicateMerge(preview, CancellationToken.None));
            await library.UpdateMetadataAsync(c, new("Third", "", "", "", "", 8000, true, false, ""));
            var changed = await library.PreviewDuplicateMerge(a, new[] { c }, CancellationToken.None);
            await File.AppendAllTextAsync(c, "changed"); await Reject(() => library.ApplyDuplicateMerge(changed, CancellationToken.None));
        }
        await using var reopened = new LibraryService(profile);
        Check((await reopened.SnapshotAsync()).Single(e => e.Path == a).PlayCount == 2, "Merged statistics survive restart.");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    public static int ExtensionFixture(string mode)
    {
        using var request = JsonDocument.Parse(Console.ReadLine() ?? "{}");
        if (request.RootElement.GetProperty("apiVersion").GetInt32() != 1) return 3;
        if (mode == "fail") { Console.Error.WriteLine("Expected fixture failure."); return 2; }
        if (mode == "wait") { Thread.Sleep(30000); return 0; }
        if (mode == "overflow") { Console.Write(new string('x', 100000)); Console.Out.Flush(); Thread.Sleep(30000); return 0; }
        Console.Write(mode == "invalid" ? "{\"apiVersion\":1,\"message\":\"Invalid\",\"actions\":[{\"command\":\"open-file\"}]}" : "{\"apiVersion\":1,\"message\":\"Fixture\",\"actions\":[{\"command\":\"volume\",\"value\":25},{\"command\":\"mute\",\"value\":1}]}");
        return 0;
    }
    public static async Task Extensions(string root)
    {
        string folder = Path.Combine(root, "extensions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "extension.json"); string executable = Path.Combine(AppContext.BaseDirectory, "FluxPlayer.Tests.exe");
        async Task<LoadedExtension> Load(string mode, int version = 1)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ExtensionManifest(version, "test.extension", "Test extension", executable, new[] { "--extension-fixture=" + mode })));
            return await PlayerExtensions.Load(path, CancellationToken.None);
        }
        var snapshot = new ExtensionSnapshot(1, "Title", false, 0, 0, 50, false, 1, []);
        var extension = await Load("ok");
        var reply = await PlayerExtensions.Run(extension, snapshot, CancellationToken.None);
        Check(reply.Actions.Length == 2 && reply.Actions[0] == new ExtensionAction("volume", 25), "Real extension receives versioned JSON and returns typed proposed commands.");
        await Reject(async () => { await Load("ok", 2); });
        await Reject(async () => PlayerExtensions.ValidateReply(new(1, "", [new("volume", double.NaN)])));
        await Reject(async () => PlayerExtensions.ValidateReply(new(1, "", [new("queue", .5)])));
        await Reject(async () => PlayerExtensions.ValidateReply(new(1, "", [new("delete-file")])));
        foreach (string mode in new[] { "invalid", "fail", "overflow" })
        {
            extension = await Load(mode); var timer = System.Diagnostics.Stopwatch.StartNew();
            await Reject(() => PlayerExtensions.Run(extension, snapshot, CancellationToken.None));
            Check(timer.Elapsed < TimeSpan.FromSeconds(8), "Invalid or excessive output terminates the owned process promptly.");
        }
        extension = await Load("wait"); using var cancel = new CancellationTokenSource(500);
        await Reject(() => PlayerExtensions.Run(extension, snapshot, cancel.Token));
    }
    public static async Task Mobile(string root)
    {
        string folder = Path.Combine(root, "mobile-" + Guid.NewGuid().ToString("N"));
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        await using var library = new LibraryService(folder);
        var vm = new FluxPlayer.ViewModels.MainViewModel(engine, new SettingsService(folder), new AppSettings { AutoPlay = false, Volume = 0 }, library);
        await using var remote = new MobileRemote(vm, System.Windows.Application.Current.Dispatcher);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        try
        {
            remote.Start("127.0.0.1", port); string address = remote.Address, secret = remote.PairingCode;
            using var page = await client.GetAsync(address); string html = await page.Content.ReadAsStringAsync();
            Check(page.IsSuccessStatusCode && html.Contains("<html") && !html.Contains("{{nonce}}") && !html.Contains(secret) && page.Headers.Contains("Content-Security-Policy"), "Packaged page loads with CSP, nonce and no embedded credential.");
            using var denied = await client.GetAsync(address + "state"); Check(denied.StatusCode == HttpStatusCode.Unauthorized, "State requires pairing.");
            client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
            using var state = await client.GetAsync(address + "state"); Check(state.IsSuccessStatusCode, "Paired client reads state.");
            async Task<HttpStatusCode> Post(string body)
            {
                using var response = await client.PostAsync(address + "control", new StringContent(body, System.Text.Encoding.UTF8, "application/json")); return response.StatusCode;
            }
            Check(await Post("{\"command\":\"volume\",\"value\":37}") == HttpStatusCode.OK && vm.Volume == 37, "Remote changes the real VM volume.");
            Check(await Post("{\"command\":\"mute\",\"value\":1}") == HttpStatusCode.OK && vm.IsMuted, "Remote changes mute state.");
            PlayerExtensions.Apply(vm, new(1, "Reviewed", [new("volume", 25), new("mute", 0)]));
            Check(vm.Volume == 25 && !vm.IsMuted, "Reviewed extension actions change real VM state.");
            await Reject(() => { PlayerExtensions.Apply(vm, new(1, "Invalid batch", [new("volume", 50), new("queue", 0)])); return Task.CompletedTask; });
            Check(vm.Volume == 25, "Extension context preflight rejects a bad batch before changing volume.");
            foreach (string invalid in new[] { "{}", "[]", "{\"command\":1}", "{\"command\":\"volume\",\"value\":\"wrong\"}", "{\"command\":\"volume\",\"value\":101}", "{\"command\":\"unknown\"}", "{\"command\":\"queue\",\"value\":0}", "broken" })
                Check(await Post(invalid) == HttpStatusCode.BadRequest, "Malformed or invalid remote input returns 400: " + invalid);
            client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
            using var origin = await client.GetAsync(address + "state"); Check(origin.StatusCode == HttpStatusCode.Forbidden, "Foreign browser origins are rejected."); client.DefaultRequestHeaders.Remove("Origin");
            await remote.DisposeAsync(); Check(remote.Address == "" && remote.PairingCode == "", "Stopping clears session details.");
            remote.Start("127.0.0.1", port); Check(remote.PairingCode != secret, "Restart rotates pairing secret.");
            using var expired = await client.GetAsync(address + "state"); Check(expired.StatusCode == HttpStatusCode.Unauthorized, "Previous pairing does not authorize a new session.");
        }
        finally { await remote.DisposeAsync(); await vm.ShutdownAsync(); }
    }
    public static async Task Lyrics(string root)
    {
        var requests = new List<Uri>(); HttpStatusCode status = HttpStatusCode.OK;
        string body = "[{\"id\":42,\"trackName\":\"Canción\",\"artistName\":\"Artist\",\"albumName\":\"Album\",\"duration\":12,\"instrumental\":false,\"plainLyrics\":\"Hola\",\"syncedLyrics\":\"[00:00.00]Hola\"}]";
        using var lyrics = new OnlineLyrics(new HttpClient(new Handler((request, token) => { token.ThrowIfCancellationRequested(); requests.Add(request.RequestUri!); return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }); })));
        var row = (await lyrics.Search("Canción & Artist", CancellationToken.None)).Single();
        Check(row.HasPlain && row.HasSynced && row.Title == "Canción" && requests[0].Host == "lrclib.net" && requests[0].Query.Contains("%26"), "Search encodes metadata and parses synchronized Unicode lyrics.");
        body = body[1..^1]; await lyrics.Match("Title", "Artist", "", 12.5, CancellationToken.None);
        Check(requests[^1].Query.Contains("duration=12.5"), "Exact matching uses invariant duration and accepts single records.");
        string folder = Path.Combine(root, "lyrics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string lrc = Path.Combine(folder, "lyrics.lrc"), txt = Path.Combine(folder, "lyrics.txt");
        await OnlineLyrics.Export(row, true, lrc, CancellationToken.None); await OnlineLyrics.Export(row, false, txt, CancellationToken.None);
        Check(File.ReadAllText(txt) == "Hola", "Plain export retains text."); await Reject(() => OnlineLyrics.Export(row, true, lrc, CancellationToken.None));
        status = HttpStatusCode.NotFound; Check((await lyrics.Search("missing", CancellationToken.None)).Length == 0, "Not found returns no results.");
        status = HttpStatusCode.TooManyRequests; int before = requests.Count; await Reject(() => lyrics.Search("limited", CancellationToken.None)); Check(requests.Count == before + 1, "Rate limit does not retry.");
        status = HttpStatusCode.OK; body = "invalid"; await Reject(() => lyrics.Search("broken", CancellationToken.None));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Reject(() => lyrics.Search("canceled", cancel.Token));
        await Reject(() => lyrics.Match("x", "y", "", double.NaN, CancellationToken.None));
    }
}
