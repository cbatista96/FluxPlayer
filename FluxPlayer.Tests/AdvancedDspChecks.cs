using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;

internal static class AdvancedDspChecks
{
    private const int Rate = 48000;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static string Wave(string folder, string name, Func<int, int, double> sample)
    {
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, name + ".wav");
        using var output = new BinaryWriter(File.Create(path)); const int frames = Rate * 2, length = frames * 4;
        output.Write("RIFF"u8); output.Write(length + 36); output.Write("WAVEfmt "u8); output.Write(16); output.Write((short)1); output.Write((short)2); output.Write(Rate); output.Write(Rate * 4); output.Write((short)4); output.Write((short)16); output.Write("data"u8); output.Write(length);
        for (int frame = 0; frame < frames; frame++) for (int channel = 0; channel < 2; channel++) output.Write((short)Math.Clamp(sample(frame, channel) * 32767, short.MinValue, short.MaxValue)); return path;
    }
    internal static async Task<(float[] Samples, NativeDspStatus? Status)> Capture(string path, AudioEffects effects, bool meters = false)
    {
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        var samples = new float[Rate * 2]; int written = 0; var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.MediaPlayer.SetAudioFormat("S16N", Rate, 2);
        engine.MediaPlayer.SetAudioCallbacks((_, input, count, _) =>
        {
            if (ready.Task.IsCompleted) return; int take = Math.Min(checked((int)count * 2), samples.Length - written);
            if (take > 0) { var pcm = new short[take]; Marshal.Copy(input, pcm, 0, take); for (int i = 0; i < take; i++) samples[written + i] = pcm[i] / 32768f; written += take; }
            if (written == samples.Length) ready.TrySetResult();
        }, null!, null!, null!, null!);
        engine.ApplyAudioEffects(effects); if (meters) engine.EnableSignalPathMeters(true); engine.SetVolume(100); engine.PlayFile(path);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8)); var status = engine.ExtendedEqualizerStatus;
        if (effects.Active != AudioEffect.None || meters) Check(status is { Active: true, CurrentSettingsApplied: true, Error: 0, Channels: 2 }, "Actual native DSP acknowledgement: " + status);
        engine.Stop(); return (samples, status);
    }
    internal static double Rms(float[] samples, int channel, double from = .2, double to = .9)
    { double sum = 0; int count = 0; for (int frame = (int)(Rate * from); frame < Math.Min(samples.Length / 2, Rate * to); frame++) { double value = samples[frame * 2 + channel]; sum += value * value; count++; } return Math.Sqrt(sum / Math.Max(1, count)); }
    public static Task Validation()
    {
        new AudioEffects().Validate();
        foreach (var bad in new[] { new AudioEffects { Ratio = float.NaN }, new AudioEffects { Bands = ImmutableArray<ParametricBand>.Empty }, new AudioEffects { ChannelDelays = ImmutableArray.CreateRange(Enumerable.Repeat(201f, 8)) }, new AudioEffects { BassDb = 13 } })
        { bool refused = false; try { bad.Validate(); } catch (ArgumentException) { refused = true; } Check(refused, "Invalid settings must fail before changing the audio pipeline"); }
        return Task.CompletedTask;
    }
    public static async Task Native(string root)
    {
        string folder = Path.Combine(root, "advanced-dsp-" + Guid.NewGuid().ToString("N"));
        string sine = Wave(folder, "mono-tone", (frame, _) => .2 * Math.Sin(2 * Math.PI * 1000 * frame / Rate));
        string loud = Wave(folder, "loud-tone", (frame, _) => .7 * Math.Sin(2 * Math.PI * 1000 * frame / Rate));
        string left = Wave(folder, "left-tone", (frame, channel) => channel == 0 ? .2 * Math.Sin(2 * Math.PI * 440 * frame / Rate) : 0);
        string bass = Wave(folder, "bass-high", (frame, channel) => .15 * Math.Sin(2 * Math.PI * (channel == 0 ? 50 : 4000) * frame / Rate));
        string impulse = Wave(folder, "impulse", (frame, _) => frame >= 9600 && frame < 9696 ? .5 * Math.Sin(2 * Math.PI * 1000 * (frame - 9600) / Rate) : 0);
        var neutral = (await Capture(sine, new())).Samples; double reference = Rms(neutral, 0);
        AudioEffects Fx(AudioEffect effect) => new() { Enabled = true, Effects = effect };
        var settings = Fx(AudioEffect.Parametric); settings = settings with { Bands = settings.Bands.SetItem(0, new(1000, 6, 1)) };
        var parametric = (await Capture(sine, settings)).Samples; Check(Rms(parametric, 0) / reference is > 1.8 and < 2.2, $"Parametric peak changes captured PCM by approximately +6 dB (reference={reference}, processed={Rms(parametric, 0)}, ratio={Rms(parametric, 0) / reference})");
        var low = (await Capture(bass, Fx(AudioEffect.Bass))).Samples; var lowFlat = (await Capture(bass, new())).Samples;
        Check(Rms(low, 0) / Rms(lowFlat, 0) > 1.7 && Rms(low, 1) / Rms(lowFlat, 1) is > .98 and < 1.04, "Bass shelf boosts lows while retaining high-frequency level");
        var crossed = (await Capture(left, Fx(AudioEffect.Crossfeed))).Samples; Check(Rms(crossed, 1) > .01 && Rms(crossed, 0) > Rms(crossed, 1), "Crossfeed sends a filtered signal to the opposite ear");
        var compressed = (await Capture(loud, Fx(AudioEffect.Compressor) with { ThresholdDb = -24, Ratio = 8, AttackMs = 1, ReleaseMs = 50 })).Samples;
        Check(Rms(compressed, 0) / (reference * 3.5) < .3, "Linked compressor reduces loud-tone amplitude");
        var limited = (await Capture(loud, Fx(AudioEffect.Limiter) with { LimiterDb = -12 })).Samples; Check(limited.Max(value => Math.Abs(value)) <= Math.Pow(10, -12d / 20) + .002, "Peak limiter enforces its ceiling on real PCM");
        var balanced = (await Capture(sine, Fx(AudioEffect.Balance) with { Balance = -1 })).Samples; Check(Rms(balanced, 0) / reference > .98 && Rms(balanced, 1) < .0001, "Balance attenuates only the requested side");
        var channel = (await Capture(sine, Fx(AudioEffect.ChannelGain) with { ChannelGains = new AudioEffects().ChannelGains.SetItem(0, -12) })).Samples;
        Check(Rms(channel, 0) / reference is > .24 and < .26 && Rms(channel, 1) / reference is > .98 and < 1.02, "Individual channel gain preserves the other channel");
        var delayed = (await Capture(impulse, Fx(AudioEffect.ChannelDelay) with { ChannelDelays = new AudioEffects().ChannelDelays.SetItem(0, 100) })).Samples;
        Check(Rms(delayed, 0, .299, .304) > .05 && Rms(delayed, 1, .199, .204) > .05 && Rms(delayed, 0, .199, .204) < .001, "Channel delay shifts the left impulse by 100 ms");
        var wide = (await Capture(left, Fx(AudioEffect.Width) with { Width = 2 })).Samples; Check(Rms(wide, 1) > .05 && Rms(wide, 0) > Rms(wide, 1) * 2.8, "Stereo width changes the side component");
        var echo = (await Capture(impulse, Fx(AudioEffect.Echo) with { EchoMs = 250, EchoMix = .5f })).Samples; Check(Rms(echo, 0, .449, .455) > .025, "Echo produces the delayed impulse");
        var reverb = (await Capture(impulse, Fx(AudioEffect.Reverb) with { ReverbMix = .8f })).Samples; Check(Rms(reverb, 0, .226, .246) > .005, "Reverb produces a multi-tap decay tail");
        foreach (var effect in new[] { AudioEffect.Flanger, AudioEffect.Chorus })
        {
            var processed = (await Capture(sine, Fx(effect))).Samples; double difference = Math.Sqrt(processed.Select((value, index) => Math.Pow(value - neutral[index], 2)).Average()); Check(difference > .025 && processed.All(float.IsFinite), effect + " changes real PCM without non-finite samples");
        }
        var voice = (await Capture(sine, Fx(AudioEffect.VoiceReduction))).Samples; Check(Rms(voice, 0) < .0001 && Rms(voice, 1) < .0001, "Center reduction cancels a correlated center signal");
        var metered = await Capture(loud, new(), true); Check(metered.Status is { InputPeak: > .5f, OutputPeak: > .5f, InputRms: > .1f, OutputRms: > .1f }, "Signal-path meters measure actual native input/output blocks");
        File.WriteAllText(Path.Combine(folder, "meter-snapshot.json"), System.Text.Json.JsonSerializer.Serialize(metered.Status));
        await LiveUpdates(folder, sine);
        Console.WriteLine("Measured 14 native effects and live input/output meters against PCM fixtures.");
    }
    private static async Task LiveUpdates(string folder, string source)
    {
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        string profile = Path.Combine(folder, "live-settings"); var store = new SettingsService(profile); var original = new AppSettings { Volume = 0, AutoPlay = false };
        Check(store.Save(original), "Initial settings publication"); await using var library = new LibraryService(Path.Combine(profile, "library")); var vm = new MainViewModel(engine, store, original, library);
        async Task Wait(Func<bool> predicate) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!predicate()) { if (watch.ElapsedMilliseconds > 8000) throw new TimeoutException("Live DSP state: " + engine.ExtendedEqualizerStatus); await Task.Delay(25); } }
        try
        {
            engine.SetVolume(0); engine.PlayFile(source); await Wait(() => engine.IsPlaying && engine.GetTime() > 100); engine.MediaPlayer.SetPause(true); await Wait(() => engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused);
            var fx = new AudioEffects { Enabled = true, Effects = AudioEffect.Bass | AudioEffect.Limiter, BassDb = 3 };
            long position = engine.GetTime(); vm.CommitAudioEffects(fx); Check(engine.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused && Math.Abs(engine.GetTime() - position) < 100, "Activating effects retains paused transport and position");
            Check(store.Load().AudioEffects.SameAs(fx), "Every audio-effect value survives settings round trip");
            engine.MediaPlayer.SetPause(false); await Wait(() => engine.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true, Error: 0, Effects: AudioEffect.Bass | AudioEffect.Limiter });
            engine.ApplyEqualizer(new float[31], -3, true); fx = fx with { BassDb = 5, Effects = fx.Effects | AudioEffect.Crossfeed }; vm.CommitAudioEffects(fx);
            await Wait(() => engine.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true } status && status.Effects.HasFlag(AudioEffect.Crossfeed));
            using (var locked = new FileStream(Path.Combine(profile, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            { bool refused = false; try { vm.CommitAudioEffects(fx with { BassDb = -4 }); } catch (IOException) { refused = true; } Check(refused && engine.CurrentAudioEffects.SameAs(fx), "Failed preference save cannot alter live audio effects"); }
            vm.CommitAudioEffects(fx with { Enabled = false }); await Wait(() => engine.ExtendedEqualizerStatus is { Active: true, CurrentSettingsApplied: true, Effects: AudioEffect.None });
            Check(engine.IsPlaying && engine.GetVolume() == 0, "Bypassing effects retains graphic EQ, transport and volume");
        }
        finally { engine.Stop(); await vm.ShutdownAsync(); }
    }
}
