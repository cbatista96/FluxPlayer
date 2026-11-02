using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public sealed record VideoExportProfile(string Name, string Extension, string VideoEncoder, string AudioEncoder, int MaximumWidth, int MaximumHeight, bool Mobile = false)
{ public override string ToString() => Name; }
public static class MediaProduction
{
    public static IReadOnlyList<VideoExportProfile> VideoProfiles { get; } = new[]
    {
        new VideoExportProfile("MP4 · H.264 / AAC", ".mp4", "libx264", "aac", 0, 0), new("WebM · VP9 / Opus", ".webm", "libvpx-vp9", "libopus", 0, 0),
        new("Mobile · 720p H.264 / AAC", ".mp4", "libx264", "aac", 1280, 720, true), new("Mobile · 1080p H.264 / AAC", ".mp4", "libx264", "aac", 1920, 1080, true)
    };
    public static Task ExportVideo(string source, string destination, VideoExportProfile profile, long durationMs, IProgress<double>? progress, CancellationToken token)
    {
        if (!VideoProfiles.Contains(profile)) throw new ArgumentException("Choose a supported video profile."); source = FfmpegJob.Source(source);
        var args = new List<string> { "-loglevel", "error", "-i", source, "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "0", "-sn", "-c:v", profile.VideoEncoder, "-pix_fmt", "yuv420p", "-c:a", profile.AudioEncoder };
        if (profile.VideoEncoder == "libx264") args.AddRange(new[] { "-preset", "fast", "-crf", profile.Mobile ? "24" : "21" }); else args.AddRange(new[] { "-crf", "32", "-b:v", "0", "-row-mt", "1" });
        if (profile.MaximumWidth > 0) args.AddRange(new[] { "-vf", FormattableString.Invariant($"scale=w='min({profile.MaximumWidth},iw)':h='min({profile.MaximumHeight},ih)':force_original_aspect_ratio=decrease:force_divisible_by=2"), "-ac", "2", "-ar", "48000", "-b:a", "128k" });
        else args.AddRange(new[] { "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-b:a", "192k" });
        if (profile.Extension == ".mp4") args.AddRange(new[] { "-movflags", "+faststart" });
        return FfmpegJob.Copy(args, destination, profile.Extension, durationMs, progress, token);
    }
    public static Task JoinAudio(IEnumerable<string> sources, string destination, long durationMs, IProgress<double>? progress, CancellationToken token)
    {
        var files = sources.Take(33).Select(FfmpegJob.Source).ToArray(); if (files.Length is < 2 or > 32) throw new ArgumentException("Join 2–32 ordered local audio sources.");
        string extension = Path.GetExtension(destination).ToLowerInvariant(); if (extension is not (".wav" or ".flac")) throw new ArgumentException("Choose WAV or FLAC for the joined copy.");
        var args = new List<string> { "-loglevel", "error" }; foreach (string source in files) args.AddRange(new[] { "-i", source });
        string graph = string.Join(';', files.Select((_, index) => FormattableString.Invariant($"[{index}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a{index}]"))) + ";" + string.Concat(files.Select((_, index) => "[a" + index.ToString(CultureInfo.InvariantCulture) + "]")) + "concat=n=" + files.Length.ToString(CultureInfo.InvariantCulture) + ":v=0:a=1[out]";
        args.AddRange(new[] { "-filter_complex", graph, "-map", "[out]", "-map_metadata", "0", "-c:a", extension == ".wav" ? "pcm_s24le" : "flac" });
        return FfmpegJob.Copy(args, destination, extension, durationMs, progress, token);
    }
}
