namespace FluxPlayer.Models;

public enum StereoEye { FullPicture, SideBySideLeft, SideBySideRight, TopBottomTop, TopBottomBottom }
public sealed record VideoViewport(double Zoom = 1, double PanX = 0, double PanY = 0, StereoEye Eye = StereoEye.FullPicture, bool HalfResolution = true)
{
    public VideoCrop Rectangle(uint width, uint height)
    {
        if (width is < 2 or > 32768 || height is < 2 or > 32768 || !double.IsFinite(Zoom) || Zoom is < 1 or > 8 || !double.IsFinite(PanX) || PanX is < -1 or > 1 || !double.IsFinite(PanY) || PanY is < -1 or > 1 || !Enum.IsDefined(Eye)) throw new ArgumentException("Open a video and choose zoom 1–8 with pan in −1…1.");
        int left = 0, top = 0, regionWidth = (int)width, regionHeight = (int)height;
        if (Eye is StereoEye.SideBySideLeft or StereoEye.SideBySideRight) { regionWidth /= 2; if (Eye == StereoEye.SideBySideRight) left = regionWidth; }
        if (Eye is StereoEye.TopBottomTop or StereoEye.TopBottomBottom) { regionHeight /= 2; if (Eye == StereoEye.TopBottomBottom) top = regionHeight; }
        int croppedWidth = Math.Max(1, (int)Math.Round(regionWidth / Zoom)), croppedHeight = Math.Max(1, (int)Math.Round(regionHeight / Zoom));
        left += (int)Math.Round((regionWidth - croppedWidth) * (PanX + 1) / 2); top += (int)Math.Round((regionHeight - croppedHeight) * (PanY + 1) / 2);
        var crop = new VideoCrop(left, top, croppedWidth, croppedHeight); crop.Validate(width, height); return crop;
    }
    public (double X, double Y) Stretch => !HalfResolution ? (1, 1) : Eye switch { StereoEye.SideBySideLeft or StereoEye.SideBySideRight => (2, 1), StereoEye.TopBottomTop or StereoEye.TopBottomBottom => (1, 2), _ => (1, 1) };
}
public sealed record VideoPresentation(VideoViewport View, int Rotation, bool MirrorHorizontal, bool FlipVertical, string? Aspect = null)
{
    public bool Transformed => Rotation != 0 || MirrorHorizontal || FlipVertical || View.Stretch != (1d, 1d) || Aspect != null;
    public (double X, double Y) Stretch(int width, int height) { if (Aspect == null) return View.Stretch; var parts = Aspect.Split(':'); double ratio = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) / double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); return (ratio * height / width, 1); }
}
