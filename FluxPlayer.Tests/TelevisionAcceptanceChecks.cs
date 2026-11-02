using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

internal static class TelevisionAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<Task<bool>> predicate, string message)
    { var clock = Stopwatch.StartNew(); while (!await predicate()) { if (clock.Elapsed.TotalSeconds > 12) throw new InvalidOperationException(message); await Task.Delay(40); } }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (int id in Enumerable.Range(279, 7)) await test($"F{id:0000} Television acceptance: isolated catalog and loopback media", async () =>
        {
            string folder = Path.Combine(root, "television-" + id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string P(string name) => Path.Combine(folder, name); var library = new TelevisionLibrary(folder);
            string source = "http://127.0.0.1:18881/test.ts"; await using var share = new MediaFileShare();
            if (id is 279 or 282 or 283 or 284 or 285)
            {
                string media = P("Synthetic.ts"); await FfmpegJob.Run(["-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=128x72:r=25:d=22", "-c:v", "mpeg2video", "-g", "25", "-f", "mpegts", media], CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); share.Start(media, "127.0.0.1", port); source = share.Address;
            }
            var channel = new TvChannel(Guid.NewGuid().ToString("N"), "News, HD ñ", source, "Group, one", "news"); await library.SaveChannel(channel, CancellationToken.None);
            async Task Plays(string path)
            {
                using var engine = new MediaService(hardwareDecoding: false, videoOutputModule: "dummy", enableDiagnosticLogging: false); engine.SetVolume(0); engine.PlayFile(path); await Until(() => Task.FromResult(engine.IsPlaying && engine.GetTime() >= 200), "Actual VLC decodes the catalog source."); engine.Stop();
            }
            switch (id)
            {
                case 279:
                    var listed = await library.Snapshot(); Check(listed.Channels.Single() == channel, "Channel navigation retains source identity."); await Plays(listed.Channels.Single().Source); break;
                case 280:
                    string guide = P("Guide.xml"); await File.WriteAllTextAsync(guide, "<tv><programme channel=\"news\" start=\"20261031080000 -0400\" stop=\"20261031090000 -0400\"><title>News ñ</title><desc>One &amp; two</desc></programme></tv>"); await library.ImportGuide(guide, CancellationToken.None); var programme = (await library.Snapshot()).Guide.Single(); Check(programme.StartUtc == new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero) && programme.DurationSeconds == 3600 && programme.Description == "One & two", "XMLTV offsets, entities and duration.");
                    await File.WriteAllTextAsync(guide, "<tv><programme channel=\"news\" start=\"20261031080000 +0060\" stop=\"20261031090000 +0060\"><title>Invalid</title></programme></tv>"); bool rejected = false; try { await library.ImportGuide(guide, CancellationToken.None); } catch (Exception ex) when (ex is ArgumentException or InvalidDataException or FormatException) { rejected = true; } Check(rejected && (await library.Snapshot()).Guide.Single().Title == "News ñ", "Malformed offset is rejected without replacing saved guide."); break;
                case 281:
                    string m3u = P("Channels.m3u8"); await File.WriteAllTextAsync(m3u, $"#EXTM3U\n#EXTINF:-1 tvg-id=\"news\" group-title=\"Group, one\",News, HD ñ\n{source}\n"); await library.ImportM3u(m3u, CancellationToken.None); var imported = (await library.Snapshot()).Channels.Single(); Check(imported.Name == "News, HD ñ" && imported.Group == "Group, one" && imported.GuideId == "news" && imported.Id == channel.Id, "M3U imports commas in titles and quoted attributes while retaining existing IDs."); await library.ImportM3u(m3u, CancellationToken.None); Check((await library.Snapshot()).Channels.Length == 1, "Repeated import updates rather than duplicates."); break;
                case 282:
                    string recording = P("Recorded.mkv"); await CaptureService.SaveStream(source, recording, 2, CancellationToken.None); var row = new TvRecording(Guid.NewGuid().ToString("N"), channel.Id, "Recorded ñ", recording, DateTimeOffset.UtcNow, 2); await library.AddRecording(row, CancellationToken.None); Check((await new TelevisionLibrary(folder).Snapshot()).Recordings.Single() == row, "Recording catalog persists across reopen."); await Plays(recording); break;
                case 283:
                    var now = DateTimeOffset.UtcNow; var scheduled = new TvRecordingTask(Guid.NewGuid().ToString("N"), channel.Id, now.AddSeconds(-1), 2, P("Scheduled.mkv")); var missed = scheduled with { Id = Guid.NewGuid().ToString("N"), DueUtc = now.AddMinutes(-3), Destination = P("Missed.mkv") }; var canceled = scheduled with { Id = Guid.NewGuid().ToString("N"), DueUtc = now.AddHours(1), Destination = P("Canceled.mkv") };
                    await library.Schedule(scheduled, CancellationToken.None); await library.Schedule(missed, CancellationToken.None); await library.Schedule(canceled, CancellationToken.None); await library.CancelArmed(canceled.Id, CancellationToken.None);
                    await using (var recorder = new TelevisionRecorder(library)) { Check(!recorder.Armed && !File.Exists(scheduled.Destination), "Schedules do not record until explicitly armed."); recorder.Arm(); await Until(async () => (await library.Snapshot()).Tasks.Single(t => t.Id == scheduled.Id).State is "Completed" or "Failed", "Scheduler finishes its finite job."); await recorder.Disarm(); }
                    var data = await library.Snapshot(); Check(data.Tasks.Single(t => t.Id == scheduled.Id).State == "Completed" && data.Tasks.Single(t => t.Id == missed.Id).State == "Missed" && data.Tasks.Single(t => t.Id == canceled.Id).State == "Canceled" && data.Recordings.Single().Path == scheduled.Destination, "Actual scheduler records once, persists completion and skips canceled/late tasks."); break;
                case 284:
                    await using (var buffer = new TelevisionBuffer(folder)) { buffer.Start(source); await Until(() => Task.FromResult(buffer.Status == "Stream ended"), "Finite fixture completes segmentation: " + buffer.Status); var segments = buffer.CompletedSegments(); Check(segments.Length == 3 && segments.All(p => new FileInfo(p).Length > 0), "All three finalized segments, including the final short tail, are available."); await Plays(segments[0]); await buffer.Stop(); Check(buffer.CompletedSegments().Length == 3, "Stopping a completed stream retains finalized tail."); } break;
                case 285:
                    string copy = P("Finite.mkv"); await CaptureService.SaveStream(source, copy, 2, CancellationToken.None); Check(new FileInfo(copy).Length > 1000, "Finite HTTP stream copy creates real media."); await Plays(copy); byte[] saved = await File.ReadAllBytesAsync(copy); bool conflict = false; try { await CaptureService.SaveStream(source, copy, 2, CancellationToken.None); } catch (IOException) { conflict = true; } Check(conflict && saved.SequenceEqual(await File.ReadAllBytesAsync(copy)), "Recording preserves existing destinations."); break;
            }
        });
    }
}
