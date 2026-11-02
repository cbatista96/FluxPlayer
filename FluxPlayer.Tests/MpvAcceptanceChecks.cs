using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal static class MpvAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<Task<bool>> predicate, string message)
    { var clock = Stopwatch.StartNew(); while (!await predicate()) { if (clock.Elapsed.TotalSeconds > 10) throw new InvalidOperationException(message); await Task.Delay(30); } }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string executable = Environment.GetEnvironmentVariable("FLUXPLAYER_TEST_MPV") ?? Path.GetFullPath(Path.Combine(root, "../tools/mpv-20261021/mpv.exe"));
        if (!File.Exists(executable)) return;
        string folder = Path.Combine(root, "mpv-" + Guid.NewGuid().ToString("N")), media = Path.Combine(folder, "Media ñ.mkv"), audio = Path.Combine(folder, "Silence.wav"), subtitle = Path.Combine(folder, "Captions ñ.srt"); bool prepared = false;
        async Task Prepare()
        {
            if (prepared) return; Directory.CreateDirectory(folder); await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:02,000\nFirst ñ\n\n2\n00:00:03,000 --> 00:00:05,000\nSecond\n");
            string metadata = Path.Combine(folder, "chapters.txt"); await File.WriteAllTextAsync(metadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=3000\ntitle=One\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=3000\nEND=6000\ntitle=Two\n");
            await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=6", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=6", "-i", subtitle, "-i", subtitle, "-f", "ffmetadata", "-i", metadata, "-map", "0:v", "-map", "1:a", "-map", "2:s", "-map", "3:s", "-map_metadata", "4", "-map_chapters", "4", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "pcm_s16le", "-c:s", "srt", media], CancellationToken.None);
            await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=6", "-c:a", "pcm_s16le", audio], CancellationToken.None);
            string editions = Path.Combine(folder, "editions.xml"); await File.WriteAllTextAsync(editions, "<?xml version=\"1.0\"?><Chapters><EditionEntry><EditionUID>100</EditionUID><EditionFlagDefault>1</EditionFlagDefault><ChapterAtom><ChapterUID>101</ChapterUID><ChapterTimeStart>00:00:00.000</ChapterTimeStart><ChapterTimeEnd>00:00:03.000</ChapterTimeEnd><ChapterDisplay><ChapterString>One</ChapterString><ChapterLanguage>eng</ChapterLanguage></ChapterDisplay></ChapterAtom><ChapterAtom><ChapterUID>102</ChapterUID><ChapterTimeStart>00:00:03.000</ChapterTimeStart><ChapterTimeEnd>00:00:06.000</ChapterTimeEnd><ChapterDisplay><ChapterString>Two</ChapterString><ChapterLanguage>eng</ChapterLanguage></ChapterDisplay></ChapterAtom></EditionEntry><EditionEntry><EditionUID>200</EditionUID><ChapterAtom><ChapterUID>201</ChapterUID><ChapterTimeStart>00:00:00.000</ChapterTimeStart><ChapterTimeEnd>00:00:06.000</ChapterTimeEnd><ChapterDisplay><ChapterString>Alternative</ChapterString><ChapterLanguage>eng</ChapterLanguage></ChapterDisplay></ChapterAtom></EditionEntry></Chapters>");
            string alternate = Path.Combine(folder, "Editions ñ.mkv"); await MatroskaTools.ApplyCopy(media, alternate, MatroskaChapterProject.Read(editions), CancellationToken.None); media = alternate; prepared = true;
        }
        await using var session = new MpvSession(); bool started = false;
        void Defaults(MpvSession target) { foreach (string key in new[] { "vo", "gpu-api", "gpu-context", "ao" }) { var option = MpvActionCatalog.Actions.Single(a => a.Key == key); target.Stage(option, key is "gpu-api" or "gpu-context" ? "d3d11" : option.Default); } }
        async Task Open(MpvSession target)
        {
            await target.Execute("mute", "true", false, CancellationToken.None); await target.Execute("pause", "true", false, CancellationToken.None); await target.Execute("open-file", media, false, CancellationToken.None);
            await Until(async () => (await target.Snapshot(CancellationToken.None)).GetProperty("track-list").GetArrayLength() >= 4, "Real mpv must decode the synthetic media's four tracks.");
        }
        for (int index = 0; index < MpvActionCatalog.Actions.Length; index++)
        {
            var action = MpvActionCatalog.Actions[index]; int id = 681 + index;
            await test($"F{id:0000} mpv native IPC acceptance: {action.Key}", async () =>
            {
                await Prepare();
                if (action.Mode == "startup")
                {
                    await using var isolated = new MpvSession(); Defaults(isolated); isolated.Stage(action, action.Default); await isolated.Start(executable, CancellationToken.None); await Open(isolated); Check((await isolated.Snapshot(CancellationToken.None)).GetProperty("audio-device-list").ValueKind == JsonValueKind.Array, "Staged option starts a working owned backend."); return;
                }
                if (!started) { Defaults(session); await session.Start(executable, CancellationToken.None); started = true; }
                await Open(session); await session.Execute("append-file", media, false, CancellationToken.None); await session.Execute("append-file", media, false, CancellationToken.None);
                string value = action.Kind == "path" ? action.Key.StartsWith("audio-", StringComparison.Ordinal) ? audio : action.Key.StartsWith("sub-", StringComparison.Ordinal) ? subtitle : media : action.Default;
                if (action.Kind == "bool" && action.Key is not ("fullscreen" or "ontop" or "pause" or "mute")) value = action.Default == "true" ? "false" : "true";
                if (action.Kind == "number" && action.Key != "volume") value = Math.Clamp(double.Parse(action.Default, System.Globalization.CultureInfo.InvariantCulture) + .25, action.Minimum, action.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (action.Key is "audio-remove" or "sub-remove")
                {
                    string kind = action.Key == "audio-remove" ? "audio" : "sub"; await session.Execute(kind + "-add", kind == "audio" ? audio : subtitle, false, CancellationToken.None);
                    await Until(async () => (await session.Snapshot(CancellationToken.None)).GetProperty("track-list").EnumerateArray().Any(t => t.GetProperty("type").GetString() == kind && t.TryGetProperty("external", out var external) && external.GetBoolean()), "External track attaches.");
                    value = (await session.Snapshot(CancellationToken.None)).GetProperty("track-list").EnumerateArray().Single(t => t.GetProperty("type").GetString() == kind && t.TryGetProperty("external", out var external) && external.GetBoolean()).GetProperty("id").GetInt32().ToString();
                }
                if (action.Key == "previous") await session.Execute("playlist-pos", "1", false, CancellationToken.None);
                if (action.Key == "chapter") value = "1";
                if (action.Key == "target-peak") { bool rejected = false; try { MpvActionCatalog.Value(action, "203.25"); } catch (ArgumentException) { rejected = true; } Check(rejected, "Fractional peak luminance is rejected before IPC because mpv requires an integer."); }
                if (action.Key == "seek-absolute") value = "2";
                if (action.Key == "seek-relative") value = "1";
                if (action.Key == "seek-revert") await session.Execute("seek-absolute", "2", false, CancellationToken.None);
                if (action.Key == "frame-back") await session.Execute("frame-next", "", false, CancellationToken.None);
                if (action.Key == "open-url")
                {
                    var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
                    using var http = new HttpListener(); string url = $"http://127.0.0.1:{port}/"; http.Prefixes.Add(url); http.Start();
                    using var cancellation = new CancellationTokenSource(); byte[] payload = await File.ReadAllBytesAsync(media);
                    var serving = Task.Run(async () => { try { while (!cancellation.IsCancellationRequested) { var request = await http.GetContextAsync().WaitAsync(cancellation.Token); request.Response.ContentLength64 = payload.Length; request.Response.ContentType = "video/x-matroska"; await request.Response.OutputStream.WriteAsync(payload, cancellation.Token); request.Response.Close(); } } catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { } });
                    try { await session.Execute(action.Key, url + "media.mkv", false, CancellationToken.None); await Until(async () => (await session.Snapshot(CancellationToken.None)).GetProperty("playlist").EnumerateArray().Any(p => p.GetProperty("filename").GetString() == url + "media.mkv"), "Actual HTTP media reaches the real backend queue."); }
                    finally { cancellation.Cancel(); http.Stop(); await serving; } return;
                }
                await session.Execute(action.Key, value, false, CancellationToken.None);
                if (action.Mode == "property")
                {
                    var actual = await session.Execute(action.Key, "", true, CancellationToken.None); if (actual.ValueKind == JsonValueKind.Array && actual.GetArrayLength() == 1) actual = actual[0]; object? expected = MpvActionCatalog.Value(action, value);
                    bool matches = expected switch { bool boolean => actual.ValueKind is JsonValueKind.True or JsonValueKind.False && actual.GetBoolean() == boolean, double numeric => actual.ValueKind == JsonValueKind.Number && Math.Abs(actual.GetDouble() - numeric) < .001, _ => actual.ToString().Equals(value, StringComparison.OrdinalIgnoreCase) || double.TryParse(actual.ToString(), out double parsed) && double.TryParse(value, out double wanted) && Math.Abs(parsed - wanted) < .001 };
                    matches |= value == "no" && actual.ValueKind == JsonValueKind.False || value == "yes" && actual.ValueKind == JsonValueKind.True;
                    Check(matches, $"Native property {action.Key} must acknowledge {value}; observed {actual}.");
                }
                else
                {
                    var snapshot = await session.Snapshot(CancellationToken.None);
                    if (action.Key == "queue-clear") Check(snapshot.GetProperty("playlist").GetArrayLength() == 1, "Clear retains only current source.");
                    if (action.Key == "queue-remove") Check(snapshot.GetProperty("playlist").GetArrayLength() == 2, "Remove deletes exactly one occurrence.");
                    if (action.Key == "append-file") Check(snapshot.GetProperty("playlist").GetArrayLength() == 4, "Append preserves repeated queue occurrences.");
                    if (action.Key is "sub-remove" or "audio-remove") Check(!snapshot.GetProperty("track-list").EnumerateArray().Any(t => t.GetProperty("type").GetString() == (action.Key == "audio-remove" ? "audio" : "sub") && t.GetProperty("id").ToString() == value), "Selected external track disappears.");
                }
            });
        }
    }
}
