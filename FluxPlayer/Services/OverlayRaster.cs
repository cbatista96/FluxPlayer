using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

/// <summary>Render explicit caption text on the UI thread, keeping text out of FFmpeg expressions.</summary>
public static class OverlayRaster
{
    public static string CreateCaption(string text, int width)
    {
        if (Application.Current == null || !Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Create captions on the UI thread.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500 || width is < 64 or > 1024) throw new ArgumentException("Use 1–500 caption characters and a width of 64–1,024 pixels.");
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 26, Brushes.White, 1) { MaxTextWidth = width - 24, Trimming = TextTrimming.None };
        if (formatted.Height > 480) throw new ArgumentException("Shorten the caption or increase its width.");
        int height = Math.Max(48, (int)Math.Ceiling(formatted.Height + 24)); var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(190, 20, 22, 28)), null, new Rect(0, 0, width, height), 8, 8); drawing.DrawText(formatted, new Point(12, 12)); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        string path = Path.Combine(Path.GetTempPath(), "flux-caption-" + Guid.NewGuid().ToString("N") + ".png");
        try { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); encoder.Save(stream); return path; }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
}
