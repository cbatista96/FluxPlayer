using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record DiagnosticKind(string Id, string Name, bool Audio, string Filter, string Extension, string Description)
{ public override string ToString() => Name; }
public sealed record DiagnosticLine(int Number, string Detail);
public sealed record DiagnosticReport(string Kind, string Source, double StartSeconds, double DurationSeconds, DateTimeOffset CreatedUtc, string Scope, ImmutableArray<DiagnosticLine> Lines, string DiagnosticTail);

/// <summary>Explicit bounded-excerpt analysis and visualizations; no idle polling or playback hooks.</summary>
public static class MediaDiagnostics
{
    public static ImmutableArray<DiagnosticKind> Kinds { get; } = [
        new("silence", "Silence interval detection", true, "silencedetect=n=-45dB:d=0.5", ".json", "Find intervals below −45 dBFS for at least 0.5 seconds."),
        new("channelstats", "Per-channel signal statistics", true, "astats=metadata=0:reset=0", ".json", "DC offset, min/max, RMS, crest factor, crossings and reported bit depth; decoded signal, not container bit depth."),
        new("spectralstats", "Spectral feature timeline", true, "aspectralstats=win_size=2048:overlap=0.5,ametadata=mode=print", ".json", "Per-frame centroid, spread, flatness and other spectral metadata in a bounded diagnostic tail."),
        new("phase", "Stereo phase-correlation timeline", true, "aformat=channel_layouts=stereo,aphasemeter=video=0,ametadata=mode=print", ".json", "Stereo phase metadata after stereo negotiation; not proof of physical speaker phase."),
        new("dr", "Excerpt dynamic-range meter", true, "drmeter=length=3", ".json", "FFmpeg DR estimate for the chosen excerpt; not EBU LRA or a certified whole-album rating."),
        new("spectrogram", "Log-frequency spectrogram image", true, "showspectrumpic=s=1280x720:legend=1:scale=log:fscale=log", ".png", "One bounded excerpt spectrogram PNG."),
        new("waveform", "Channel-separated waveform image", true, "showwavespic=s=1280x720:split_channels=1:colors=0x56CCF2|0xBB86FC", ".png", "One waveform PNG; height is shared between decoded channels."),
        new("frequencybars", "Animated frequency bars", true, "aformat=channel_layouts=stereo,showfreqs=s=1280x720:mode=bar:fscale=log:ascale=log:rate=25", ".mp4", "H.264 frequency-bar visualization of up to 30 seconds, without embedded audio."),
        new("constantq", "Animated constant-Q spectrum", true, "aformat=channel_layouts=stereo,showcqt=s=1280x720:fps=25", ".mp4", "Constant-Q frequency visualization, up to 30 seconds; requires FFmpeg showcqt support."),
        new("vectorscope", "Animated stereo vectorscope", true, "aformat=channel_layouts=stereo,avectorscope=s=720x720:r=25:draw=line", ".mp4", "Stereo phase-space visualization without embedded audio, up to 30 seconds."),
        new("black", "Black interval detection", false, "blackdetect=d=0.25:pix_th=0.1", ".json", "Black intervals after bounded 960×540 downscaling."),
        new("freeze", "Frozen-frame interval detection", false, "freezedetect=n=-50dB:d=2", ".json", "Candidate frozen intervals of at least two seconds on downscaled frames."),
        new("fields", "Interlace and field-order analysis", false, "idet", ".json", "IDET field-order summary on decoded source frames before scaling; heuristic only."),
        new("signal", "Video signal-statistics timeline", false, "signalstats=stat=tout+vrep+brng,metadata=mode=print", ".json", "Luma/chroma, temporal outlier, vertical repetition and broadcast-range metadata after downscaling."),
        new("entropy", "Frame entropy timeline", false, "entropy=mode=normal,metadata=mode=print", ".json", "Frame-plane entropy metadata on downscaled video; not a perceptual quality score."),
        new("blur", "Blur candidate scoring", false, "blurdetect=block_width=32:block_height=32:block_pct=80,metadata=mode=print", ".json", "Blur metadata after downscaling; thresholds depend on content."),
        new("block", "Block artifact candidate scoring", false, "blockdetect=period_min=8:period_max=16,metadata=mode=print", ".json", "Blocking metadata after downscaling; not a codec conformance check."),
        new("scenes", "Scene-cut candidate detection", false, "scdet=threshold=10,metadata=mode=print", ".json", "Scene scores and cut candidates at threshold 10 on downscaled frames."),
        new("crop", "Active-picture crop suggestions", false, "cropdetect=limit=24:round=2:reset=100", ".json", "Crop candidates in downscaled coordinates; no automatic crop application."),
        new("framehash", "Decoded frame SHA-256 manifest", false, "", ".txt", "Framehash manifest for decoded source video frames in the selected excerpt; not a source-file hash.")
    ];
    public static DiagnosticKind Find(string id) => Kinds.FirstOrDefault(k => k.Id == id) ?? throw new ArgumentException("Unknown diagnostic.");
    public static Task<DiagnosticReport> Analyze(string id, string source, double start, double seconds, CancellationToken token) => Task.Run(async () =>
    {
        var kind = Find(id); if (kind.Extension != ".json") throw new ArgumentException("This diagnostic exports a visual or frame manifest instead.");
        ProcessingPalette.Excerpt(start, seconds, 60); var version = SourceVersion.Capture(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(10)); token = deadline.Token;
        var args = Input(version.Path, start, seconds);
        string graph = kind.Audio ? "aresample=48000,aformat=sample_fmts=fltp," + kind.Filter : kind.Id == "fields" ? kind.Filter : VideoScale + "," + kind.Filter;
        args.AddRange(["-map", kind.Audio ? "0:a:0" : "0:v:0", kind.Audio ? "-af" : "-vf", graph]);
        if (!kind.Audio) args.Add("-an");
        args.AddRange([kind.Audio ? "-vn" : "-sn", "-f", "null", "-"]);
        string log = await FfmpegJob.Run(args, token).ConfigureAwait(false); version.Verify();
        string[] lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new DiagnosticReport(id, version.Path, start, seconds, DateTimeOffset.UtcNow,
            "Excerpt-relative timestamps. Diagnostic tail capped at 128 KiB / last 1,000 nonempty lines; events may be omitted. " + kind.Description,
            lines.TakeLast(1000).Select((line, index) => new DiagnosticLine(index + 1, line)).ToImmutableArray(), log);
    }, token);
    public static Task ExportReport(DiagnosticReport report, string destination, CancellationToken token)
    {
        if (report.Lines.Length > 1000 || report.DiagnosticTail.Length > 256 * 1024) throw new ArgumentException("The report exceeds its bound.");
        return OfflineMediaJob.Publish(destination, ".json", [], staging => File.WriteAllTextAsync(staging, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), token), token);
    }
    public static Task ExportVisual(string id, string source, string destination, double start, double seconds, CancellationToken token) => Task.Run(async () =>
    {
        var kind = Find(id); if (kind.Extension == ".json") throw new ArgumentException("Use Analyze for a diagnostic report.");
        ProcessingPalette.Excerpt(start, seconds, kind.Extension == ".mp4" ? 30 : 60); var version = SourceVersion.Capture(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(15)); token = deadline.Token;
        var args = Input(version.Path, start, seconds);
        if (kind.Id == "framehash") args.AddRange(["-map", "0:v:0", "-an", "-f", "framehash", "-hash", "sha256"]);
        else
        {
            args.AddRange(["-filter_complex", "[0:a:0]aresample=48000," + kind.Filter + "[out]", "-map", "[out]", "-an", "-sn"]);
            if (kind.Extension == ".png") args.AddRange(["-frames:v", "1", "-c:v", "png", "-update", "1"]);
            else args.AddRange(["-c:v", "libx264", "-preset", "fast", "-crf", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"]);
        }
        await OfflineMediaJob.Publish(destination, kind.Extension, [version], staging => FfmpegJob.Copy(args, staging, kind.Extension, 0, null, token), token).ConfigureAwait(false);
    }, token);
    private const string VideoScale = "scale=w='min(960,iw)':h='min(540,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2";
    private static List<string> Input(string path, double start, double seconds) => ["-loglevel", "info", "-ss", ProcessingPalette.N(start), "-t", ProcessingPalette.N(seconds), "-i", path, "-map_metadata", "-1", "-dn"];
}
