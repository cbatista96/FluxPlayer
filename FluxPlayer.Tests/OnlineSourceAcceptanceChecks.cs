using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class OnlineSourceAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action)
    { bool rejected = false; try { await action(); } catch (Exception ex) when (ex is ArgumentException or IOException or OperationCanceledException) { rejected = true; } Check(rejected, "Invalid operation must be rejected."); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string tools = Environment.GetEnvironmentVariable("FLUXPLAYER_TEST_YTDLP") ?? Path.GetFullPath(Path.Combine(root, "../tools/yt-dlp-2026.10.26/yt-dlp.exe"));
        if (!File.Exists(tools)) return;
        string originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(tools) + Path.PathSeparator + originalPath);
        Fixture? fixture = null;
        async Task<Fixture> Prepare() => fixture ??= await Fixture.Create(root);
        try
        {
            await test("F0363 yt-dlp actual acceptance: bounded HTML5 page resolution and cancellation", async () =>
            {
                var f = await Prepare(); var info = await OnlineMediaSource.Inspect(f.Url + "video.html", CancellationToken.None);
                Check(info.Title.Contains("Synthetic", StringComparison.Ordinal) && info.Formats.Any(v => v.Extension == "mp4") && info.Formats.All(v => v.Address.StartsWith(f.Url, StringComparison.Ordinal)), "Real yt-dlp resolves supplied local HTML5 media.");
                await File.WriteAllTextAsync(Path.Combine(f.Folder, "video-info.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
                using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Reject(async () => await OnlineMediaSource.Inspect(f.Url + "video.html", canceled.Token));
                Environment.SetEnvironmentVariable("PATH", originalPath);
                try { Check((await OnlineMediaSource.Inspect(f.Url + "video.html", CancellationToken.None, executable: tools)).Formats.Length > 0, "Explicit trusted executable works without adding its directory to PATH."); }
                finally { Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(tools) + Path.PathSeparator + originalPath); }
                await Reject(async () => await OnlineMediaSource.Inspect(f.Url + "video.html", CancellationToken.None, executable: "relative.exe"));
                var direct = await OnlineMediaSource.Inspect(f.Url + "clip.mp4", CancellationToken.None); Check(direct.Formats.Any(format => format.Address == f.Url + "clip.mp4"), "Direct media metadata also exposes a playable format.");
                foreach (string invalid in new[] { "http://example.test/page", "file:///C:/media", "https://user:password@example.test/", "http://localhost/page" }) await Reject(async () => await OnlineMediaSource.Inspect(invalid, CancellationToken.None));
            });
            await test("F0364 yt-dlp actual acceptance: selected resolved video decodes in native player", async () =>
            {
                var f = await Prepare(); var info = await OnlineMediaSource.Inspect(f.Url + "video.html", CancellationToken.None); var format = info.Formats.First(v => v.Extension == "mp4");
                using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); engine.SetVolume(0); engine.PlayFile(format.Address); await Until(() => engine.IsPlaying && engine.GetTime() >= 150, "Resolved video must actually play."); engine.Stop();
            });
            await test("F0365 yt-dlp actual acceptance: authored audio-only format selection and playback", async () =>
            {
                var f = await Prepare(); var info = await OnlineMediaSource.Inspect(f.Url + "audio.html", CancellationToken.None); await File.WriteAllTextAsync(Path.Combine(f.Folder, "audio-info.json"), JsonSerializer.Serialize(info));
                var format = info.Formats.LastOrDefault(v => v.VideoCodec == "none" && v.AudioCodec != "none"); Check(format != null, "HTML5 audio exposes an audio-only format.");
                using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); engine.SetVolume(0); engine.PlayFile(format!.Address); await Until(() => engine.IsPlaying && engine.GetTime() >= 150, "Resolved audio must actually play."); engine.Stop();
            });
            await test("F0366 yt-dlp actual acceptance: live HLS detection and actual native playback", async () =>
            {
                var f = await Prepare(); var info = await OnlineMediaSource.Inspect(f.Url + "live.m3u8", CancellationToken.None); Check(info.Live && info.Formats.Length > 0, "No-ENDLIST HLS is identified as live by real yt-dlp.");
                using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); engine.SetVolume(0); engine.PlayFile(info.Formats[0].Address); await Until(() => engine.IsPlaying && engine.GetTime() >= 150, "Resolved live manifest actually decodes."); engine.Stop();
            });
            await test("F0367 yt-dlp actual acceptance: ordered HTML5 playlist references resolve independently", async () =>
            {
                var f = await Prepare(); var pages = await OnlineMediaSource.Playlist(f.Url + "playlist.html", CancellationToken.None); Check(pages.Length == 2 && pages.All(p => p.Path.StartsWith(f.Url, StringComparison.Ordinal)), "Two authored media entries survive ordered playlist resolution.");
                foreach (var page in pages) Check((await OnlineMediaSource.Inspect(page.Path, CancellationToken.None, page.PlaylistIndex)).Formats.Length > 0, "Each selected playlist occurrence resolves independently.");
                Check(pages[0].PlaylistIndex == 1 && pages[1].PlaylistIndex == 2, "Shared-page entries retain distinct original positions.");
                var second = await OnlineMediaSource.Inspect(pages[1].Path, CancellationToken.None, pages[1].PlaylistIndex); var selected = second.Formats.Single(); Check(selected.VideoCodec == "none" && selected.Extension == "m4a", "Selecting the second occurrence resolves its audio, not the first entry's video.");
                string saved = Path.Combine(f.Folder, "Playlist second entry.m4a"); await OnlineMediaSource.Download(second, selected, saved, CancellationToken.None); Check((await File.ReadAllBytesAsync(saved)).SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(f.Folder, "audio.m4a"))), "Download preserves selected occurrence identity and exact native audio bytes.");
            });
            foreach (bool audio in new[] { false, true })
                await test($"F{(audio ? 369 : 368):0000} yt-dlp actual acceptance: native {(audio ? "audio" : "video")} download, collisions and independent decode", async () =>
                {
                    var f = await Prepare(); var info = await OnlineMediaSource.Inspect(f.Url + (audio ? "audio.html" : "video.html"), CancellationToken.None); var format = info.Formats.Last(v => audio ? v.VideoCodec == "none" && v.AudioCodec != "none" : v.Extension == "mp4"); string destination = Path.Combine(f.Folder, "Saved ñ." + format.Extension);
                    await OnlineMediaSource.Download(info, format, destination, CancellationToken.None); Check(File.Exists(destination) && new FileInfo(destination).Length > 100, "Real yt-dlp publishes complete bytes.");
                    string decoded = Path.Combine(f.Folder, audio ? "decoded-audio.wav" : "decoded-video.wav"); await FfmpegJob.Run(["-loglevel", "error", "-i", destination, "-vn", "-c:a", "pcm_s16le", decoded], CancellationToken.None); Check(new FileInfo(decoded).Length > 48000, "Independent decoder reads a full audio payload.");
                    byte[] before = await File.ReadAllBytesAsync(destination); await Reject(() => OnlineMediaSource.Download(info, format, destination, CancellationToken.None)); Check(before.SequenceEqual(await File.ReadAllBytesAsync(destination)), "Existing destination remains intact.");
                    await Reject(() => OnlineMediaSource.Download(info with { Live = true }, format, Path.Combine(f.Folder, "live." + format.Extension), CancellationToken.None));
                    await Reject(() => OnlineMediaSource.Download(info, format with { Id = "absent" }, Path.Combine(f.Folder, "forged." + format.Extension), CancellationToken.None));
                });
            await test("F0370 yt-dlp actual acceptance: authored WebVTT language selection and exact download", async () =>
            {
                var f = await Prepare(); var info = await OnlineMediaSource.Inspect(f.Url + "video.html", CancellationToken.None); var caption = info.Captions.Single(v => v.Language == "es"); string saved = Path.Combine(f.Folder, "Saved captions ñ.vtt");
                await OnlineMediaSource.DownloadCaption(caption, saved, CancellationToken.None); Check((await File.ReadAllBytesAsync(saved)).SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(f.Folder, "captions.vtt"))), "Selected authored Unicode caption bytes are preserved.");
                Check(SubtitleDocument.Read(saved).Length == 1, "Independent subtitle parser accepts the downloaded cues."); await Reject(() => OnlineMediaSource.DownloadCaption(caption, saved, CancellationToken.None)); await Reject(() => OnlineMediaSource.DownloadCaption(caption with { Address = f.Url + "video.html" }, Path.Combine(f.Folder, "invalid.vtt"), CancellationToken.None)); Check(!File.Exists(Path.Combine(f.Folder, "invalid.vtt")), "Non-WebVTT response is never published.");
            });
        }
        finally { if (fixture != null) await fixture.DisposeAsync(); Environment.SetEnvironmentVariable("PATH", originalPath); }
    }
    private static async Task Until(Func<bool> predicate, string message)
    { var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed.TotalSeconds > 10) throw new InvalidOperationException(message); await Task.Delay(30); } }
    private sealed class Fixture : IAsyncDisposable
    {
        public required string Folder { get; init; }
        public required string Url { get; init; }
        private readonly HttpListener _http = new();
        private Task? _loop;
        private readonly List<Task> _requests = [];
        public static async Task<Fixture> Create(string root)
        {
            string folder = Path.Combine(root, "online-actual-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=160x90:r=25:d=4", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=4", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-movflags", "+faststart", Path.Combine(folder, "clip.mp4")], CancellationToken.None);
            await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=4", "-c:a", "aac", Path.Combine(folder, "audio.m4a")], CancellationToken.None);
            await FfmpegJob.Run(["-loglevel", "error", "-stream_loop", "3", "-i", Path.Combine(folder, "clip.mp4"), "-c:v", "libx264", "-preset", "ultrafast", "-g", "25", "-c:a", "aac", "-hls_time", "1", "-hls_list_size", "0", "-hls_flags", "omit_endlist", Path.Combine(folder, "live.m3u8")], CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(folder, "video.html"), "<!doctype html><html><head><title>Synthetic video ñ</title></head><body><video controls><source src='/clip.mp4' type='video/mp4'><track src='/captions.vtt' kind='subtitles' srclang='es' label='Español'></video></body></html>");
            await File.WriteAllTextAsync(Path.Combine(folder, "audio.html"), "<!doctype html><html><head><title>Synthetic audio ñ</title></head><body><audio controls src='/audio.m4a'></audio></body></html>");
            await File.WriteAllTextAsync(Path.Combine(folder, "playlist.html"), "<!doctype html><html><head><title>Synthetic playlist</title></head><body><video src='/clip.mp4'></video><audio src='/audio.m4a'></audio></body></html>");
            await File.WriteAllTextAsync(Path.Combine(folder, "captions.vtt"), "WEBVTT\n\n00:00.000 --> 00:02.000\nTexto sintético ñ\n", new UTF8Encoding(false));
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop(); var fixture = new Fixture { Folder = folder, Url = $"http://127.0.0.1:{port}/" }; fixture._http.Prefixes.Add(fixture.Url); fixture._http.Start(); fixture._loop = Task.Run(fixture.Loop); return fixture;
        }
        private async Task Loop()
        {
            try { while (_http.IsListening) { var context = await _http.GetContextAsync(); _requests.Add(Serve(context)); } }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { }
        }
        private async Task Serve(HttpListenerContext context)
        {
            try
            {
                string name = context.Request.Url!.AbsolutePath.TrimStart('/'); if (name.Contains('/') || name.Contains("..", StringComparison.Ordinal)) { context.Response.StatusCode = 404; return; }
                string file = Path.Combine(Folder, name); if (!File.Exists(file)) { context.Response.StatusCode = 404; return; }
                byte[] bytes = await File.ReadAllBytesAsync(file); context.Response.ContentType = Path.GetExtension(file) switch { ".html" => "text/html; charset=utf-8", ".mp4" => "video/mp4", ".m4a" => "audio/mp4", ".m3u8" => "application/vnd.apple.mpegurl", ".ts" => "video/mp2t", ".vtt" => "text/vtt; charset=utf-8", _ => "application/octet-stream" }; context.Response.Headers["Accept-Ranges"] = "bytes";
                int offset = 0, count = bytes.Length; string? range = context.Request.Headers["Range"]; if (range?.StartsWith("bytes=", StringComparison.Ordinal) == true) { var parts = range[6..].Split('-'); offset = int.Parse(parts[0]); if (offset >= bytes.Length) { context.Response.StatusCode = 416; return; } int end = parts.Length > 1 && int.TryParse(parts[1], out int requested) ? Math.Min(requested, bytes.Length - 1) : bytes.Length - 1; count = end - offset + 1; context.Response.StatusCode = 206; context.Response.Headers["Content-Range"] = $"bytes {offset}-{end}/{bytes.Length}"; }
                context.Response.ContentLength64 = count; if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes.AsMemory(offset, count));
            }
            catch (Exception e) when (e is IOException or HttpListenerException or ObjectDisposedException) { }
            finally { context.Response.Close(); }
        }
        public async ValueTask DisposeAsync() { _http.Close(); if (_loop != null) await _loop; await Task.WhenAll(_requests); }
    }
}
