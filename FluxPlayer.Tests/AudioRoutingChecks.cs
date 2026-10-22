using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Runtime.InteropServices;

internal static class AudioRoutingChecks
{
    public static async Task VerifyAsync(string root)
    {
        string stereo = Path.Combine(root, "routing-stereo.wav");
        WriteWave(stereo, new[] { 500.0, 1500.0 });
        foreach (string mode in new[] { "Stereo", "Original", "ReverseStereo", "Mono", "Left", "Right" })
        {
            double[,] signal = await CaptureAsync(stereo, mode, new[] { 500.0, 1500.0 });
            Console.WriteLine($"Routing {mode}: L={signal[0, 0]:0.####}/{signal[0, 1]:0.####}, R={signal[1, 0]:0.####}/{signal[1, 1]:0.####}");
            bool Good(int channel, int frequency) => signal[channel, frequency] > 0.005
                && signal[channel, frequency] > signal[channel, 1 - frequency] * 25;
            bool valid = mode switch
            {
                "Stereo" or "Original" => Good(0, 0) && Good(1, 1),
                "ReverseStereo" => Good(0, 1) && Good(1, 0),
                "Left" => Good(0, 0) && Good(1, 0),
                "Right" => Good(0, 1) && Good(1, 1),
                "Mono" => signal.Cast<double>().All(value => value > 0.005)
                    && Math.Abs(signal[0, 0] / signal[1, 0] - 1) < 0.05
                    && Math.Abs(signal[0, 1] / signal[1, 1] - 1) < 0.05,
                _ => false
            };
            if (!valid) throw new InvalidOperationException("Actual PCM does not match requested channel routing: " + mode);
        }

        string surround = Path.Combine(root, "routing-six-channel.wav");
        double[] frequencies = { 300, 500, 700, 100, 1100, 1300 };
        WriteWave(surround, frequencies);
        double[,] mixed = await CaptureAsync(surround, "Stereo", frequencies, expectedInputChannels: 6);
        if (mixed[0, 0] < 0.001 || mixed[1, 1] < 0.001 || mixed[0, 2] < 0.001 || mixed[1, 2] < 0.001
            || mixed[0, 4] < 0.001 || mixed[1, 5] < 0.001)
            throw new InvalidOperationException("The stereo downmix must retain front, center and rear-channel signals from the six-channel PCM source.");
        Console.WriteLine("Six-channel PCM downmix: front, center and rear contributions verified; LFE behavior is not asserted.");

        string folder = Path.Combine(root, "routing-live-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsService(folder);
        if (!store.Save(new AppSettings { AudioChannels = "ReverseStereo", Volume = 0, EqualizerEnabled = true, EqualizerBands = new float[31] }))
            throw new InvalidOperationException("Save isolated routing settings.");
        string longSource = Path.Combine(root, "routing-live.wav"); WriteWave(longSource, new[] { 500.0, 1500.0 }, 10);
        using var live = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var liveLogs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        live.LibVLC.Log += (_, message) => liveLogs.Enqueue(message.Module + ": " + message.Message);
        await using var library = new LibraryService(Path.Combine(folder, "library"));
        var vm = new MainViewModel(live, store, library: library);
        try
        {
            vm.AddFilesToPlaylist(new[] { longSource }, true);
            await UntilAsync(() => live.IsPlaying && (int)live.MediaPlayer.Channel == 2 && live.ExtendedEqualizerStatus is { Active: true });
            vm.PlayPauseCommand.Execute(null);
            await UntilAsync(() => live.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused);
            long pausedAt = live.GetTime();
            vm.SaveSettings(); var saved = store.Load(); saved.AudioChannels = "Mono";
            if (!store.Save(saved)) throw new InvalidOperationException("Save a changed routing preference.");
            vm.ReloadSettingsFromDisk();
            await UntilAsync(() => (int)live.MediaPlayer.Channel == 7);
            await Task.Delay(1100); // Superseded startup retries must not restore reverse stereo.
            if (live.MediaPlayer.State != LibVLCSharp.Shared.VLCState.Paused || Math.Abs(live.GetTime() - pausedAt) > 250)
                throw new InvalidOperationException("Routing changes must preserve pause and position.");
            if ((int)live.MediaPlayer.Channel != 7 || !vm.SelectOutputDevice(new AudioDevice("Default", "System default")))
                throw new InvalidOperationException("Routing changes supersede pending startup work and survive output selection.");
            vm.AddFilesToPlaylist(new[] { stereo }, true);
            await UntilAsync(() => live.IsPlaying && vm.CurrentItem?.FilePath == stereo && (int)live.MediaPlayer.Channel == 7
                && live.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true });
            if (!live.GetMediaInfo().Contains("Channel routing: Mono", StringComparison.Ordinal))
                throw new InvalidOperationException("Signal-path information must identify actual native routing.");
        }
        catch
        {
            Console.WriteLine($"Live routing failure: state={live.MediaPlayer.State}, channel={(int)live.MediaPlayer.Channel}, DSP={live.ExtendedEqualizerStatus}, configured={live.PlaybackSettings.AudioChannels}, info={live.GetMediaInfo()}");
            throw;
        }
        finally { File.WriteAllLines(Path.Combine(folder, "native.log"), liveLogs); vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("Native routing state did not settle.");
            await Task.Delay(25);
        }
    }

    private static async Task<double[,]> CaptureAsync(string source, string mode, double[] frequencies, int expectedInputChannels = 2)
    {
        const int rate = 48000;
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var firstBuffer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var real = new double[2, frequencies.Length]; var imaginary = new double[2, frequencies.Length];
        int capture = 0, frames = 0, measured = 0;
        engine.MediaPlayer.SetAudioFormat("S16N", rate, 2);
        engine.MediaPlayer.SetAudioCallbacks((_, pointer, count, _) =>
        {
            firstBuffer.TrySetResult();
            if (Volatile.Read(ref capture) == 0 || ready.Task.IsCompleted) return;
            var values = new short[checked((int)count * 2)]; Marshal.Copy(pointer, values, 0, values.Length);
            for (int frame = 0; frame < values.Length / 2; frame++)
            {
                if (++frames <= rate / 2 || measured >= rate) continue;
                for (int frequency = 0; frequency < frequencies.Length; frequency++)
                {
                    double angle = 2 * Math.PI * frequencies[frequency] * measured / rate;
                    double cosine = Math.Cos(angle), sine = Math.Sin(angle);
                    for (int channel = 0; channel < 2; channel++)
                    {
                        double value = values[frame * 2 + channel] / 32768.0;
                        real[channel, frequency] += value * cosine; imaginary[channel, frequency] += value * sine;
                    }
                }
                measured++;
            }
            if (measured >= rate) ready.TrySetResult();
        }, null!, null!, null!, null!);
        engine.SetVolume(100); engine.PlayFile(source);
        await firstBuffer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Native callbacks only deliver samples; configuration runs on this continuation.
        engine.PlaybackSettings.AudioChannels = mode;
        if (!engine.ApplyAudioChannelRouting()) throw new InvalidOperationException("Native routing request failed: " + mode);
        Volatile.Write(ref capture, 1);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(7));
        var metadata = engine.ReadCurrentMetadata();
        if (metadata == null || !metadata.Technical.Contains(expectedInputChannels + " ch", StringComparison.Ordinal))
            throw new InvalidOperationException("The routing source must decode the intended input channel count.");
        int actual = (int)engine.MediaPlayer.Channel;
        if (mode != "Original" && actual != AudioChannelRouting.NativeValue(mode))
            throw new InvalidOperationException($"Native channel acknowledgement differs: {mode} / {actual}.");
        engine.Stop();
        for (int channel = 0; channel < 2; channel++)
            for (int frequency = 0; frequency < frequencies.Length; frequency++)
                real[channel, frequency] = 2 * Math.Sqrt(real[channel, frequency] * real[channel, frequency]
                    + imaginary[channel, frequency] * imaginary[channel, frequency]) / measured;
        return real;
    }

    private static void WriteWave(string path, double[] frequencies, int seconds = 3)
    {
        const int rate = 48000;
        int frames = rate * seconds;
        int dataSize = frames * frequencies.Length * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(dataSize + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)frequencies.Length); writer.Write(rate);
        writer.Write(rate * frequencies.Length * 2); writer.Write((short)(frequencies.Length * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(dataSize);
        for (int frame = 0; frame < frames; frame++)
            foreach (double frequency in frequencies)
                writer.Write((short)(2000 * Math.Sin(frame * 2 * Math.PI * frequency / rate)));
    }
}
