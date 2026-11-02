namespace FluxPlayer.Models;

public sealed record FirDesign(string Mode = "Low-pass", int SampleRate = 48000, int Taps = 511, double LowHz = 100, double HighHz = 8000);
public sealed record AudioLabOptions
{
    public int SampleRate { get; init; } = 48000;
    public int Bits { get; init; } = 24;
    public string Resampler { get; init; } = "Kaiser";
    public string Dither { get; init; } = "None";
    public string NoiseShape { get; init; } = "None";
    public double PitchSemitones { get; init; }
    public double Tempo { get; init; } = 1;
    public bool RemoveDc { get; init; }
    public bool RestoreRiaa { get; init; }
    public string Impulse { get; init; } = "";
    public double ImpulseGain { get; init; } = 1;
    public bool NormalizePeak { get; init; }
    public double TargetPeakDb { get; init; } = -1;
}
public sealed record VideoLabOptions
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool ReduceOnly { get; init; } = true;
    public string Kernel { get; init; } = "Bicubic";
    public double Deband { get; init; }
    public double Denoise { get; init; }
    public bool Stabilize { get; init; }
    public int InterpolateFps { get; init; }
    public string ToneMap { get; init; } = "None";
    public string HdrTransfer { get; init; } = "PQ";
    public double TargetNits { get; init; } = 100;
    public double SourcePeakNits { get; init; } = 1000;
    public string InputRange { get; init; } = "Auto";
    public string OutputRange { get; init; } = "Limited";
    public string OverlayImage { get; init; } = "";
    public string OverlayText { get; init; } = "";
    public int OverlayX { get; init; } = 24;
    public int OverlayY { get; init; } = 24;
    public int OverlayWidth { get; init; } = 300;
}
