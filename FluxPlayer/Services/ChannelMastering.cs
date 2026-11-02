using FluxPlayer.Models;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>Explicit file rendering, independent from the stereo playback DSP and device configuration.</summary>
public static class ChannelMastering
{
    public static readonly string[] Layouts = ["mono", "stereo", "2.1", "5.1(side)", "7.1"];
    public static string[] Speakers(string layout) => layout switch
    {
        "mono" => ["FC"], "stereo" => ["FL", "FR"], "2.1" => ["FL", "FR", "LFE"],
        "5.1(side)" => ["FL", "FR", "FC", "LFE", "SL", "SR"],
        "7.1" => ["FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR"],
        _ => throw new ArgumentException("Choose a supported speaker layout.")
    };
    private static string N(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Expression(double[] coefficients)
    {
        var terms = new List<string>();
        for (int i = 0; i < coefficients.Length; i++)
        {
            double value = coefficients[i]; if (value == 0) continue;
            terms.Add((value < 0 ? "-" : terms.Count > 0 ? "+" : "") + N(Math.Abs(value)) + "*c" + i);
        }
        return terms.Count == 0 ? "0*c0" : string.Concat(terms);
    }
    public static double[][] Preset(string input, string output, bool spread)
    {
        var from = Speakers(input); var to = Speakers(output);
        var matrix = to.Select(_ => new double[from.Length]).ToArray();
        for (int i = 0; i < to.Length; i++)
        {
            int match = Array.IndexOf(from, to[i]); if (match >= 0) matrix[i][match] = 1;
            if (spread && input == "stereo")
            {
                if (to[i] == "FC") { matrix[i][0] = .5; matrix[i][1] = .5; }
                if (to[i] is "SL" or "BL") { matrix[i][0] = .5; matrix[i][1] = -.5; }
                if (to[i] is "SR" or "BR") { matrix[i][0] = -.5; matrix[i][1] = .5; }
                // LFE is populated only by the explicitly selected bass-management stage.
            }
        }
        return matrix;
    }
    public static void Validate(ChannelMasteringOptions o)
    {
        int input = Speakers(o.InputLayout).Length, count = Speakers(o.OutputLayout).Length;
        if (o.SampleRate is not (44100 or 48000 or 96000 or 192000) || o.Bits is not (16 or 24 or 32)
            || o.Matrix == null || o.Matrix.Length != count || o.Matrix.Any(r => r == null || r.Length != input || r.Any(v => !double.IsFinite(v) || Math.Abs(v) > 2) || r.Sum(Math.Abs) > 4)
            || o.GainDb == null || o.GainDb.Length != count || o.GainDb.Any(v => !double.IsFinite(v) || v is < -60 or > 12)
            || o.DelayMs == null || o.DelayMs.Length != count || o.DelayMs.Any(v => v is < 0 or > 2000)
            || o.Order == null || o.Order.Length != count || !o.Order.Order().SequenceEqual(Enumerable.Range(0, count))
            || !double.IsFinite(o.CrossoverHz) || o.CrossoverHz is < 40 or > 200
            || !double.IsFinite(o.HeadroomDb) || o.HeadroomDb is < -24 or > 0
            || !double.IsFinite(o.SecondaryGain) || o.SecondaryGain is < 0 or > 1)
            throw new ArgumentException("Check matrix dimensions, channel permutation, finite gains, delays and format bounds.");
        if (o.BassManagement && !Speakers(o.OutputLayout).Contains("LFE")) throw new ArgumentException("Bass redirection requires a surround output with an LFE channel.");
    }
    public static string Graph(ChannelMasteringOptions o, bool second)
    {
        Validate(o); var names = Speakers(o.OutputLayout); int count = names.Length, inputs = Speakers(o.InputLayout).Length;
        var graph = new List<string>();
        string Normalize(int index, string label) => $"[{index}:a:0]asetpts=PTS-STARTPTS,aresample={o.SampleRate},aformat=sample_fmts=dblp:channel_layouts={o.InputLayout}[{label}]";
        graph.Add(Normalize(0, "primary")); string source = "primary";
        if (second)
        {
            graph.Add(Normalize(1, "secondary"));
            graph.Add("[primary][secondary]amerge=inputs=2,pan=" + o.InputLayout + "|" + string.Join('|', Enumerable.Range(0, inputs).Select(i => $"c{i}=c{i}+{N(o.SecondaryGain)}*c{i + inputs}")) + "[mixed]"); source = "mixed";
        }
        var routes = o.Matrix.Select((row, i) => "c" + i + "=" + Expression(row));
        graph.Add("[" + source + "]pan=" + o.OutputLayout + "|" + string.Join('|', routes) + "[routed]");
        source = "routed";
        if (o.BassManagement)
        {
            graph.Add("[routed]asplit=2[mainsin][bassin]");
            graph.Add("[mainsin]highpass=f=" + N(o.CrossoverHz) + ":p=2:precision=f64,pan=" + o.OutputLayout + "|" + string.Join('|', names.Select((name, i) => name + "=" + (name == "LFE" ? "0*c0" : "c" + i))) + "[mains]");
            string sum = string.Join('+', names.Select((name, i) => N(name == "LFE" ? 1 : 1d / (count - 1)) + "*c" + i));
            graph.Add("[bassin]pan=mono|c0=" + sum + ",lowpass=f=" + N(o.CrossoverHz) + ":p=2:precision=f64,pan=" + o.OutputLayout + "|" + string.Join('|', names.Select(name => name + "=" + (name == "LFE" ? "c0" : "0*c0"))) + "[bass]");
            graph.Add("[mains][bass]amerge=inputs=2,pan=" + o.OutputLayout + "|" + string.Join('|', Enumerable.Range(0, count).Select(i => $"c{i}=c{i}+c{i + count}")) + "[managed]"); source = "managed";
        }
        // Route first, then permute, then apply controls to physical output positions.
        graph.Add("[" + source + "]pan=" + o.OutputLayout + "|" + string.Join('|', o.Order.Select((value, i) => $"c{i}=c{value}"))
            + ",aformat=sample_fmts=dblp,aeval=exprs='" + string.Join('|', o.GainDb.Select((gain, i) => $"val({i})*{N(Math.Pow(10, (gain + o.HeadroomDb) / 20))}")) + "':c=" + o.OutputLayout
            + ",adelay=" + string.Join('|', o.DelayMs) + "[out]");
        return string.Join(';', graph);
    }
    public static Task Export(string primary, string secondary, string destination, ChannelMasteringOptions options, CancellationToken token) => Task.Run(async () =>
    {
        // Detach mutable UI arrays before any asynchronous job work.
        Validate(options);
        options = options with { Matrix = options.Matrix.Select(r => r.ToArray()).ToArray(), GainDb = options.GainDb.ToArray(), DelayMs = options.DelayMs.ToArray(), Order = options.Order.ToArray() };
        string graph = Graph(options, secondary != ""); var sources = new List<SourceVersion> { SourceVersion.Capture(primary) };
        if (secondary != "") sources.Add(SourceVersion.Capture(secondary));
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".wav" or ".flac") || extension == ".flac" && options.Bits == 32) throw new ArgumentException("Use WAV 16/24/32-bit or FLAC 16/24-bit.");
        var args = new List<string> { "-loglevel", "error" }; foreach (var source in sources) args.AddRange(["-i", source.Path]);
        args.AddRange(["-filter_complex", graph, "-map", "[out]", "-map_metadata", "-1", "-vn", "-sn", "-dn", "-c:a", extension == ".flac" ? "flac" : "pcm_s" + options.Bits + "le"]);
        if (extension == ".flac") args.AddRange(["-sample_fmt", options.Bits == 16 ? "s16" : "s32", "-bits_per_raw_sample", options.Bits.ToString(CultureInfo.InvariantCulture)]);
        else args.AddRange(["-rf64", "auto"]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(2));
        await OfflineMediaJob.Publish(destination, extension, sources, staging => FfmpegJob.Copy(args, staging, extension, 0, null, deadline.Token), deadline.Token).ConfigureAwait(false);
    }, token);
}
