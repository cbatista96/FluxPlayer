namespace FluxPlayer.Models;

public sealed record VideoFinishingOptions
{
    public string Deinterlace { get; init; } = "None";
    public string FieldOrder { get; init; } = "Auto";
    public string StereoInput { get; init; } = "None";
    public string StereoOutput { get; init; } = "Left eye";
    public int CropLeft { get; init; }
    public int CropRight { get; init; }
    public int CropTop { get; init; }
    public int CropBottom { get; init; }
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public int X { get; init; }
    public int Y { get; init; }
    public int SarNumerator { get; init; } = 1;
    public int SarDenominator { get; init; } = 1;
    public double[] RgbMatrix { get; init; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    public bool Bt2020SdrTo709 { get; init; }
    public bool Legalize { get; init; }
    public string Lut { get; init; } = "";
    public string LutInterpolation { get; init; } = "Tetrahedral";
}
public sealed record LuminanceReport(string Source, long Bytes, long ModifiedUtcTicks, int Samples, double MinimumFrameAverage, double MeanFrameAverage, double MaximumFrameAverage, string Scope);
