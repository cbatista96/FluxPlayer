using FluxPlayer.Models;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>Windowed-sinc mono impulse design for explicit offline convolution.</summary>
public static class FirDesigner
{
    public static Task Write(string destination, FirDesign options, CancellationToken token) => Task.Run(async () =>
    {
        if (options.Mode is not ("Low-pass" or "High-pass" or "Band-pass") || options.SampleRate is not (44100 or 48000 or 96000 or 192000) || options.Taps is < 31 or > 8191 || options.Taps % 2 != 1 || !double.IsFinite(options.LowHz) || !double.IsFinite(options.HighHz)
            || options.Mode != "Low-pass" && (options.LowHz < 10 || options.LowHz >= options.SampleRate * .49)
            || options.Mode != "High-pass" && (options.HighHz < 10 || options.HighHz >= options.SampleRate * .49)
            || options.Mode == "Band-pass" && options.LowHz >= options.HighHz)
            throw new ArgumentException("Use an odd 31–8,191 tap count and active cutoffs from 10 Hz to below 49% of sample rate. A band-pass needs low < high.");
        var taps = new double[options.Taps]; int center = options.Taps / 2;
        double LowPass(double hz, int n) => n == 0 ? 2 * hz / options.SampleRate : Math.Sin(2 * Math.PI * hz * n / options.SampleRate) / (Math.PI * n);
        for (int i = 0; i < taps.Length; i++)
        {
            token.ThrowIfCancellationRequested(); int n = i - center;
            double value = options.Mode switch { "High-pass" => (n == 0 ? 1 : 0) - LowPass(options.LowHz, n), "Band-pass" => LowPass(options.HighHz, n) - LowPass(options.LowHz, n), _ => LowPass(options.HighHz, n) };
            taps[i] = value * (.54 - .46 * Math.Cos(2 * Math.PI * i / (taps.Length - 1)));
        }
        double reference = options.Mode switch { "High-pass" => options.SampleRate / 2d, "Band-pass" => (options.LowHz + options.HighHz) / 2, _ => 0 };
        double re = 0, im = 0;
        for (int i = 0; i < taps.Length; i++) { double phase = 2 * Math.PI * reference * (i - center) / options.SampleRate; re += taps[i] * Math.Cos(phase); im -= taps[i] * Math.Sin(phase); }
        double gain = Math.Sqrt(re * re + im * im); if (gain < 1e-9) throw new ArgumentException("The design is degenerate. Widen its passband or use more taps.");
        await OfflineMediaJob.Publish(destination, ".wav", [], async staging =>
        {
            using var bytes = new MemoryStream(); using (var writer = new BinaryWriter(bytes, System.Text.Encoding.ASCII, true))
            {
                void Four(string value) => writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
                int length = taps.Length * 4; Four("RIFF"); writer.Write(length + 48); Four("WAVE"); Four("fmt "); writer.Write(16); writer.Write((short)3); writer.Write((short)1); writer.Write(options.SampleRate); writer.Write(options.SampleRate * 4); writer.Write((short)4); writer.Write((short)32); Four("fact"); writer.Write(4); writer.Write(taps.Length); Four("data"); writer.Write(length);
                foreach (double tap in taps) { token.ThrowIfCancellationRequested(); writer.Write((float)(tap / gain)); }
            }
            await File.WriteAllBytesAsync(staging, bytes.ToArray(), token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }, token);
}
