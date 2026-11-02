using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public static partial class AudioLab
{
    public static readonly string[] Resamplers = ["Cubic", "Blackman-Nuttall", "Kaiser"];
    public static readonly string[] Dithers = ["None", "Rectangular", "Triangular", "High-pass triangular"];
    public static readonly string[] NoiseShapes = ["None", "Shibata", "Low Shibata", "Lipshitz"];
    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static void Validate(AudioLabOptions o)
    {
        if (o.SampleRate is not (44100 or 48000 or 96000 or 192000) || o.Bits is not (16 or 24 or 32) || !Resamplers.Contains(o.Resampler) || !Dithers.Contains(o.Dither) || !NoiseShapes.Contains(o.NoiseShape) || !double.IsFinite(o.PitchSemitones) || o.PitchSemitones is < -12 or > 12 || !double.IsFinite(o.Tempo) || o.Tempo is < .5 or > 2 || !double.IsFinite(o.TargetPeakDb) || o.TargetPeakDb is < -12 or > -.1 || !double.IsFinite(o.ImpulseGain) || o.ImpulseGain is < 0 or > 1)
            throw new ArgumentException("Check the audio format and bounded pitch, tempo, gain and peak settings.");
        if ((o.Dither != "None" || o.NoiseShape != "None") && o.Bits != 16) throw new ArgumentException("Dither/noise shaping in this workflow requires 16-bit output.");
        if (o.NoiseShape != "None" && o.SampleRate > 48000 || o.NoiseShape == "Lipshitz" && o.SampleRate != 44100) throw new ArgumentException("Use 44.1/48 kHz for noise shaping; Lipshitz requires 44.1 kHz.");
    }
    // Cubic interpolation uses a four-point kernel. Applying the 64-tap sinc
    // setting to it widens the polynomial and audibly attenuates the passband.
    private static string Resample(AudioLabOptions o) => "aresample=" + o.SampleRate.ToString(CultureInfo.InvariantCulture) + ":resampler=swr:filter_type=" + (o.Resampler switch { "Cubic" => "cubic", "Blackman-Nuttall" => "blackman_nuttall", _ => "kaiser" }) + ":filter_size=" + (o.Resampler == "Cubic" ? "4" : "64");
    public static string Graph(AudioLabOptions o)
    {
        Validate(o); var filters = new List<string> { Resample(o), "aformat=sample_fmts=fltp:channel_layouts=stereo" };
        if (o.RemoveDc) filters.Add("highpass=f=20:p=2");
        if (o.RestoreRiaa) filters.Add("aemphasis=type=riaa:mode=reproduction");
        double pitch = Math.Pow(2, o.PitchSemitones / 12);
        if (o.PitchSemitones != 0) { filters.Add("asetrate=" + Math.Round(o.SampleRate * pitch).ToString(CultureInfo.InvariantCulture)); filters.Add(Resample(o)); }
        double tempo = o.Tempo / pitch;
        while (tempo < .5) { filters.Add("atempo=0.5"); tempo /= .5; }
        while (tempo > 2) { filters.Add("atempo=2"); tempo /= 2; }
        if (Math.Abs(tempo - 1) > 1e-8) filters.Add("atempo=" + N(tempo));
        string source = "[0:a:0]" + string.Join(',', filters) + "[clean]";
        if (o.Impulse == "") return source + ";[clean]anull[processed]";
        return source + ";[1:a:0]" + Resample(o) + ",aformat=sample_fmts=fltp:channel_layouts=mono,atrim=duration=10[ir];[clean][ir]afir=irfmt=mono:maxir=10:dry=1:wet=1:irnorm=-1:irgain=" + N(o.ImpulseGain) + "[processed]";
    }
    private static string Quantize(AudioLabOptions o)
    {
        string method = o.NoiseShape switch { "Shibata" => "shibata", "Low Shibata" => "low_shibata", "Lipshitz" => "lipshitz", _ => o.Dither switch { "Rectangular" => "rectangular", "Triangular" => "triangular", "High-pass triangular" => "triangular_hp", _ => "none" } };
        return "aresample=" + o.SampleRate.ToString(CultureInfo.InvariantCulture) + ":osf=" + (o.Bits == 16 ? "s16" : "s32") + ":output_sample_bits=" + o.Bits.ToString(CultureInfo.InvariantCulture) + ":dither_method=" + method;
    }
    public static Task Export(string source, string destination, AudioLabOptions options, IProgress<string>? progress, CancellationToken token) => Task.Run(async () =>
    {
        Validate(options); string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (!MediaFormats.IsMedia(source) || options.Impulse != "" && !MediaFormats.IsAudio(options.Impulse)) throw new ArgumentException("Choose local audio/video source media and an audio impulse file.");
        if (extension is not (".wav" or ".flac") || extension == ".flac" && options.Bits == 32) throw new ArgumentException("Choose WAV (16/24/32-bit PCM) or FLAC (16/24-bit).");
        var versions = new List<SourceVersion> { SourceVersion.Capture(source) }; if (options.Impulse != "") versions.Add(SourceVersion.Capture(options.Impulse));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2)); token = deadline.Token;
        var inputs = new List<string> { "-loglevel", "info", "-i", versions[0].Path }; if (versions.Count == 2) inputs.AddRange(["-i", versions[1].Path]);
        string graph = Graph(options); double gain = 0;
        await OfflineMediaJob.Publish(destination, extension, versions, async staging =>
        {
            if (options.NormalizePeak)
            {
                progress?.Report("Measuring processed sample peak (first pass)…");
                string report = await FfmpegJob.Run(inputs.Concat(["-filter_complex", graph + ";[processed]astats=metadata=0:reset=0:measure_perchannel=none:measure_overall=Peak_level[out]", "-map", "[out]", "-c:a", "pcm_f32le", "-f", "null", "-"]), token).ConfigureAwait(false);
                var value = PeakPattern().Matches(report).LastOrDefault()?.Groups[1].Value;
                if (value is null) throw new IOException("FFmpeg did not return a processed sample-peak measurement.");
                if (value is not ("-inf" or "-infinity"))
                {
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double peak) || !double.IsFinite(peak)) throw new IOException("Invalid peak report.");
                    gain = Math.Min(24, options.TargetPeakDb - peak); if (gain < -60) throw new IOException("The processed level is excessive. Reduce the impulse/input gain.");
                }
                foreach (var version in versions) version.Verify();
            }
            progress?.Report("Rendering a new processed audio file…");
            string tail = ";[processed]volume=" + N(gain) + "dB," + Quantize(options) + "[out]";
            var args = inputs.Concat(["-filter_complex", graph + tail, "-map", "[out]", "-map_metadata", "0", "-vn", "-c:a", extension == ".flac" ? "flac" : "pcm_s" + options.Bits.ToString(CultureInfo.InvariantCulture) + "le"] ).ToList();
            if (extension == ".flac") args.AddRange(["-sample_fmt", options.Bits == 16 ? "s16" : "s32", "-bits_per_raw_sample", options.Bits.ToString(CultureInfo.InvariantCulture)]);
            await FfmpegJob.Copy(args, staging, extension, 0, null, token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }, token);
    [GeneratedRegex(@"Peak level dB:\s*([-+\w.]+)", RegexOptions.IgnoreCase)] private static partial Regex PeakPattern();
}
