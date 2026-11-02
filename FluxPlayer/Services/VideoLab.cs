using FluxPlayer.Models;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public static class VideoLab
{
    public static readonly string[] Kernels = ["Bilinear", "Bicubic", "Lanczos", "Spline"];
    public static readonly string[] ToneMaps = ["None", "Reinhard", "Hable"];
    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static void Validate(VideoLabOptions o)
    {
        if (o.Width is < 64 or > 3840 || o.Height is < 64 or > 2160 || o.Width % 2 != 0 || o.Height % 2 != 0 || !Kernels.Contains(o.Kernel) || !ToneMaps.Contains(o.ToneMap) || !double.IsFinite(o.Deband) || o.Deband is < 0 or > .1 || !double.IsFinite(o.Denoise) || o.Denoise is < 0 or > 10 || o.InterpolateFps is not (0 or 24 or 25 or 30 or 50 or 60) || o.HdrTransfer is not ("PQ" or "HLG") || !double.IsFinite(o.TargetNits) || o.TargetNits is < 50 or > 400 || !double.IsFinite(o.SourcePeakNits) || o.SourcePeakNits < o.TargetNits || o.SourcePeakNits > 10000 || o.InputRange is not ("Auto" or "Limited" or "Full") || o.OutputRange is not ("Limited" or "Full") || o.OverlayText == null || o.OverlayText.Length > 500 || o.OverlayX is < 0 or > 3840 || o.OverlayY is < 0 or > 2160 || o.OverlayWidth is < 64 or > 1024)
            throw new ArgumentException("Check output bounds, finite processing values, HDR assumptions and overlay limits.");
        if (o.OverlayImage != "" && !PhotoLibrary.Extensions.Contains(Path.GetExtension(o.OverlayImage), StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a supported local image for the logo.");
    }
    public static string Graph(VideoLabOptions o, bool textImage)
    {
        Validate(o); var filters = new List<string>();
        string outputRange = o.OutputRange.ToLowerInvariant();
        // Reduce frame dimensions before float-HDR, restoration and interpolation allocations.
        string width = o.ReduceOnly ? "min(" + o.Width + ",iw)" : o.Width.ToString(CultureInfo.InvariantCulture);
        string height = o.ReduceOnly ? "min(" + o.Height + ",ih)" : o.Height.ToString(CultureInfo.InvariantCulture);
        string range = o.InputRange == "Auto" ? "auto" : o.InputRange.ToLowerInvariant();
        filters.Add("scale=w='" + width + "':h='" + height + "':force_original_aspect_ratio=decrease:force_divisible_by=2:flags=" + o.Kernel.ToLowerInvariant() + ":in_range=" + range + ":out_range=" + outputRange);
        if (o.ToneMap != "None")
        {
            // The linear float stage is RGB. Leaving the YUV matrix inherited
            // here gives zimg no valid conversion path for untagged sources.
            filters.Add("zscale=tin=" + (o.HdrTransfer == "PQ" ? "smpte2084" : "arib-std-b67") + ":pin=2020:min=2020_ncl:rin=" + outputRange + ":t=linear:p=2020:m=gbr:r=full:npl=" + N(o.TargetNits));
            filters.Add("format=gbrpf32le"); filters.Add("zscale=p=709");
            filters.Add("tonemap=tonemap=" + o.ToneMap.ToLowerInvariant() + ":desat=2:peak=" + N(o.SourcePeakNits / o.TargetNits));
            filters.Add("zscale=t=709:m=709:r=" + outputRange); filters.Add("format=yuv420p");
            filters.Add("sidedata=mode=delete:type=MASTERING_DISPLAY_METADATA"); filters.Add("sidedata=mode=delete:type=CONTENT_LIGHT_LEVEL");
        }
        if (o.Stabilize) filters.Add("deshake=rx=16:ry=16:edge=mirror");
        if (o.Denoise > 0) filters.Add("hqdn3d=" + N(o.Denoise) + ":" + N(o.Denoise * .75) + ":" + N(o.Denoise * 1.5) + ":" + N(o.Denoise));
        if (o.Deband > 0) filters.Add("deband=1thr=" + N(o.Deband) + ":2thr=" + N(o.Deband) + ":3thr=" + N(o.Deband) + ":range=16");
        if (o.InterpolateFps != 0) filters.Add("minterpolate=fps=" + o.InterpolateFps + ":mi_mode=mci:mc_mode=obmc:me_mode=bilat:vsbmc=0");
        var graph = new List<string> { "[0:v:0]" + string.Join(',', filters) + "[base]" };
        string current = "base"; int input = 1;
        void Overlay(string label, int x, int y)
        {
            string sizing = label == "logo" ? ":h=" + o.OverlayWidth + ":force_original_aspect_ratio=decrease" : ":h=-1";
            graph.Add("[" + input.ToString(CultureInfo.InvariantCulture) + ":v:0]scale=w=" + o.OverlayWidth + sizing + ":flags=bilinear,format=rgba[" + label + "]");
            string next = label + "out"; graph.Add("[" + current + "][" + label + "]overlay=x=" + x + ":y=" + y + ":eof_action=repeat:repeatlast=1:shortest=0[" + next + "]");
            current = next; input++;
        }
        if (o.OverlayImage != "") Overlay("logo", o.OverlayX, o.OverlayY);
        if (textImage) Overlay("caption", o.OverlayX, o.OverlayY + (o.OverlayImage == "" ? 0 : o.OverlayWidth + 12));
        graph.Add("[" + current + "]format=yuv420p[out]"); return string.Join(';', graph);
    }
    public static Task Export(string source, string destination, VideoLabOptions options, string renderedText, IProgress<string>? progress, CancellationToken token) => Task.Run(async () =>
    {
        Validate(options); if (options.OverlayText != "" && renderedText == "") throw new ArgumentException("Render the caption before starting the job.");
        var versions = new List<SourceVersion> { SourceVersion.Capture(source) };
        if (options.OverlayImage != "") { var logo = SourceVersion.Capture(options.OverlayImage); if (logo.Bytes > 20 * 1024 * 1024) throw new IOException("The logo must be at most 20 MiB."); versions.Add(logo); }
        if (renderedText != "") versions.Add(SourceVersion.Capture(renderedText));
        var args = new List<string> { "-loglevel", "error", "-i", versions[0].Path };
        foreach (var extra in versions.Skip(1)) args.AddRange(["-i", extra.Path]);
        args.AddRange(["-filter_complex", Graph(options, renderedText != ""), "-map", "[out]", "-map", "0:a:0?", "-sn", "-dn", "-map_metadata", "-1", "-c:v", "libx264", "-preset", "fast", "-crf", "21", "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", "-color_range", options.OutputRange == "Full" ? "pc" : "tv", "-movflags", "+faststart"]);
        if (options.ToneMap != "None") args.AddRange(["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
        // x264 can omit a limited-range VUI when all color descriptions are
        // unspecified. Publish the selected range explicitly in the bitstream.
        args.AddRange(["-bsf:v", "h264_metadata=video_full_range_flag=" + (options.OutputRange == "Full" ? "1" : "0")]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
        progress?.Report("Rendering a new H.264/AAC copy. Heavy filters can take longer than source playback…");
        await OfflineMediaJob.Publish(destination, ".mp4", versions, staging => FfmpegJob.Copy(args, staging, ".mp4", 0, null, token), token).ConfigureAwait(false);
    }, token);
}
