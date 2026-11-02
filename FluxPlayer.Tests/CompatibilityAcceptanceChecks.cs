using FluxPlayer.Services;
using System.IO;
using System.Text.Json;

internal static class CompatibilityAcceptanceChecks
{
    public static async Task Native(string root)
    {
        string folder = Path.Combine(root, "compatibility-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var fixtures = new List<string>();
        foreach (var (extension, encoder) in new[] { ("wav", "pcm_s16le"), ("flac", "flac"), ("mp3", "libmp3lame"), ("m4a", "aac"), ("opus", "libopus"), ("ogg", "libvorbis") })
        {
            string file = Path.Combine(folder, "audio." + extension);
            await FfmpegJob.Run(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "1.2", "-c:a", encoder, file], CancellationToken.None); fixtures.Add(file);
        }
        foreach (var (name, encoder) in new[] { ("h264", "libx264"), ("hevc", "libx265"), ("vp9", "libvpx-vp9"), ("av1", "libaom-av1"), ("mpeg2", "mpeg2video") })
        {
            string file = Path.Combine(folder, name + ".mkv"); var args = new List<string> { "-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=1.2", "-c:v", encoder };
            if (name is "h264" or "hevc") args.AddRange(["-preset", "ultrafast"]); if (name == "av1") args.AddRange(["-cpu-used", "8"]);
            args.Add(file); await FfmpegJob.Run(args, CancellationToken.None); fixtures.Add(file);
        }
        string invalid = Path.Combine(folder, "invalid.mkv"); await File.WriteAllTextAsync(invalid, "Not a media file"); fixtures.Add(invalid);
        var report = await PlaybackCompatibility.Run(fixtures.ToArray(), ["none", "any", "d3d11va", "dxva2"], null, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "playback-compatibility.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (report.Results.Length != fixtures.Count * 4 || report.Results.Where(r => r.Source != invalid).Any(r => !r.Decoded) || report.Results.Where(r => r.Source == invalid).Any(r => r.Decoded))
            throw new InvalidOperationException("Native compatibility matrix differs from fixture expectations: " + string.Join("; ", report.Results.Where(r => !r.Decoded && r.Source != invalid).Select(r => Path.GetFileName(r.Source) + "/" + r.Decoder + ": " + r.Detail)));
        if (report.Results.Any(r => r.Decoder == "none" && r.HardwareSurfaceObserved)) throw new InvalidOperationException("Software policy was incorrectly labeled as a hardware surface.");
        await File.WriteAllTextAsync(Path.Combine(root, "playback-compatibility.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); bool rejected = false; try { await PlaybackCompatibility.Run([fixtures[0]], ["none"], null, canceled.Token); } catch (OperationCanceledException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Canceled compatibility playback unexpectedly proceeded.");
    }
}
