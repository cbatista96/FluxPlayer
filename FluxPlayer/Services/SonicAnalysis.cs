using FluxPlayer.Models;
using System.Collections.Immutable;
using System.Numerics;

namespace FluxPlayer.Services;

/// <summary>Bounded source-audio descriptors, not semantic or machine-learning identification.</summary>
public static class SonicAnalysis
{
    public const int SampleRate = 11025, FftSize = 2048, BandCount = 32;
    public static async Task<SpectrumInspection> Inspect(string path, string title, string artist, string album, CancellationToken token)
    {
        if (!MediaFormats.IsMedia(path)) throw new ArgumentException("Choose a recognized local audio/video source, rather than a playlist.");
        var version = SourceVersion.Capture(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(10)); token = deadline.Token;
        byte[] pcm = await FfmpegJob.Pcm(version.Path, 120, token).ConfigureAwait(false);
        var result = await Task.Run(() => AnalyzePcm(pcm, version, title, artist, album, token), token).ConfigureAwait(false);
        version.Verify(); return result;
    }
    private static SpectrumInspection AnalyzePcm(byte[] pcm, SourceVersion source, string title, string artist, string album, CancellationToken token)
    {
        int count = pcm.Length / 2; if (count < 256) throw new ArgumentException("Choose a source with at least 256 decoded audio samples.");
        double Sample(int i) => BitConverter.ToInt16(pcm, i * 2) / 32768d;
        var waveform = new double[512]; double energy = 0; int crossings = 0; double previous = Sample(0);
        for (int i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested(); double sample = Sample(i); energy += sample * sample;
            if ((sample >= 0) != (previous >= 0)) crossings++; previous = sample;
            int bin = (int)((long)i * waveform.Length / count); waveform[bin] = Math.Max(waveform[bin], Math.Abs(sample));
        }
        var power = new double[FftSize / 2]; var values = new Complex[FftSize]; int frames = Math.Min(128, Math.Max(1, count / FftSize));
        for (int frame = 0; frame < frames; frame++)
        {
            token.ThrowIfCancellationRequested(); int offset = frames == 1 ? 0 : (int)((long)frame * Math.Max(0, count - FftSize) / (frames - 1));
            for (int i = 0; i < FftSize; i++) values[i] = new Complex(offset + i < count ? Sample(offset + i) * (.5 - .5 * Math.Cos(2 * Math.PI * i / (FftSize - 1))) : 0, 0);
            Fft(values);
            for (int i = 1; i < power.Length; i++) power[i] += values[i].Magnitude * values[i].Magnitude / (frames * (double)FftSize * FftSize);
        }
        var bands = new double[BandCount]; double total = 0, weighted = 0;
        for (int i = 1; i < power.Length; i++)
        {
            double hz = i * (double)SampleRate / FftSize; total += power[i]; weighted += hz * power[i];
            int band = (int)(Math.Log(Math.Max(50, hz) / 50) / Math.Log((SampleRate / 2d) / 50) * BandCount); bands[Math.Clamp(band, 0, BandCount - 1)] += power[i];
        }
        for (int i = 0; i < bands.Length; i++) bands[i] = total > 1e-20 ? Math.Sqrt(bands[i] / total) : 0;
        var tempo = AudioAnalysis.EstimateTempo(pcm, token);
        var signature = new SonicSignature(source.Path, title, artist, album, source.Bytes, source.Ticks, DateTimeOffset.UtcNow, count / (double)SampleRate, bands.ToImmutableArray(), Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, energy / count))), total > 1e-20 ? weighted / total : 0, crossings / (double)count, tempo.Bpm);
        return new(signature, waveform.ToImmutableArray(), power.Select(v => Math.Clamp(10 * Math.Log10(Math.Max(1e-12, v)), -120, 0)).ToImmutableArray());
    }
    private static void Fft(Complex[] values)
    {
        int n = values.Length, reversed = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1; while ((reversed & bit) != 0) { reversed ^= bit; bit >>= 1; } reversed ^= bit;
            if (i < reversed) (values[i], values[reversed]) = (values[reversed], values[i]);
        }
        for (int size = 2; size <= n; size <<= 1)
        {
            var root = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (int start = 0; start < n; start += size)
            {
                Complex weight = Complex.One;
                for (int j = 0; j < size / 2; j++) { Complex even = values[start + j], odd = values[start + j + size / 2] * weight; values[start + j] = even + odd; values[start + j + size / 2] = even - odd; weight *= root; }
            }
        }
    }
    public static double Similarity(SonicSignature a, SonicSignature b)
    {
        double dot = 0, first = 0, second = 0;
        for (int i = 0; i < BandCount; i++) { dot += a.Bands[i] * b.Bands[i]; first += a.Bands[i] * a.Bands[i]; second += b.Bands[i] * b.Bands[i]; }
        if (first < 1e-12 || second < 1e-12) return 0;
        double timbre = dot / Math.Sqrt(first * second), centroid = 1 - Math.Min(1, Math.Abs(a.CentroidHz - b.CentroidHz) / (SampleRate / 2d));
        double rhythm = a.Tempo.HasValue && b.Tempo.HasValue ? 1 - Math.Min(1, Math.Abs(a.Tempo.Value - b.Tempo.Value) / 120) : 1 - Math.Min(1, Math.Abs(a.ZeroCrossing - b.ZeroCrossing));
        return Math.Clamp(.8 * timbre + .1 * centroid + .1 * rhythm, 0, 1);
    }
    public static SonicSignature Aggregate(IEnumerable<SonicSignature> values)
    {
        var rows = values.ToArray(); if (rows.Length == 0) throw new ArgumentException("Analyze at least one matching track.");
        var first = rows[0]; var bands = Enumerable.Range(0, BandCount).Select(i => rows.Average(r => r.Bands[i])).ToArray();
        double norm = Math.Sqrt(bands.Sum(v => v * v)); if (norm > 0) for (int i = 0; i < bands.Length; i++) bands[i] /= norm;
        var tempos = rows.Where(r => r.Tempo.HasValue).Select(r => r.Tempo!.Value).ToArray();
        return first with { Bands = bands.ToImmutableArray(), RmsDb = rows.Average(r => r.RmsDb), CentroidHz = rows.Average(r => r.CentroidHz), ZeroCrossing = rows.Average(r => r.ZeroCrossing), Tempo = tempos.Length == 0 ? null : tempos.Average() };
    }
}
