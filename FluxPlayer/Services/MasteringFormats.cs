using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public sealed record MasteringFormat(string Name, string Extension, bool Video, string Description, ImmutableArray<string> Arguments)
{
    public override string ToString() => Name;
}

/// <summary>Allowlisted offline encoders. Availability is resolved only by an explicit export, never at startup.</summary>
public static class MasteringFormats
{
    public static ImmutableArray<MasteringFormat> Profiles { get; } =
    [
        new("ProRes 422 HQ", ".mov", true, "10-bit 4:2:2 MOV with 24-bit PCM; up to 1080p. Requires prores_ks.", ["-c:v", "prores_ks", "-profile:v", "3", "-pix_fmt", "yuv422p10le", "-c:a", "pcm_s24le", "-ar", "48000"]),
        new("HEVC / AAC", ".mp4", true, "8-bit SDR HEVC with AAC. Requires libx265; two worker threads requested.", ["-c:v", "libx265", "-preset", "fast", "-crf", "24", "-x265-params", "pools=2:frame-threads=1", "-tag:v", "hvc1", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
        new("AV1 / Opus", ".mkv", true, "8-bit SDR AV1 / Opus Matroska. Requires libaom-av1 and libopus; CPU encoding can be slow.", ["-c:v", "libaom-av1", "-cpu-used", "8", "-crf", "34", "-b:v", "0", "-row-mt", "1", "-pix_fmt", "yuv420p", "-c:a", "libopus", "-b:a", "160k"]),
        new("MPEG-2 / MP2", ".mpg", true, "25 fps MPEG-2 program stream / MP2 audio. File export only, not DVD authoring.", ["-c:v", "mpeg2video", "-q:v", "3", "-r", "25", "-pix_fmt", "yuv420p", "-c:a", "mp2", "-b:a", "256k", "-ar", "48000", "-f", "mpeg"]),
        new("MXF / DNxHR HQ", ".mxf", true, "1920 × 1080, 25 fps DNxHR HQ / 48 kHz PCM in MXF. Requires dnxhd encoder and MXF muxer.", ["-c:v", "dnxhd", "-profile:v", "dnxhr_hq", "-pix_fmt", "yuv422p", "-r", "25", "-c:a", "pcm_s24le", "-ar", "48000", "-f", "mxf"]),
        new("AC-3 stereo", ".ac3", false, "48 kHz stereo AC-3 at 384 kb/s. This does not create Atmos, TrueHD or a licensed disc.", ["-c:a", "ac3", "-b:a", "384k", "-ar", "48000", "-f", "ac3"]),
        new("E-AC-3 stereo", ".eac3", false, "48 kHz stereo E-AC-3 at 384 kb/s. No object-audio metadata.", ["-c:a", "eac3", "-b:a", "384k", "-ar", "48000", "-f", "eac3"]),
        new("RF64 / 24-bit PCM", ".wav", false, "Always-RF64 48 kHz stereo PCM; intended for RF64-aware applications.", ["-c:a", "pcm_s24le", "-ar", "48000", "-rf64", "always", "-f", "wav"]),
        new("AIFF / 24-bit PCM", ".aiff", false, "48 kHz stereo big-endian 24-bit PCM in AIFF.", ["-c:a", "pcm_s24be", "-ar", "48000", "-f", "aiff"]),
        new("DXD-rate / 24-bit PCM", ".wav", false, "352.8 kHz stereo 24-bit PCM, RF64-auto. Resampling does not recover missing source detail.", ["-c:a", "pcm_s24le", "-ar", "352800", "-rf64", "auto", "-f", "wav"])
    ];
    public static Task Export(string source, string destination, string profileName, int audioStream, int limitSeconds, CancellationToken token) => Task.Run(async () =>
    {
        var profile = Profiles.FirstOrDefault(p => p.Name == profileName) ?? throw new ArgumentException("Choose an available export profile.");
        if (audioStream is < 0 or > 63 || limitSeconds is < 0 or > 7200) throw new ArgumentException("Choose audio stream 0–63 and duration 0–7,200 seconds (0 means full source).");
        var version = SourceVersion.Capture(source); var args = new List<string> { "-loglevel", "error", "-i", version.Path };
        if (limitSeconds > 0) args.AddRange(["-t", limitSeconds.ToString(CultureInfo.InvariantCulture)]);
        if (profile.Video)
        {
            string sizing = "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2";
            if (profile.Name == "MXF / DNxHR HQ") sizing += ",pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black";
            args.AddRange(["-map", "0:v:0", "-map", $"0:a:{audioStream}?", "-vf", sizing + ",setsar=1"]);
        }
        else args.AddRange(["-map", $"0:a:{audioStream}", "-vn"]);
        args.AddRange(["-sn", "-dn", "-map_metadata", "-1", "-ac", "2"]); args.AddRange(profile.Arguments); args.AddRange(["-threads", "2"]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2));
        await OfflineMediaJob.Publish(destination, profile.Extension, [version], staging => FfmpegJob.Copy(args, staging, profile.Extension, 0, null, deadline.Token), deadline.Token).ConfigureAwait(false);
    }, token);
}
