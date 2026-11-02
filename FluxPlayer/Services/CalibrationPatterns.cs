using System.Windows;
using System.Windows.Media;

namespace FluxPlayer.Services;

/// <summary>Static SDR code-value patterns. No animation, monitor settings or color certification.</summary>
public static class CalibrationPatterns
{
    public static readonly string[] Names = ["Color bars", "Grayscale steps", "Near black", "Near white", "Geometry grid", "White Wash"];
    public static DrawingImage Create(string name, double width, double height)
    {
        if (!Names.Contains(name) || !double.IsFinite(width) || !double.IsFinite(height) || width is < 1 or > 10000 || height is < 1 or > 10000) throw new ArgumentException("Choose a known pattern and dimensions 1–10,000.");
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            if (name == "White Wash") context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            else if (name == "Geometry grid")
            {
                var pen = new Pen(Brushes.White, 1);
                for (int index = 0; index <= 16; index++) context.DrawLine(pen, new Point(index * width / 16, 0), new Point(index * width / 16, height));
                for (int index = 0; index <= 9; index++) context.DrawLine(pen, new Point(0, index * height / 9), new Point(width, index * height / 9));
                double radius = Math.Min(width, height) * .4;
                context.DrawEllipse(null, new Pen(Brushes.Yellow, 2), new Point(width / 2, height / 2), radius, radius);
            }
            else
            {
                Color[] colors = name switch
                {
                    "Color bars" => [Colors.White, Colors.Yellow, Colors.Cyan, Colors.Green, Colors.Magenta, Colors.Red, Colors.Blue, Colors.Black],
                    "Near black" => new byte[] { 0, 4, 8, 12, 16, 20, 24, 32 }.Select(value => Color.FromRgb(value, value, value)).ToArray(),
                    "Near white" => new byte[] { 224, 232, 240, 244, 248, 250, 252, 255 }.Select(value => Color.FromRgb(value, value, value)).ToArray(),
                    _ => Enumerable.Range(0, 21).Select(index => (byte)Math.Round(index * 255d / 20)).Select(value => Color.FromRgb(value, value, value)).ToArray()
                };
                for (int index = 0; index < colors.Length; index++) context.DrawRectangle(new SolidColorBrush(colors[index]), null, new Rect(index * width / colors.Length, 0, width / colors.Length + .01, height));
            }
        }
        drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
