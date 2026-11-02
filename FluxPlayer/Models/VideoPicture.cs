namespace FluxPlayer.Models;

public sealed record VideoPicture(float Brightness = 1, float Contrast = 1, float Saturation = 1, int Hue = 0, float Gamma = 1)
{
    public bool Neutral => this == new VideoPicture();
    public bool IsValid => new[] { Brightness, Contrast, Saturation, Gamma }.All(float.IsFinite)
        && Brightness is >= 0 and <= 2 && Contrast is >= 0 and <= 2 && Saturation is >= 0 and <= 2 && Hue is >= 0 and <= 360 && Gamma is >= .1f and <= 2;
    public static VideoPicture FromSettings(AppSettings s) => new(Math.Clamp(1 + s.Brightness / 100f, 0, 2), Math.Clamp(1 + s.Contrast / 100f, 0, 2), Math.Clamp(1 + s.Saturation / 100f, 0, 2), (s.Hue % 360 + 360) % 360, Math.Clamp(1 + s.Gamma / 100f, .1f, 2));
}
public sealed record VideoCrop(int Left, int Top, int Width, int Height)
{
    public string Geometry => FormattableString.Invariant($"{Width}x{Height}+{Left}+{Top}");
    public void Validate(uint sourceWidth, uint sourceHeight)
    {
        if (Left < 0 || Top < 0 || Width < 1 || Height < 1 || (long)Left + Width > sourceWidth || (long)Top + Height > sourceHeight) throw new ArgumentException("Choose a positive crop rectangle contained in the source picture.");
    }
}
