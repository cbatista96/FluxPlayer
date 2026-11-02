using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static class CompletionChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> work)
    { try { await work(); } catch (Exception e) when (e is ArgumentException or IOException or OperationCanceledException or JsonException) { return; } throw new InvalidOperationException("Expected rejection."); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private static string Folder(string root, string name) { string folder = Path.Combine(root, "completion-" + name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
    public static async Task Preferences(string root)
    {
        var layout = new HudLayout { Enabled = true }; layout.Validate();
        var placed = HudLayout.Place(new("PlayPause", 1, 1, 120, 40), 80, 30); Check(placed == (0d, 0d, 80d, 30d), "Widgets stay inside a small canvas.");
        await Reject(() => { (layout with { Widgets = [new("Unknown", 0, 0, 120, 40)] }).Validate(); return Task.CompletedTask; });
        await Reject(() => { (layout with { Widgets = [new("Time", double.NaN, 0, 120, 40)] }).Validate(); return Task.CompletedTask; });
        var policy = new GpuRenderPolicy(true, Nvidia: "dxva2");
        Check(policy.Resolve([new("GPU", "", true, "NVIDIA")], true) == "dxva2" && policy.Resolve([new("CPU", "", true, "Microsoft")], true) == "none" && policy.Resolve([], false) == "none", "Vendor policy respects explicit rules and software overrides.");
        await Reject(() => { (policy with { Amd = "invalid" }).Validate(); return Task.CompletedTask; });
        var settings = new SettingsService(Folder(root, "preferences")); var value = new AppSettings { Enhancements = new() { Hud = layout, GpuPolicy = policy } }; settings.Save(value);
        Check(settings.Load().Enhancements.Hud.Enabled && settings.Load().Enhancements.GpuPolicy.Nvidia == "dxva2", "HUD and GPU rules survive profile reload.");
        Check(GpuRenderPolicy.Detect().Length <= 32, "Adapter enumeration is bounded.");
    }
    public static async Task Duplicates(string root, string wav)
    {
        string folder = Folder(root, "duplicates"), profile = Folder(root, "duplicate-profile");
        string a = Path.Combine(folder, "a.wav"), b = Path.Combine(folder, "b.wav"); File.Copy(wav, a); File.Copy(wav, b);
        await using (var library = new LibraryService(profile))
        {
            await library.ScanFolderAsync(folder, false); var groups = (await DuplicateFinder.FindAsync(await library.SnapshotAsync())).Groups;
            Check(await library.AutoConsolidate(groups, null, CancellationToken.None) == 1, "Exact groups consolidate automatically.");
            await library.ScanFolderAsync(folder, false); Check((await library.SnapshotAsync()).Length == 1, "Rescans do not resurrect consolidated entries.");
        }
        await using (var library = new LibraryService(profile))
        {
            await library.ScanFolderAsync(folder, false); Check((await library.SnapshotAsync()).Length == 1, "Exclusions survive restart.");
            string backup = Path.Combine(profile, "backup.json"); await library.ExportBackupAsync(backup);
            await using var restored = new LibraryService(Folder(root, "duplicate-restored")); await restored.ImportBackupAsync(backup); await restored.ScanFolderAsync(folder, false);
            Check((await restored.SnapshotAsync()).Length == 1, "Backup round trip retains consolidation exclusions.");
            Check(await library.RestoreConsolidatedDuplicates(CancellationToken.None) == 1, "Exclusions can be restored explicitly.");
            await library.ScanFolderAsync(folder, false); Check((await library.SnapshotAsync()).Length == 2, "Restoring permits originals to reimport.");
            var plan = await library.PreviewDuplicateMerge(a, [b], CancellationToken.None); await library.ApplyDuplicateMerge(plan, CancellationToken.None);
            await File.AppendAllTextAsync(b, "Changed"); await library.ScanFolderAsync(folder, false);
            Check((await library.SnapshotAsync()).Length == 2 && File.Exists(a) && File.Exists(b), "Changed copies reappear; originals are preserved.");
        }
    }
    public static async Task Share(string root, string wav)
    {
        Check(MediaFileShare.Range("bytes=5-9", 20) == (5L, 9L) && MediaFileShare.Range("bytes=-5", 20) == (15L, 19L), "Normal and suffix ranges.");
        await Reject(() => { MediaFileShare.Range("bytes=99-", 20); return Task.CompletedTask; });
        var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        await using var share = new MediaFileShare(); share.Start(wav, "127.0.0.1", port); string address = share.Address;
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, address); request.Headers.Range = new(0, 15);
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.PartialContent && (await response.Content.ReadAsByteArrayAsync()).SequenceEqual(File.ReadAllBytes(wav).Take(16)), "HTTP range reads actual selected media bytes.");
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, address)); Check(head.Content.Headers.ContentLength == new FileInfo(wav).Length, "HEAD advertises full length.");
        using var denied = await client.GetAsync("http://127.0.0.1:" + port + "/different"); Check(denied.StatusCode == HttpStatusCode.NotFound, "Unknown paths are inaccessible.");
        await share.DisposeAsync(); Check(share.Address == "", "Stopping clears the share URL.");
    }
    public static async Task Subtitles(string root)
    {
        var requests = new List<string>(); bool badOrigin = false; bool sawCredentialsOnFile = false;
        var api = new HttpClient(new Handler(async (request, token) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath); string body;
            if (request.RequestUri.AbsolutePath.EndsWith("login")) body = "{\"token\":\"session-token\",\"base_url\":\"api.opensubtitles.com\"}";
            else if (request.RequestUri.AbsolutePath.EndsWith("subtitles")) body = "{\"data\":[{\"attributes\":{\"language\":\"en\",\"release\":\"Fixture\",\"download_count\":5,\"moviehash_match\":true,\"files\":[{\"file_id\":42}]}}]}";
            else { Check((await request.Content!.ReadAsStringAsync(token)).Contains("42"), "Selected file ID is posted."); body = "{\"link\":\"https://" + (badOrigin ? "untrusted.example" : "www.opensubtitles.com") + "/download/fixture\"}"; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        var files = new HttpClient(new Handler((request, token) => { sawCredentialsOnFile |= request.Headers.Authorization != null || request.Headers.Contains("Api-Key"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("1\n00:00:00,000 --> 00:00:01,000\nHello\n") }); }));
        using var provider = new SubtitleDownloads("fixture-key", api, files); await provider.Login("user", "password", CancellationToken.None);
        string folder = Folder(root, "subtitles"), media = Path.Combine(folder, "media.bin"); await File.WriteAllBytesAsync(media, new byte[131072]);
        Check(await SubtitleDownloads.MovieHash(media, CancellationToken.None) == "0000000000020000", "OpenSubtitles little-endian file hash.");
        var rows = await provider.Search("Example", "en", media, CancellationToken.None); Check(rows.Single().HashMatched, "Exact movie hash match is retained.");
        string path = Path.Combine(folder, "subtitle.srt"); await provider.Download(rows[0], path, CancellationToken.None);
        Check((await SubtitleWorkshop.Read(path, CancellationToken.None)).Single().Text == "Hello" && !sawCredentialsOnFile, "Download validates real SRT and separates API credentials from CDN requests.");
        await Reject(() => provider.Download(rows[0], path, CancellationToken.None)); badOrigin = true;
        await Reject(() => provider.Download(rows[0], Path.Combine(folder, "bad.srt"), CancellationToken.None));
        Check(!File.Exists(Path.Combine(folder, "bad.srt")), "Foreign origin does not publish a subtitle file.");
    }
    public static async Task Sync(string root, string wav)
    {
        string remoteFolder = Folder(root, "sync-remote"); await using var remoteLibrary = new LibraryService(remoteFolder);
        await remoteLibrary.UpdateMetadataAsync(wav, new("Remote", "Artist", "Album", "", "", 8000, true, false, ""));
        string export = Path.Combine(remoteFolder, "catalogue.json"); await remoteLibrary.ExportBackupAsync(export); byte[] bytes = await File.ReadAllBytesAsync(export);
        bool conflict = false; int writes = 0;
        using var transport = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; response.Headers.ETag = new("\"r1\""); return response; }
            Check(request.Headers.IfMatch.Single().Tag == "\"r1\"", "Upload uses the reviewed ETag."); writes++;
            if (conflict) return new(HttpStatusCode.PreconditionFailed);
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token)); Check(doc.RootElement.GetProperty("Entries").GetArrayLength() == 1, "Merged catalogue is uploaded."); return new(HttpStatusCode.NoContent);
        }));
        using var sync = new RemoteLibrarySync("https://sync.example/catalogue.json", "", "", transport);
        await using var local = new LibraryService(Folder(root, "sync-local"));
        var preview = await sync.Preview(CancellationToken.None); var result = await sync.Synchronize(local, preview, Folder(root, "sync-jobs"), CancellationToken.None);
        Check(result?.AddedFiles == 1 && (await local.SnapshotAsync()).Single().Artist == "Artist" && writes == 1, "Remote catalogue merges and publishes.");
        conflict = true; await Reject(() => sync.Synchronize(local, preview, Folder(root, "sync-conflict"), CancellationToken.None));
        Check((await local.SnapshotAsync()).Length == 1, "Conflict retains the local merged catalogue.");
        await Reject(() => sync.Synchronize(local, preview with { Hash = "tampered" }, Folder(root, "sync-tamper"), CancellationToken.None));
    }
    public static async Task Linked(string root, string wav)
    {
        string folder = Folder(root, "linked"), source = Path.Combine(folder, "primary.wav"), second = Path.Combine(folder, "second.wav"); File.Copy(wav, source); File.Copy(wav, second);
        string xml = Path.Combine(folder, "chapters.xml");
        await File.WriteAllTextAsync(xml, "<Chapters><EditionEntry><EditionUID>1</EditionUID><EditionFlagOrdered>1</EditionFlagOrdered><ChapterAtom><ChapterUID>1</ChapterUID><ChapterTimeStart>00:00:00.000</ChapterTimeStart><ChapterTimeEnd>00:00:04.000</ChapterTimeEnd><ChapterAtom><ChapterUID>2</ChapterUID><ChapterTimeStart>00:00:01.000</ChapterTimeStart><ChapterTimeEnd>00:00:02.000</ChapterTimeEnd><ChapterDisplay><ChapterString>English</ChapterString><ChapterLanguage>eng</ChapterLanguage></ChapterDisplay><ChapterDisplay><ChapterString>Español</ChapterString><ChapterLanguage>spa</ChapterLanguage></ChapterDisplay></ChapterAtom><ChapterAtom><ChapterUID>3</ChapterUID><ChapterTimeStart>00:00:03.000</ChapterTimeStart><ChapterTimeEnd>00:00:04.000</ChapterTimeEnd><ChapterSegmentUID>11111111111111111111111111111111</ChapterSegmentUID></ChapterAtom></ChapterAtom></EditionEntry></Chapters>");
        var project = MatroskaChapterProject.Read(xml); var map = new Dictionary<string, string> { ["11111111111111111111111111111111"] = second };
        var steps = LinkedChapters.Compile(project, "1", source, map, "spa");
        Check(steps.Length == 2 && steps[0].Title == "Español" && steps[1].Source == second, "Nested leaf order, selected language and linked source mapping.");
        await Reject(() => { LinkedChapters.Compile(project, "1", source, new Dictionary<string, string>(), "eng"); return Task.CompletedTask; });
        await File.AppendAllTextAsync(second, "changed"); await Reject(() => { LinkedChapters.Verify(steps[1]); return Task.CompletedTask; });
        Check(ChannelMastering.Speakers("2.1").SequenceEqual(new[] { "FL", "FR", "LFE" }), "2.1 has the proper named LFE output.");
    }
    public static async Task NativeLinked(string root, string wav)
    {
        string folder = Folder(root, "native-linked"); string second = Path.Combine(folder, "second.wav"); File.Copy(wav, second);
        LinkedChapterStep Step(string path, long start, long end) { var info = new FileInfo(path); return new(path, start, end, "Chapter", info.Length, info.LastWriteTimeUtc.Ticks); }
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder);
        var vm = new FluxPlayer.ViewModels.MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        async Task Wait(Func<bool> condition) { var timer = System.Diagnostics.Stopwatch.StartNew(); while (!condition()) { if (timer.ElapsedMilliseconds > 12000) throw new TimeoutException("Native chapter sequence did not advance/stop."); await Task.Delay(25); } }
        try
        {
            vm.PlayLinkedEdition([Step(wav, 1000, 2000), Step(second, 3000, 4000)]);
            await Wait(() => vm.CurrentItem?.FilePath == second && engine.GetTime() >= 3000);
            await Wait(() => !vm.IsLinkedEditionActive && !vm.IsPlaying);
            vm.PlayLinkedEdition([Step(wav, 7000, 8000), Step(second, 1000, 2000)]);
            await Wait(() => vm.CurrentItem?.FilePath == second && engine.GetTime() >= 1000);
            await Wait(() => !vm.IsLinkedEditionActive && !vm.IsPlaying);
            vm.PlayLinkedEdition([Step(wav, 1000, 3000)]); vm.StopCommand.Execute(null); Check(!vm.IsLinkedEditionActive, "Manual Stop cancels linked sequencing.");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static async Task NativeUi(string root)
    {
        string folder = Folder(root, "ui"); using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        await using var library = new LibraryService(folder); var vm = new FluxPlayer.ViewModels.MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        var owner = new System.Windows.Window { Width = 1000, Height = 740, ShowActivated = false }; owner.Show();
        try
        {
            int commits = 0;
            Func<System.Windows.Window>[] factories = [() => new FluxPlayer.Windows.HudLayoutWindow(owner, vm, _ => commits++), () => new FluxPlayer.Windows.GpuPolicyWindow(owner, vm, _ => commits++), () => new FluxPlayer.Windows.RemoteLibrarySyncWindow(owner, vm), () => new FluxPlayer.Windows.SubtitleDownloadsWindow(owner, vm), () => new FluxPlayer.Windows.CastingWindow(owner, vm), () => new FluxPlayer.Windows.LinkedChaptersWindow(owner, vm), () => new FluxPlayer.Windows.RealtimeAudioWindow(owner, vm), () => new FluxPlayer.Windows.NativeDsdWindow(owner, vm)];
            factories = factories.Concat(new Func<System.Windows.Window>[] { () => new FluxPlayer.Windows.ProcessingPaletteWindow(owner, vm, false), () => new FluxPlayer.Windows.ProcessingPaletteWindow(owner, vm, true), () => new FluxPlayer.Windows.MediaDiagnosticsWindow(owner, vm, true), () => new FluxPlayer.Windows.MediaDiagnosticsWindow(owner, vm, false), () => new FluxPlayer.Windows.SubtitleWorkshopWindow(owner, vm), () => new FluxPlayer.Windows.PlaybackCompatibilityWindow(owner, vm), () => new FluxPlayer.Windows.AutomaticProfilesWindow(owner, vm), () => new FluxPlayer.Windows.ExperienceWindow(owner, vm, _ => Task.CompletedTask), () => new FluxPlayer.Windows.SubtitleOcrWindow(owner, vm), () => new FluxPlayer.Windows.OnlineLyricsWindow(owner, vm), () => new FluxPlayer.Windows.PlayerExtensionsWindow(owner, vm), () => new FluxPlayer.Windows.DuplicateFilesWindow(owner, vm), () => new FluxPlayer.Windows.OnlineSourceWindow(owner, vm), () => new FluxPlayer.Windows.OnlinePlaylistWindow(owner, vm, [new("https://example.test/playlist", "Second occurrence", 2)]) }).ToArray();
            foreach (var factory in factories)
            {
                var window = factory(); window.ShowActivated = false;
                var testTheme = new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark };
                window.Resources.MergedDictionaries.Insert(0, testTheme);
                window.Resources.MergedDictionaries.Insert(1, new Wpf.Ui.Markup.ControlsDictionary());
                try
                {
                    window.Show(); window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 32)); window.UpdateLayout(); await Task.Delay(30); window.UpdateLayout();
                    Check(window.ActualWidth >= 600 && window.ActualHeight >= 400, "Feature panel is arranged on the actual desktop.");
                    IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject node)
                    {
                        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) { var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
                    }
                    foreach (var combo in Descendants(window).OfType<System.Windows.Controls.ComboBox>().Where(c => c.IsVisible && c.ActualHeight > 0 && window is FluxPlayer.Windows.RealtimeAudioWindow or FluxPlayer.Windows.NativeDsdWindow))
                    {
                        var toggle = Descendants(combo).OfType<System.Windows.Controls.Primitives.ToggleButton>().FirstOrDefault();
                        Check(toggle != null && toggle.ActualWidth >= combo.ActualWidth - 2, $"The dropdown background and hit target span its visible selection width: {window.GetType().Name}, combo={combo.ActualWidth}, toggle={toggle?.ActualWidth}, template={combo.Template}.");
                    }
                    System.Windows.Controls.TextBox? Input(System.Windows.DependencyObject node)
                    {
                        if (node is System.Windows.Controls.TextBox box) return box;
                        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) { var found = Input(System.Windows.Media.VisualTreeHelper.GetChild(node, i)); if (found != null) return found; } return null;
                    }
                    var input = Input(window);
                    if (input != null) Check(input.Background is System.Windows.Media.SolidColorBrush background && background.Color.R < 128 && input.Foreground is System.Windows.Media.SolidColorBrush foreground && foreground.Color.R > 128, $"Actual dark-theme inputs have readable contrasting colors: {window.GetType().Name} bg={input.Background} fg={input.Foreground}.");
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(folder, window.GetType().Name + ".png")); encoder.Save(output);
                    if (window is FluxPlayer.Windows.HudLayoutWindow or FluxPlayer.Windows.GpuPolicyWindow)
                    {
                        System.Windows.Controls.Button? Find(System.Windows.DependencyObject node, string text)
                        {
                            if (node is System.Windows.Controls.Button b && b.Content?.ToString() == text) return b;
                            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) { var found = Find(System.Windows.Media.VisualTreeHelper.GetChild(node, i), text); if (found != null) return found; } return null;
                        }
                        var save = Find(window, window is FluxPlayer.Windows.HudLayoutWindow ? "Save HUD" : "Save policy"); Check(save != null, "Save action is available.");
                        save!.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Task.Delay(20);
                    }
                    testTheme.Theme = Wpf.Ui.Appearance.ApplicationTheme.Light; ThemeService.Apply(AppTheme.LightMica, window);
                    window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 246, 249)); window.UpdateLayout();
                    if (input != null) Check(input.Background is System.Windows.Media.SolidColorBrush lightBackground && lightBackground.Color.R > 128 && input.Foreground is System.Windows.Media.SolidColorBrush lightForeground && lightForeground.Color.R < 128, "Actual light-theme inputs have readable contrasting colors.");
                    var lightBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); lightBitmap.Render(window);
                    var lightEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); lightEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(lightBitmap));
                    using var lightOutput = File.Create(Path.Combine(folder, window.GetType().Name + "-light.png")); lightEncoder.Save(lightOutput);
                }
                finally { window.Close(); }
            }
            Check(commits == 2, "Actual HUD and GPU save actions forward validated preferences once.");
        }
        finally { owner.Close(); await vm.ShutdownAsync(); }
    }
}
