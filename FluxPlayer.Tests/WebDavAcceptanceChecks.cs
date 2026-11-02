using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

internal static class WebDavAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action)
    { try { await action(); } catch (Exception ex) when (ex is IOException or ArgumentException or OperationCanceledException or HttpRequestException) { return; } throw new InvalidOperationException("Expected operation to be rejected."); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string executable = Environment.GetEnvironmentVariable("FLUXPLAYER_TEST_DUFS") ?? Path.GetFullPath(Path.Combine(root, "../tools/dufs-0.47.0/dufs.exe")); if (!File.Exists(executable)) return;
        string python = Path.GetFullPath(Path.Combine(root, "../tools/python-3.14-embed/python.exe"));
        foreach (int id in Enumerable.Range(333, 10).Concat(File.Exists(python) ? new[] { 66 } : Array.Empty<int>())) await test($"F{id:0000} WebDAV native acceptance: {(id == 66 ? "conditional catalogue merge on WsgiDAV" : "real isolated dufs server")}", async () =>
        {
            string folder = Path.Combine(root, "dav-" + id + "-" + Guid.NewGuid().ToString("N")), served = Path.Combine(folder, "served"), profile = Path.Combine(folder, "profile"), downloads = Path.Combine(folder, "downloads"); Directory.CreateDirectory(served); Directory.CreateDirectory(profile); Directory.CreateDirectory(downloads); Directory.CreateDirectory(Path.Combine(served, "Album ñ"));
            string wav = Path.Combine(served, "Silence ñ.wav"); using (var writer = new BinaryWriter(File.Create(wav))) { int bytes = 48000 * 4 * 4; writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]); }
            string nested = Path.Combine(served, "Album ñ", "Track.wav"); File.Copy(wav, nested); await File.WriteAllTextAsync(Path.Combine(served, "List.m3u8"), "#EXTM3U\nSilence ñ.wav\nAlbum ñ/Track.wav\n"); byte[] original = await File.ReadAllBytesAsync(wav);
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop(); string address = $"http://127.0.0.1:{port}/";
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            // This child can read/upload only the newly created synthetic directory.
            // No delete, outside-root symlink, public-interface or service permission.
            foreach (string arg in new[] { served, "--bind", "127.0.0.1", "--port", port.ToString(), "--allow-upload", "--auth", "fixture:fixture-pass@/:rw" }) start.ArgumentList.Add(arg);
            if (id == 66)
            {
                string configuration = Path.Combine(folder, "wsgidav.json");
                var config = new { host = "127.0.0.1", port, server = "cheroot", verbose = 1, provider_mapping = new Dictionary<string, string> { ["/"] = served }, simple_dc = new { user_mapping = new Dictionary<string, object> { ["*"] = new Dictionary<string, object> { ["fixture"] = new { password = "fixture-pass" } } } }, http_authenticator = new { accept_basic = true, accept_digest = false, default_to_digest = false }, dir_browser = new { enable = false } };
                await File.WriteAllTextAsync(configuration, System.Text.Json.JsonSerializer.Serialize(config)); start.FileName = python; start.ArgumentList.Clear();
                foreach (string arg in new[] { "-m", "wsgidav.server.server_cli", "--config", configuration }) start.ArgumentList.Add(arg);
            }
            using var process = Process.Start(start)!; var errors = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync(); using var source = new WebDavSource(address, "fixture", "fixture-pass", profile);
            try
            {
                DavRow[] rows = []; var clock = Stopwatch.StartNew(); while (true) { try { rows = await source.List(address, CancellationToken.None); break; } catch (HttpRequestException) when (clock.Elapsed.TotalSeconds < 5) { await Task.Delay(40); } }
                DavRow row = rows.Single(r => r.Name == "Silence ñ.wav");
                switch (id)
                {
                    case 66:
                        await using (var firstLibrary = new LibraryService(Path.Combine(profile, "first")))
                        await using (var secondLibrary = new LibraryService(Path.Combine(profile, "second")))
                        using (var sync = new RemoteLibrarySync(address + "catalogue.json", "fixture", "fixture-pass"))
                        {
                            await firstLibrary.UpdateMetadataAsync(wav, new("First ñ", "Artist", "Album", "", "", 4000, true, false, ""));
                            await secondLibrary.UpdateMetadataAsync(nested, new("Second ñ", "Artist", "Album", "", "", 4000, true, false, ""));
                            var missing = await sync.Preview(CancellationToken.None); Check(missing.Missing, "Absent remote catalogue previews without creating an object.");
                            await sync.Synchronize(firstLibrary, missing, downloads, CancellationToken.None); var firstPreview = await sync.Preview(CancellationToken.None); Check(firstPreview.Entries == 1 && firstPreview.ETag != "", "Actual conditional PUT creates a catalogue with a strong GET ETag.");
                            await sync.Synchronize(secondLibrary, firstPreview, downloads, CancellationToken.None); Check((await secondLibrary.SnapshotAsync()).Length == 2 && (await sync.Preview(CancellationToken.None)).Entries == 2, "A separate profile merges remote and local metadata and uploads the union.");
                            await Reject(async () => await sync.Synchronize(firstLibrary, firstPreview, downloads, CancellationToken.None)); Check((await firstLibrary.SnapshotAsync()).Length == 1, "A stale preview is rejected before local merge.");
                            await sync.Synchronize(firstLibrary, await sync.Preview(CancellationToken.None), downloads, CancellationToken.None); Check((await firstLibrary.SnapshotAsync()).Length == 2, "Fresh two-way synchronization reaches both profiles.");
                            string remoteFile = Path.Combine(served, "catalogue.json"); var reviewed = await sync.Preview(CancellationToken.None);
                            using var racingSync = new RemoteLibrarySync(address + "catalogue.json", "fixture", "fixture-pass", new HttpClient(new CatalogueRace(remoteFile)));
                            await Reject(async () => await racingSync.Synchronize(firstLibrary, reviewed, downloads, CancellationToken.None)); Check((await File.ReadAllBytesAsync(remoteFile)).SequenceEqual(reviewed.Bytes.Concat(new byte[] { 10 })), "The real server refuses If-Match after an edit between final GET and PUT; competing remote bytes survive.");
                            using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Reject(async () => await sync.Synchronize(firstLibrary, reviewed, downloads, canceled.Token));
                            Check(!Directory.EnumerateFiles(downloads, "sync-*.json").Any() && original.SequenceEqual(await File.ReadAllBytesAsync(wav)), "Owned transfer files are cleaned and no media bytes are uploaded or altered.");
                        }
                        break;
                    case 333:
                        Check(rows.Length == 3 && rows[0].Folder && row.Bytes == original.Length, "Authenticated real PROPFIND preserves Unicode, type and size."); using (var denied = new WebDavSource(address, "fixture", "incorrect", profile)) await Reject(async () => await denied.List(address, CancellationToken.None)); break;
                    case 334:
                        var scan = await source.Scan(address, CancellationToken.None); Check(scan.Length == 3 && scan.All(r => !r.Folder) && scan.Any(r => r.Address.EndsWith("/Track.wav", StringComparison.Ordinal)), "Recursive view includes nested files exactly once."); await Reject(async () => await source.List("http://example.test/escape/", CancellationToken.None)); break;
                    case 335:
                        await using (var relay = new RemoteMediaRelay())
                        {
                            string url = relay.Register((range, token) => source.Stream(row.Address, range, token)); Check(!url.Contains("fixture", StringComparison.Ordinal), "Queue relay URL does not expose credentials."); using var client = new HttpClient(); using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Range = new(0, 43); using var reply = await client.SendAsync(request); Check(reply.StatusCode == HttpStatusCode.PartialContent && (await reply.Content.ReadAsByteArrayAsync()).SequenceEqual(original.Take(44)), "Actual relay preserves authenticated upstream range bytes.");
                            using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); engine.SetVolume(0); engine.PlayFile(url); clock.Restart(); while (!engine.IsPlaying || engine.GetTime() < 200) { if (clock.Elapsed.TotalSeconds > 5) throw new InvalidOperationException("Relayed WAV does not actually play."); await Task.Delay(30); } engine.Stop();
                        } break;
                    case 336:
                        string destination = Path.Combine(downloads, "Single.wav"); await source.Download(row, destination, CancellationToken.None); Check(original.SequenceEqual(await File.ReadAllBytesAsync(destination)), "Atomic download preserves actual server bytes."); await Reject(() => source.Download(row, destination, CancellationToken.None)); Check(original.SequenceEqual(await File.ReadAllBytesAsync(destination)), "Existing destinations are preserved."); using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); string never = Path.Combine(downloads, "Canceled.wav"); await Reject(() => source.Download(row, never, canceled.Token)); Check(!File.Exists(never), "Cancellation publishes no local copy."); } break;
                    case 337:
                        var selection = (await source.Scan(address, CancellationToken.None)).Where(r => r.Name.EndsWith(".wav", StringComparison.Ordinal)).ToArray(); foreach (var item in selection) await source.Download(item, Path.Combine(downloads, item.Name), CancellationToken.None); Check(Directory.GetFiles(downloads).Length == 2 && Directory.GetFiles(downloads).All(p => File.ReadAllBytes(p).SequenceEqual(original)), "Selected batch produces two independent intact new copies."); break;
                    case 338:
                        await source.Upload(wav, address, "Uploaded ñ.wav", CancellationToken.None); Check(original.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(served, "Uploaded ñ.wav"))), "Real PUT uploads the selected bytes."); await Reject(() => source.Upload(nested, address, "Uploaded ñ.wav", CancellationToken.None)); Check(original.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(served, "Uploaded ñ.wav"))), "If-None-Match prevents replacement."); break;
                    case 339:
                        await source.CreateFolder(address, "Created ñ", CancellationToken.None); Check(Directory.Exists(Path.Combine(served, "Created ñ")) && (await source.List(address, CancellationToken.None)).Any(r => r.Name == "Created ñ" && r.Folder), "Real MKCOL creates and lists requested collection."); await Reject(() => source.CreateFolder(address, "Created ñ", CancellationToken.None)); await Reject(() => source.CreateFolder(address, "..", CancellationToken.None)); break;
                    case 340:
                        var entries = await source.ReadPlaylist(rows.Single(r => r.Name == "List.m3u8"), CancellationToken.None); Check(entries.Length == 2 && entries[0].Address == row.Address && entries[1].Address.EndsWith("/Track.wav", StringComparison.Ordinal), "Remote UTF-8 playlist resolves relative media within root."); await File.WriteAllTextAsync(Path.Combine(served, "List.m3u8"), "https://example.test/outside.wav\n"); await Reject(async () => await source.ReadPlaylist(rows.Single(r => r.Name == "List.m3u8"), CancellationToken.None)); break;
                    case 341:
                        string resumed = Path.Combine(downloads, "Resumed.wav"), cache = Path.Combine(profile, "webdav-cache", RemoteMediaIO.Key(address + "\nfixture")); Directory.CreateDirectory(cache); string partial = Path.Combine(cache, RemoteMediaIO.Key(row.Address + "\n" + resumed) + ".partial"); string etag; using (var upstream = await source.Stream(row.Address, null, CancellationToken.None)) { Check(upstream.Headers.ETag is { IsWeak: false }, "Real server provides a strong GET ETag for resumption."); etag = upstream.Headers.ETag!.ToString(); } await File.WriteAllBytesAsync(partial, original[..12345]); await File.WriteAllTextAsync(partial + ".etag", etag);
                        using (var locked = new FileStream(partial + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { await Reject(() => source.ResumeDownload(row, resumed, CancellationToken.None)); Check((await File.ReadAllBytesAsync(partial)).SequenceEqual(original.Take(12345)), "Concurrent resume cannot modify another owner's partial bytes or validator."); }
                        await source.ResumeDownload(row, resumed, CancellationToken.None); Check(original.SequenceEqual(await File.ReadAllBytesAsync(resumed)) && !File.Exists(partial) && !File.Exists(partial + ".etag"), "Actual HTTP range resumes owned partial state and cleans metadata only after publication.");
                        string completed = Path.Combine(downloads, "Completed partial.wav"), fullPartial = Path.Combine(cache, RemoteMediaIO.Key(row.Address + "\n" + completed) + ".partial"); await File.WriteAllBytesAsync(fullPartial, original); await File.WriteAllTextAsync(fullPartial + ".etag", etag); await source.ResumeDownload(row, completed, CancellationToken.None); Check(original.SequenceEqual(await File.ReadAllBytesAsync(completed)), "A complete partial left by interrupted publication recovers from HTTP 416 instead of failing indefinitely."); break;
                    case 342:
                        string cached = await source.CachedCopy(row, CancellationToken.None), same = await source.CachedCopy(row, CancellationToken.None); Check(cached == same && original.SequenceEqual(await File.ReadAllBytesAsync(cached)), "Unchanged ETag reuses valid cached bytes."); byte[] changed = original.Concat(new byte[] { 1, 2, 3 }).ToArray(); await File.WriteAllBytesAsync(wav, changed); File.SetLastWriteTimeUtc(wav, DateTime.UtcNow.AddSeconds(2)); string refreshed = await source.CachedCopy(row, CancellationToken.None); Check(refreshed != cached && changed.SequenceEqual(await File.ReadAllBytesAsync(refreshed)), "Changed ETag publishes new cache content."); break;
                }
            }
            finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(errors, output); }
        });
    }
    private sealed class CatalogueRace(string path) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Put) await File.AppendAllTextAsync(path, "\n", token);
            return await base.SendAsync(request, token);
        }
    }
}
