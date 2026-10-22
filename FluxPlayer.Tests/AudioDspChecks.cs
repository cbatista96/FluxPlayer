using FluxPlayer.Services;
using System.IO;
using System.Runtime.InteropServices;

internal static class AudioDspChecks
{
    public static async Task VerifyIsoBandsAsync(string root)
    {
        foreach (var (frequency, band) in new[] { (1000.0, 5), (31.25, 0) })
        {
            string source = Path.Combine(root, $"tone-{frequency:0.##}.wav"); WriteTone(source, frequency);
            double reference = await CaptureRmsAsync(source, null, 0);
            double flat = await CaptureRmsAsync(source, new float[10], 0);
            var gains = new float[10]; gains[band] = 12;
            double boosted = await CaptureRmsAsync(source, gains, 0);
            double compensated = await CaptureRmsAsync(source, gains, -12);
            double ratio = boosted / reference;
            Console.WriteLine($"DSP {frequency} Hz: RMS {reference:0.#####}/{boosted:0.#####}/{compensated:0.#####}");
            if (!double.IsFinite(ratio) || !double.IsFinite(compensated / reference) || reference <= 0 || flat / reference is < 0.97 or > 1.03
                || ratio is < 3.2 or > 4.8 || compensated / reference is < 0.88 or > 1.12)
                throw new InvalidOperationException($"Band {band} at {frequency} Hz: +12 dB ratio={ratio:0.###}, compensated ratio={compensated / reference:0.###}.");
        }
    }

    public static async Task VerifyCompressorAsync(string root)
    {
        string source = Path.Combine(root, "compressor-tone.wav"); WriteTone(source, 1000, 24_000);
        double reference = await CaptureRmsAsync(source, null, 0);
        double compressed = await CaptureRmsAsync(source, null, 0, "compressor");
        Console.WriteLine($"Compressor: RMS {reference:0.#####}/{compressed:0.#####}");
        if (!double.IsFinite(compressed) || compressed / reference is < 0.01 or > 0.85)
            throw new InvalidOperationException($"The enabled compressor must measurably reduce the loud test tone: ratio={compressed / reference:0.###}.");
    }

    private static async Task<double> CaptureRmsAsync(string source, float[]? bands, float preamp, string? filters = null)
    {
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        engine.LibVLC.Log += (_, e) => logs.Enqueue(e.Module + ": " + e.Message);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long samples = 0, measured = 0; double sum = 0; int crossings = 0; short previous = 0;
        engine.MediaPlayer.SetAudioFormat("S16N", 48_000, 2);
        engine.MediaPlayer.SetAudioCallbacks((_, pointer, count, _) =>
        {
            if (ready.Task.IsCompleted) return;
            var values = new short[checked((int)count * 2)]; Marshal.Copy(pointer, values, 0, values.Length);
            foreach (short sample in values)
            {
                // Discard the initial half second so the low-frequency filter has settled.
                if (++samples <= 48_000 || measured >= 96_000) continue;
                if (samples % 2 == 1) { if (previous < 0 && sample >= 0) crossings++; previous = sample; }
                double value = sample / 32768.0;
                sum += value * value; measured++;
            }
            if (measured >= 96_000) ready.TrySetResult();
        }, null!, null!, null!, null!);
        if (bands != null) engine.ApplyEqualizer(bands, preamp);
        engine.SetVolume(100);
        using var diagnosticMedia = filters == null ? null : new LibVLCSharp.Shared.Media(engine.LibVLC, new Uri(source));
        if (diagnosticMedia != null)
        {
            diagnosticMedia.AddOption(":audio-filter=" + filters);
            engine.MediaPlayer.Play(diagnosticMedia);
        }
        else engine.PlayFile(source);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        engine.Stop();
        if (crossings == 0) throw new InvalidOperationException("The capture must contain an audible-frequency test signal.");
        File.WriteAllLines(source + "." + (bands == null ? "flat" : preamp.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ".log", logs);
        return Math.Sqrt(sum / measured);
    }

    private static void WriteTone(string path, double frequency, short amplitude = 1000)
    {
        const int frames = 48_000 * 3, dataSize = frames * 4;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(dataSize + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48_000);
        writer.Write(192_000); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(dataSize);
        for (int frame = 0; frame < frames; frame++)
        {
            short value = (short)(Math.Sin(frame * 2 * Math.PI * frequency / 48_000) * amplitude);
            writer.Write(value); writer.Write(value);
        }
    }
}
