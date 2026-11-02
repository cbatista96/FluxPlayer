using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record AudioAnalysisResult(string Path, long Size, long ModifiedTicks, DateTimeOffset AnalyzedUtc, double? RmsDb, double? TruePeakDb, double? ReplayGainDb, double? ReplayPeak, double? Bpm, double Confidence, double TempoSeconds)
{
    public string Title => System.IO.Path.GetFileName(Path);
    public string Rms => RmsDb is { } value ? value.ToString("0.0", CultureInfo.InvariantCulture) + " dBFS" : "−∞ / unavailable";
    public string TruePeak => TruePeakDb is { } value ? value.ToString("0.0", CultureInfo.InvariantCulture) + " dBTP" : "−∞ / unavailable";
    public string ReplayGain => ReplayGainDb is { } value ? value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " dB" : "Unavailable";
    public string Tempo => Bpm is { } value ? value.ToString("0.0", CultureInfo.InvariantCulture) + " BPM" : "Uncertain";
}
public static partial class AudioAnalysis
{
    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;
    public static async Task<AudioAnalysisResult> Analyze(string source, CancellationToken token)
    {
        source = FfmpegJob.Source(source); var stamp = new FileInfo(source); long size = stamp.Length, modified = stamp.LastWriteTimeUtc.Ticks;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(30)); token = deadline.Token;
        string graph = "[0:a:0]asplit=3[r][t][g];[r]astats=metadata=0:reset=0:measure_perchannel=none:measure_overall=RMS_level[rms];[t]loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json[tp];[g]replaygain[rg]";
        string report = await FfmpegJob.Run(new[] { "-loglevel", "info", "-i", source, "-filter_complex", graph, "-map", "[rms]", "-map", "[tp]", "-map", "[rg]", "-f", "null", "-" }, token).ConfigureAwait(false);
        double? rms = Number(RmsValue().Matches(report).LastOrDefault()?.Groups[1].Value), gain = Number(GainValue().Matches(report).LastOrDefault()?.Groups[1].Value), peak = Number(PeakValue().Matches(report).LastOrDefault()?.Groups[1].Value), truePeak = null;
        var json = LoudnessJson().Matches(report).LastOrDefault()?.Value;
        if (json != null) { using var document = JsonDocument.Parse(json); if (document.RootElement.TryGetProperty("input_tp", out var value)) truePeak = Number(value.GetString()); }
        byte[] pcm = await FfmpegJob.Pcm(source, 120, token).ConfigureAwait(false); var tempo = await Task.Run(() => EstimateTempo(pcm, token), token).ConfigureAwait(false);
        stamp.Refresh(); if (!stamp.Exists || stamp.Length != size || stamp.LastWriteTimeUtc.Ticks != modified) throw new IOException("The source changed during analysis. Analyze it again.");
        return new(source, size, modified, DateTimeOffset.UtcNow, rms, truePeak, gain, peak, tempo.Bpm, tempo.Confidence, pcm.Length / 22050d);
    }
    public static (double? Bpm, double Confidence) EstimateTempo(byte[] pcm, CancellationToken token = default)
    {
        const int hop = 220; int windows = pcm.Length / (hop * 2); if (windows < 100) return (null, 0);
        var envelope = new double[windows]; double previous = 0, maximum = 0;
        for (int window = 0; window < windows; window++)
        {
            token.ThrowIfCancellationRequested(); double energy = 0;
            for (int sample = 0; sample < hop; sample++) { int offset = (window * hop + sample) * 2; double value = (short)(pcm[offset] | pcm[offset + 1] << 8) / 32768d; energy += value * value; }
            double rms = Math.Sqrt(energy / hop); envelope[window] = Math.Max(0, rms - previous); previous = rms; maximum = Math.Max(maximum, envelope[window]);
        }
        if (maximum < .001) return (null, 0);
        double best = 0, runnerUp = 0, bestBpm = 0; const double envelopeRate = 11025d / hop;
        var scores = new List<(double Bpm, double Score)>();
        for (int bpm = 45; bpm <= 210; bpm++)
        {
            token.ThrowIfCancellationRequested(); double lag = 60 * envelopeRate / bpm, sum = 0, normA = 0, normB = 0;
            for (int index = (int)Math.Ceiling(lag); index < envelope.Length; index++) { double position = index - lag; int a = (int)position; double b = envelope[a] * (1 - (position - a)) + envelope[Math.Min(a + 1, envelope.Length - 1)] * (position - a); sum += envelope[index] * b; normA += envelope[index] * envelope[index]; normB += b * b; }
            double score = sum / Math.Sqrt(Math.Max(1e-20, normA * normB)); scores.Add((bpm, score));
        }
        // Prefer the faster fundamental over an equally supported half-time alias.
        foreach (var item in scores.OrderByDescending(item => item.Score).ThenByDescending(item => item.Bpm)) if (item.Score > best) { best = item.Score; bestBpm = item.Bpm; }
        var faster = scores.Where(item => Math.Abs(item.Bpm - bestBpm * 2) <= 1 && item.Score >= best * .9).OrderByDescending(item => item.Score).FirstOrDefault();
        if (faster.Score > 0) { best = faster.Score; bestBpm = faster.Bpm; }
        // Long correlation lags can line up every third/fourth beat more accurately
        // than a fractional short lag. Regular onset intervals disambiguate those aliases.
        var onsets = new List<int>();
        for (int index = 1; index < envelope.Length - 1; index++)
            if (envelope[index] >= maximum * .25 && envelope[index] >= envelope[index - 1] && envelope[index] > envelope[index + 1] && (onsets.Count == 0 || index - onsets[^1] >= envelopeRate * .12)) onsets.Add(index);
        if (onsets.Count < 4) return (null, 0); // A steady tone's tiny window-energy ripple is not a beat.
        if (onsets.Count >= 8)
        {
            var intervals = onsets.Zip(onsets.Skip(1), (a, b) => (double)(b - a)).Order().ToArray();
            double median = intervals[intervals.Length / 2], onsetBpm = 60 * envelopeRate / median;
            double deviation = intervals.Select(value => Math.Abs(value - median)).Order().ElementAt(intervals.Length / 2) / median;
            var fundamental = scores.Where(item => Math.Abs(item.Bpm - onsetBpm) <= 5).MaxBy(item => item.Score);
            if (deviation < .12 && onsetBpm is >= 45 and <= 210 && fundamental.Score >= .45) { best = fundamental.Score; bestBpm = fundamental.Bpm; }
        }
        foreach (var item in scores.Where(item => Math.Abs(item.Bpm - bestBpm) > 3 && Math.Abs(item.Bpm * 2 - bestBpm) > 4 && Math.Abs(item.Bpm - bestBpm * 2) > 4)) runnerUp = Math.Max(runnerUp, item.Score);
        double confidence = Math.Clamp(best * (1 - runnerUp * .5), 0, 1); return best < .25 ? (null, confidence) : (bestBpm, confidence);
    }
    public static async Task Export(string path, IEnumerable<AudioAnalysisResult> results, CancellationToken token)
    {
        var rows = results.Take(1001).ToArray(); if (rows.Length > 1000) throw new ArgumentException("Export up to 1,000 measurements.");
        await ProfileDocument<AudioAnalysisResult[]>.Write(path, rows, false, 4 * 1024 * 1024, token).ConfigureAwait(false);
    }
    [GeneratedRegex(@"RMS level dB:\s*([-+\w.]+)")] private static partial Regex RmsValue();
    [GeneratedRegex(@"track_gain\s*=\s*([-+\d.]+)\s*dB")] private static partial Regex GainValue();
    [GeneratedRegex(@"track_peak\s*=\s*([-+\d.eE]+)")] private static partial Regex PeakValue();
    [GeneratedRegex("\\{\\s*\"input_i\"[\\s\\S]*?\\}")] private static partial Regex LoudnessJson();
}
