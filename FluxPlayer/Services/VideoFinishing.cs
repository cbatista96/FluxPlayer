using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

/// <summary>SDR-oriented offline finishing with explicit color assumptions and bounded excerpt analysis.</summary>
public static partial class VideoFinishing
{
    public static readonly string[] Deinterlacers = ["None", "Yadif", "Bwdif", "W3fdif"];
    public static readonly string[] StereoInputs = ["None", "Side by side", "Half-width side by side", "Above / below", "Half-height above / below"];
    public static readonly string[] StereoOutputs = ["Left eye", "Right eye", "Red/cyan Dubois", "Side by side", "Above / below"];
    private static string N(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
    public static void Validate(VideoFinishingOptions o)
    {
        if (!Deinterlacers.Contains(o.Deinterlace) || o.FieldOrder is not ("Auto" or "Top first" or "Bottom first")
            || !StereoInputs.Contains(o.StereoInput) || !StereoOutputs.Contains(o.StereoOutput)
            || new[] { o.CropLeft, o.CropRight, o.CropTop, o.CropBottom }.Any(v => v is < 0 or > 20)
            || o.Width is < 128 or > 3840 || o.Height is < 128 or > 2160 || o.Width % 2 != 0 || o.Height % 2 != 0
            || o.X < 0 || o.Y < 0 || o.X % 2 != 0 || o.Y % 2 != 0 || o.X > o.Width - 64 || o.Y > o.Height - 64
            || o.SarNumerator is < 1 or > 100 || o.SarDenominator is < 1 or > 100
            || o.RgbMatrix == null || o.RgbMatrix.Length != 9 || o.RgbMatrix.Any(v => !double.IsFinite(v) || Math.Abs(v) > 2)
            || o.LutInterpolation is not ("Trilinear" or "Tetrahedral")) throw new ArgumentException("Check finite color coefficients, stereo format, even canvas/offset values and crop percentages.");
    }
    public static string Graph(VideoFinishingOptions o)
    {
        Validate(o); var filters = new List<string>();
        string parity = o.FieldOrder switch { "Top first" => "tff", "Bottom first" => "bff", _ => "auto" };
        if (o.Deinterlace == "W3fdif") filters.Add("w3fdif=filter=complex:mode=frame:parity=" + parity + ":deint=all");
        else if (o.Deinterlace != "None") filters.Add(o.Deinterlace.ToLowerInvariant() + "=mode=send_frame:parity=" + parity + ":deint=all");
        if (o.StereoInput != "None")
        {
            string input = o.StereoInput switch { "Side by side" => "sbsl", "Half-width side by side" => "sbs2l", "Above / below" => "abl", _ => "ab2l" };
            string output = o.StereoOutput switch { "Left eye" => "ml", "Right eye" => "mr", "Red/cyan Dubois" => "arcd", "Side by side" => "sbsl", _ => "abl" };
            filters.Add("stereo3d=in=" + input + ":out=" + output);
        }
        // Bound raster dimensions before the RGB/LUT stages. Crop percentages are applied to this normalized raster.
        filters.Add("scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2");
        filters.Add($"crop=w='trunc(iw*{100 - o.CropLeft - o.CropRight}/200)*2':h='trunc(ih*{100 - o.CropTop - o.CropBottom}/200)*2':x='trunc(iw*{o.CropLeft}/200)*2':y='trunc(ih*{o.CropTop}/200)*2'");
        if (o.Bt2020SdrTo709) filters.Add("colorspace=ispace=bt2020nc:iprimaries=bt2020:itrc=bt709:irange=tv:space=bt709:primaries=bt709:trc=bt709:range=tv");
        filters.Add("format=gbrp");
        string[] keys = ["rr", "rg", "rb", "gr", "gg", "gb", "br", "bg", "bb"];
        filters.Add("colorchannelmixer=" + string.Join(':', keys.Select((key, i) => key + "=" + N(o.RgbMatrix[i]))));
        if (o.Lut != "") filters.Add("lut3d=file=grade.cube:interp=" + o.LutInterpolation.ToLowerInvariant());
        filters.Add($"scale=w={o.Width - o.X}:h={o.Height - o.Y}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos");
        filters.Add($"pad=w={o.Width}:h={o.Height}:x={o.X}:y={o.Y}:color=black");
        filters.Add($"setsar=sar={o.SarNumerator}/{o.SarDenominator}:max=100");
        filters.Add("format=yuv420p");
        if (o.Legalize) filters.Add("lutyuv=y='clip(val,16,235)':u='clip(val,16,240)':v='clip(val,16,240)'");
        return string.Join(',', filters);
    }
    private static async Task CopyCube(string path, string destination, CancellationToken token)
    {
        if (!Path.GetExtension(path).Equals(".cube", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 16 * 1024 * 1024) throw new ArgumentException("Choose a .cube 3D LUT no larger than 16 MiB.");
        var text = await File.ReadAllTextAsync(path, token).ConfigureAwait(false); int size = 0, rows = 0;
        foreach (string line in text.Split('\n'))
        {
            token.ThrowIfCancellationRequested(); string content = line.Split('#', 2)[0].Trim(); if (content == "") continue;
            var fields = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields[0] == "TITLE") continue;
            if (fields[0] == "LUT_3D_SIZE") { if (size != 0 || fields.Length != 2 || !int.TryParse(fields[1], out size) || size is < 2 or > 65) throw new InvalidDataException("Use one 2–65-point 3D LUT grid."); continue; }
            bool domain = fields[0] is "DOMAIN_MIN" or "DOMAIN_MAX";
            if (fields.Length != (domain ? 4 : 3) || fields.Skip(domain ? 1 : 0).Any(f => !double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || Math.Abs(value) > 16)) throw new InvalidDataException("The LUT contains unsupported directives or invalid coefficients.");
            if (!domain) rows++;
        }
        if (size == 0 || rows != size * size * size) throw new InvalidDataException("The LUT grid is incomplete.");
        await File.WriteAllTextAsync(destination, text, token).ConfigureAwait(false);
    }
    public static Task Export(string source, string destination, VideoFinishingOptions options, CancellationToken token) => Task.Run(async () =>
    {
        Validate(options); options = options with { RgbMatrix = options.RgbMatrix.ToArray() }; string graph = Graph(options);
        var versions = new List<SourceVersion> { SourceVersion.Capture(source) }; string? work = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
        try
        {
            if (options.Lut != "")
            {
                var version = SourceVersion.Capture(options.Lut); versions.Add(version);
                work = Path.Combine(Path.GetTempPath(), "FluxFinishing-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
                // A fixed owned filename avoids interpolating user paths into FFmpeg filter syntax.
                await CopyCube(version.Path, Path.Combine(work, "grade.cube"), token).ConfigureAwait(false); version.Verify();
            }
            await OfflineMediaJob.Publish(destination, ".mp4", versions, staging => FfmpegJob.Run(["-loglevel", "error", "-i", versions[0].Path, "-vf", graph, "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn", "-map_metadata", "-1", "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-threads:v", "2", "-c:a", "aac", "-b:a", "192k", "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv", "-movflags", "+faststart", staging], token, workingDirectory: work), token).ConfigureAwait(false);
        }
        finally
        {
            if (work != null) { string owned = Path.Combine(work, "grade.cube"); if (File.Exists(owned)) File.Delete(owned); if (Directory.Exists(work)) Directory.Delete(work, false); }
        }
    }, token);
    public static Task<LuminanceReport> AnalyzeLuminance(string source, CancellationToken token) => Task.Run(async () =>
    {
        var version = SourceVersion.Capture(source); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        string log = await FfmpegJob.Run(["-loglevel", "info", "-i", version.Path, "-t", "60", "-map", "0:v:0", "-an", "-vf", "fps=1,scale=w='min(640,iw)':h=-2,format=yuv420p,signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-frames:v", "60", "-f", "null", "-"], deadline.Token).ConfigureAwait(false);
        version.Verify(); var values = LumaPattern().Matches(log).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Where(double.IsFinite).Take(60).ToArray();
        if (values.Length == 0) throw new IOException("No frame-average luminance measurements were returned.");
        return new LuminanceReport(version.Path, version.Bytes, version.Ticks, values.Length, values.Min(), values.Average(), values.Max(), "First 60 seconds, up to 60 one-per-second reduced 8-bit YAVG samples (0–255). Not nits, HDR peak, calibrated brightness or a full-file scan.");
    }, token);
    public static Task SaveReport(LuminanceReport report, string destination, CancellationToken token) => OfflineMediaJob.Publish(destination, ".json", [], staging => File.WriteAllTextAsync(staging, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), token), token);
    [GeneratedRegex(@"lavfi\.signalstats\.YAVG=([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex LumaPattern();
}
