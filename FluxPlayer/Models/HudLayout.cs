using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record HudWidget(string Kind, double X, double Y, double Width, double Height, bool Visible = true);
public sealed record HudLayout
{
    public bool Enabled { get; init; }
    public bool AutoHide { get; init; } = true;
    public double FontSize { get; init; } = 16;
    public ImmutableArray<HudWidget> Widgets { get; init; } = [new("Title", .05, .05, 380, 40), new("Time", .05, .75, 220, 40), new("Seek", .05, .85, 480, 40), new("Previous", .3, .95, 90, 40), new("PlayPause", .45, .95, 110, 40), new("Next", .6, .95, 90, 40), new("Stop", .75, .95, 80, 40), new("Mute", .9, .8, 80, 40), new("Volume", .9, .9, 160, 40), new("Fullscreen", .95, .05, 100, 40)];
    public static readonly string[] Kinds = ["Title", "Time", "Seek", "Previous", "PlayPause", "Next", "Stop", "Mute", "Volume", "Fullscreen"];
    public void Validate()
    {
        if (!double.IsFinite(FontSize) || FontSize is < 12 or > 32 || Widgets.IsDefault || Widgets.Length > 40 || Widgets.Any(w => w == null || !Kinds.Contains(w.Kind) || new[] { w.X, w.Y, w.Width, w.Height }.Any(n => !double.IsFinite(n)) || w.X is < 0 or > 1 || w.Y is < 0 or > 1 || w.Width is < 40 or > 1200 || w.Height is < 32 or > 300)) throw new ArgumentException("HUD widgets need known types, positions 0–1, width 40–1200, height 32–300 and text size 12–32.");
    }
    public static (double Left, double Top, double Width, double Height) Place(HudWidget widget, double width, double height)
    {
        double w = Math.Min(widget.Width, Math.Max(0, width)), h = Math.Min(widget.Height, Math.Max(0, height));
        return (Math.Max(0, width - w) * widget.X, Math.Max(0, height - h) * widget.Y, w, h);
    }
}
