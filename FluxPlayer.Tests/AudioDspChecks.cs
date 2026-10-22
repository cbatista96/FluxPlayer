using FluxPlayer.Services;
using FluxPlayer.Models;
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

    public static async Task VerifyExtendedBandsAsync(string root)
    {
        foreach (int count in new[] { 15, 20, 31 })
        {
            double[] frequencies = EqualizerLayout.Frequencies(count);
            int center = count switch { 31 => 17, 20 => 10, _ => 8 };
            foreach (int band in new[] { center, 0, count - 1 })
            {
                double frequency = frequencies[band];
                string source = Path.Combine(root, $"native-{count}-band-{band}.wav"); WriteTone(source, frequency);
                double reference = await CaptureRmsAsync(source, null, 0);
                double flat = await CaptureRmsAsync(source, new float[count], 0);
                var gains = new float[count]; gains[band] = 6;
                double boosted = await CaptureRmsAsync(source, gains, 0);
                double protectedValue = await CaptureRmsAsync(source, gains, 0, antiClip: true);
                double ratio = boosted / reference;
                Console.WriteLine($"Native {count} bands, {frequency:0.###} Hz: flat={flat / reference:0.###}, boost={ratio:0.###}, compensated={protectedValue / reference:0.###}");
                if (flat / reference is < 0.97 or > 1.03 || ratio is < 1.8 or > 2.2 || protectedValue / reference is < 0.85 or > 1.05)
                    throw new InvalidOperationException("The independent native band must change the actual processed PCM and preserve flat/headroom levels.");
            }
        }
        foreach (int rate in new[] { 192_000, 384_000 })
        {
            string source = Path.Combine(root, "native-high-rate-" + rate + ".wav"); WriteTone(source, 20, sampleRate: rate);
            var gains = new float[31]; gains[0] = 6;
            double reference = await CaptureRmsAsync(source, null, 0, sampleRate: rate);
            double protectedValue = await CaptureRmsAsync(source, gains, 0, antiClip: true, sampleRate: rate);
            Console.WriteLine($"Native 31 bands, 20 Hz at {rate} Hz: compensated={protectedValue / reference:0.###}");
            if (protectedValue / reference is < 0.85 or > 1.05)
                throw new InvalidOperationException($"Low-frequency headroom must remain stable at {rate} Hz: ratio={protectedValue / reference:0.###}.");
        }
        string rangeSource = Path.Combine(root, "native-gain-range.wav"); WriteTone(rangeSource, 1000);
        double rangeReference = await CaptureRmsAsync(rangeSource, null, 0);
        var cut = new float[31]; cut[17] = -20;
        double cutValue = await CaptureRmsAsync(rangeSource, cut, 0);
        double preampValue = await CaptureRmsAsync(rangeSource, new float[31], 20);
        if (cutValue / rangeReference is < 0.095 or > 0.105 || preampValue / rangeReference is < 9.8 or > 10.2)
            throw new InvalidOperationException("The native processor must honor the extended ±20 dB gain range.");
    }

    private static async Task<double> CaptureRmsAsync(string source, float[]? bands, float preamp, string? filters = null, bool antiClip = false, int sampleRate = 48_000)
    {
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        engine.LibVLC.Log += (_, e) => logs.Enqueue(e.Module + ": " + e.Message);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long samples = 0, measured = 0; double sum = 0; int crossings = 0; short previous = 0;
        engine.MediaPlayer.SetAudioFormat("S16N", (uint)sampleRate, 2);
        engine.MediaPlayer.SetAudioCallbacks((_, pointer, count, _) =>
        {
            if (ready.Task.IsCompleted) return;
            var values = new short[checked((int)count * 2)]; Marshal.Copy(pointer, values, 0, values.Length);
            foreach (short sample in values)
            {
                // Discard the initial half second so the low-frequency filter has settled.
                if (++samples <= sampleRate || measured >= sampleRate * 2L) continue;
                if (samples % 2 == 1) { if (previous < 0 && sample >= 0) crossings++; previous = sample; }
                double value = sample / 32768.0;
                sum += value * value; measured++;
            }
            if (measured >= sampleRate * 2L) ready.TrySetResult();
        }, null!, null!, null!, null!);
        // VLC 3's fixed-rate amem storage is only 18 bits. Its setup callback
        // negotiates the full unsigned rate and avoids truncating 384 kHz.
        if (sampleRate > 262_143)
            engine.MediaPlayer.SetAudioFormatCallback((ref IntPtr opaque, ref IntPtr format, ref uint rate, ref uint channels) =>
            {
                rate = (uint)sampleRate; channels = 2; return 0; // amem defaults to S16N.
            }, null!);
        if (bands != null) engine.ApplyEqualizer(bands, preamp, antiClip);
        engine.SetVolume(100);
        using var diagnosticMedia = filters == null ? null : new LibVLCSharp.Shared.Media(engine.LibVLC, new Uri(source));
        if (diagnosticMedia != null)
        {
            diagnosticMedia.AddOption(":audio-filter=" + filters);
            engine.MediaPlayer.Play(diagnosticMedia);
        }
        else engine.PlayFile(source);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        finally
        {
            logs.Enqueue($"capture: rate={sampleRate}, samples={samples}, measured={measured}, native={engine.ExtendedEqualizerStatus}");
            File.WriteAllLines(source + "." + (bands == null ? "flat" : preamp.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ".log", logs);
        }
        if (bands?.Length is 15 or 20 or 31)
        {
            var status = engine.ExtendedEqualizerStatus;
            if (status is not { Active: true, CurrentSettingsApplied: true, Channels: 2, Error: 0 } || status.SampleRate != (uint)sampleRate)
                throw new InvalidOperationException("The native multiband plugin must acknowledge actual processing: " + status);
        }
        engine.Stop();
        if (crossings == 0) throw new InvalidOperationException("The capture must contain an audible-frequency test signal.");
        return Math.Sqrt(sum / measured);
    }

    private static void WriteTone(string path, double frequency, short amplitude = 1000, int sampleRate = 48_000)
    {
        int frames = sampleRate * 3, dataSize = frames * 4;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(dataSize + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(sampleRate);
        writer.Write(sampleRate * 4); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(dataSize);
        for (int frame = 0; frame < frames; frame++)
        {
            short value = (short)(Math.Sin(frame * 2 * Math.PI * frequency / sampleRate) * amplitude);
            writer.Write(value); writer.Write(value);
        }
    }
}
