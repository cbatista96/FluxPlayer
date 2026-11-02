using FluxPlayer.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FluxPlayer.Services;

public static class SpectrumDrawing
{
    public static DrawingImage Create(SpectrumInspection result)
    {
        var group = new DrawingGroup(); using (var drawing = group.Open())
        {
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(55, 160, 160, 160)), 1);
            var line = new Pen(new SolidColorBrush(Color.FromRgb(76, 194, 245)), 1.5);
            var text = new SolidColorBrush(Color.FromRgb(190, 194, 202));
            void Label(string value, double x, double y) => drawing.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, text, 1), new(x, y));
            const double left = 60, width = 800, top = 34, height = 160;
            Label("Peak envelope · first " + result.Signature.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s", left, 4);
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 900, 460));
            drawing.DrawLine(grid, new(left, top + height / 2), new(left + width, top + height / 2));
            for (int i = 0; i < result.Waveform.Length; i++) { double x = left + i * width / Math.Max(1, result.Waveform.Length - 1), amplitude = Math.Min(1, result.Waveform[i]) * height / 2; drawing.DrawLine(line, new(x, top + height / 2 - amplitude), new(x, top + height / 2 + amplitude)); }
            Label("0 s", left, top + height + 3); Label(result.Signature.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s", left + width - 48, top + height + 3);
            const double spectrumTop = 260, spectrumHeight = 160;
            Label("Averaged Hann FFT · logarithmic frequency · relative bin power", left, 230);
            foreach (int db in new[] { 0, -30, -60, -90, -120 }) { double y = spectrumTop - db / 120d * spectrumHeight; drawing.DrawLine(grid, new(left, y), new(left + width, y)); Label(db.ToString(CultureInfo.InvariantCulture), 20, y - 8); }
            double X(double hz) => left + Math.Log(hz / 32) / Math.Log((SonicAnalysis.SampleRate / 2d) / 32) * width;
            foreach (int hz in new[] { 50, 100, 250, 500, 1000, 2000, 5000 }) { double x = X(hz); drawing.DrawLine(grid, new(x, spectrumTop), new(x, spectrumTop + spectrumHeight)); Label(hz >= 1000 ? (hz / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k" : hz.ToString(CultureInfo.InvariantCulture), x - 10, spectrumTop + spectrumHeight + 6); }
            var geometry = new StreamGeometry(); using (var path = geometry.Open())
            {
                bool first = true;
                for (int i = 1; i < result.SpectrumDb.Length; i++)
                {
                    double hz = i * (double)SonicAnalysis.SampleRate / SonicAnalysis.FftSize; if (hz < 32) continue;
                    var point = new Point(X(hz), spectrumTop + Math.Clamp(-result.SpectrumDb[i] / 120, 0, 1) * spectrumHeight);
                    if (first) { path.BeginFigure(point, false, false); first = false; } else path.LineTo(point, true, false);
                }
            }
            geometry.Freeze(); drawing.DrawGeometry(null, line, geometry);
        }
        group.Freeze(); var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
