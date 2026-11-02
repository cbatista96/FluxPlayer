using System.Collections.Immutable;

namespace FluxPlayer.Models;

[Flags]
public enum AudioEffect
{
    None = 0, Parametric = 1, Bass = 2, Crossfeed = 4, Compressor = 8, Limiter = 16, Balance = 32,
    ChannelGain = 64, ChannelDelay = 128, Width = 256, Echo = 512, Reverb = 1024, Flanger = 2048, Chorus = 4096, VoiceReduction = 8192, Meters = 16384, Matrix = 32768, Leveling = 65536
}
public sealed record ParametricBand(float Frequency, float GainDb = 0, float Q = 1, bool Enabled = true);
public sealed record AudioEffects
{
    public bool Enabled { get; init; }
    public AudioEffect Effects { get; init; }
    public float BassDb { get; init; } = 6;
    public float BassHz { get; init; } = 120;
    public float Crossfeed { get; init; } = .25f;
    public float CrossfeedHz { get; init; } = 700;
    public float ThresholdDb { get; init; } = -18;
    public float Ratio { get; init; } = 4;
    public float AttackMs { get; init; } = 10;
    public float ReleaseMs { get; init; } = 150;
    public float MakeupDb { get; init; }
    public float LimiterDb { get; init; } = -1;
    public float Balance { get; init; }
    public float Width { get; init; } = 1.3f;
    public float EchoMs { get; init; } = 250;
    public float EchoFeedback { get; init; } = .3f;
    public float EchoMix { get; init; } = .25f;
    public float ReverbMix { get; init; } = .2f;
    public float ReverbDecay { get; init; } = .45f;
    public float FlangerMix { get; init; } = .3f;
    public float FlangerRate { get; init; } = .3f;
    public float FlangerDepthMs { get; init; } = 3;
    public float ChorusMix { get; init; } = .25f;
    public float ChorusRate { get; init; } = .8f;
    public float ChorusDepthMs { get; init; } = 8;
    public float LevelTargetDb { get; init; } = -18;
    public float LevelMaxGainDb { get; init; } = 12;
    public float LevelResponseMs { get; init; } = 500;
    public ImmutableArray<float> Matrix { get; init; } = Enumerable.Range(0, 64).Select(index => index % 9 == 0 ? 1f : 0f).ToImmutableArray();
    public ImmutableArray<float> ChannelGains { get; init; } = ImmutableArray.CreateRange(new float[8]);
    public ImmutableArray<float> ChannelDelays { get; init; } = ImmutableArray.CreateRange(new float[8]);
    public ImmutableArray<ParametricBand> Bands { get; init; } = new float[] { 31.25f, 62.5f, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }.Select(frequency => new ParametricBand(frequency)).ToImmutableArray();
    public AudioEffect Active => Enabled ? Effects : AudioEffect.None;
    public float[] NativeParameters => new[] { BassDb, BassHz, Crossfeed, ThresholdDb, Ratio, AttackMs, ReleaseMs, MakeupDb, LimiterDb, Balance, Width,
        EchoMs, EchoFeedback, EchoMix, ReverbMix, ReverbDecay, FlangerMix, FlangerRate, FlangerDepthMs, ChorusMix, ChorusRate, ChorusDepthMs, CrossfeedHz, 0 };
    public void Validate()
    {
        if (((int)Effects & ~131071) != 0 || ChannelGains.IsDefault || ChannelGains.Length != 8 || ChannelDelays.IsDefault || ChannelDelays.Length != 8 || Bands.IsDefault || Bands.Length != 10 || Matrix.IsDefault || Matrix.Length != 64) throw new ArgumentException("Use valid effects, eight decoded channels, an 8×8 matrix and ten parametric bands.");
        if (!float.IsFinite(LevelTargetDb) || LevelTargetDb is < -30 or > -6 || !float.IsFinite(LevelMaxGainDb) || LevelMaxGainDb is < 0 or > 18 || !float.IsFinite(LevelResponseMs) || LevelResponseMs is < 100 or > 5000 || Matrix.Any(value => !float.IsFinite(value) || value is < -2 or > 2) || Enumerable.Range(0, 8).Any(row => Matrix.Skip(row * 8).Take(8).Sum(value => Math.Abs(value)) > 4.001)) throw new ArgumentException("Use bounded RMS-leveling values and matrix coefficients −2…2 with absolute row sum at most 4.");
        var limits = new (float Min, float Max)[] { (-12, 12), (30, 500), (0, 1), (-60, 0), (1, 20), (.1f, 200), (10, 2000), (0, 18), (-18, 0), (-1, 1), (0, 2), (10, 1500), (0, .8f), (0, 1), (0, 1), (0, .75f), (0, 1), (.1f, 5), (1, 8), (0, 1), (.1f, 5), (2, 20), (300, 2000), (0, 0) };
        var parameters = NativeParameters;
        if (parameters.Where((value, index) => !float.IsFinite(value) || value < limits[index].Min || value > limits[index].Max).Any()
            || ChannelGains.Any(value => !float.IsFinite(value) || value is < -24 or > 12) || ChannelDelays.Any(value => !float.IsFinite(value) || value is < 0 or > 200)
            || Bands.Any(band => band == null || !float.IsFinite(band.Frequency) || band.Frequency is < 20 or > 20000 || !float.IsFinite(band.GainDb) || band.GainDb is < -18 or > 18 || !float.IsFinite(band.Q) || band.Q is < .1f or > 12)) throw new ArgumentException("An audio effect value is outside its supported range.");
    }
    public bool SameAs(AudioEffects other) => Enabled == other.Enabled && Effects == other.Effects && NativeParameters.SequenceEqual(other.NativeParameters) && ChannelGains.SequenceEqual(other.ChannelGains) && ChannelDelays.SequenceEqual(other.ChannelDelays) && Bands.SequenceEqual(other.Bands) && Matrix.SequenceEqual(other.Matrix) && LevelTargetDb == other.LevelTargetDb && LevelMaxGainDb == other.LevelMaxGainDb && LevelResponseMs == other.LevelResponseMs;
}
