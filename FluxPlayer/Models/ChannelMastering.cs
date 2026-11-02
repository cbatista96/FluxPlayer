namespace FluxPlayer.Models;

public sealed record ChannelMasteringOptions
{
    public string InputLayout { get; init; } = "stereo";
    public string OutputLayout { get; init; } = "stereo";
    public int SampleRate { get; init; } = 48000;
    public int Bits { get; init; } = 24;
    public double[][] Matrix { get; init; } = [[1, 0], [0, 1]];
    public double[] GainDb { get; init; } = [0, 0];
    public int[] DelayMs { get; init; } = [0, 0];
    public int[] Order { get; init; } = [0, 1];
    public bool BassManagement { get; init; }
    public double CrossoverHz { get; init; } = 80;
    public double HeadroomDb { get; init; } = -6;
    public double SecondaryGain { get; init; } = .5;
}
