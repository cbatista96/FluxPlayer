using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public static class MediaArtifacts
{
    public static Task ContactSheet(string source, string destination, long durationMs, int columns, int rows, int width, CancellationToken token)
    {
        source = FfmpegJob.Source(source); if (durationMs is <= 0 or > 315_360_000_000 || columns is < 1 or > 8 || rows is < 1 or > 8 || columns * rows > 48 || width is < 120 or > 480) throw new ArgumentException("Use a known duration, 1–48 thumbnails, up to 8 columns/rows and width 120–480.");
        int count = columns * rows; double interval = durationMs / 1000d / count;
        int height = ((width * 9 + 15) / 16 + 1) / 2 * 2;
        string filter = FormattableString.Invariant($"fps=1/{interval:0.########},scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=bilinear,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=0x181a20,setsar=1,tile={columns}x{rows}:nb_frames={count}:padding=4:margin=4:color=0x181a20");
        return FfmpegJob.Copy(new[] { "-loglevel", "error", "-i", source, "-an", "-vf", filter, "-frames:v", "1", "-update", "1" }, destination, ".png", durationMs, null, token);
    }
    public static Task Ringtone(string source, string destination, long startMs, long lengthMs, bool fade, CancellationToken token)
    {
        source = FfmpegJob.Source(source); if (startMs < 0 || lengthMs is < 1000 or > 120000 || startMs > 315_360_000_000 - lengthMs) throw new ArgumentException("Choose a 1–120 second ringtone interval.");
        string ext = Path.GetExtension(destination).ToLowerInvariant(); if (ext is not (".m4a" or ".mp3" or ".wav")) throw new ArgumentException("Choose M4A, MP3 or WAV.");
        var args = new List<string> { "-loglevel", "error", "-ss", (startMs / 1000d).ToString(CultureInfo.InvariantCulture), "-i", source, "-t", (lengthMs / 1000d).ToString(CultureInfo.InvariantCulture), "-map", "0:a:0", "-vn", "-ac", "2", "-ar", "44100", "-c:a", ext == ".mp3" ? "libmp3lame" : ext == ".wav" ? "pcm_s16le" : "aac" };
        if (ext != ".wav") args.AddRange(new[] { "-b:a", "192k" }); if (fade) args.AddRange(new[] { "-af", FormattableString.Invariant($"afade=t=in:d=0.15,afade=t=out:st={(lengthMs / 1000d - .25):0.###}:d=0.25") });
        return FfmpegJob.Copy(args, destination, ext, lengthMs, null, token);
    }
}
