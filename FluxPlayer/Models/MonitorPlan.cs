using System.Globalization;
using System.Windows;

namespace FluxPlayer.Models;

/// <summary>Choices about which monitor the player uses, keeping its window reachable, and the colour behind the video. Pure so it can be tested without a display.</summary>
public static class MonitorPlan
{
    public static readonly string[] FullscreenModes = ["Current", "Primary", "Other"];
    public const int MinimumLimitPercent = 50, MaximumLimitPercent = 100, DefaultLimitPercent = 97;

    public static string NormalizeFullscreenMode(string? mode) =>
        FullscreenModes.FirstOrDefault(item => string.Equals(item, mode, StringComparison.OrdinalIgnoreCase)) ?? "Current";

    public static int NormalizeLimitPercent(int percent) => Math.Clamp(percent, MinimumLimitPercent, MaximumLimitPercent);

    /// <summary>"#RRGGBB" only (so the setting can never hold anything else); <paramref name="fallback"/> otherwise.</summary>
    public static string NormalizeColor(string? color, string fallback = "#000000")
    {
        string text = (color ?? "").Trim();
        return text.Length == 7 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit) ? text.ToUpperInvariant() : fallback;
    }

    public static bool TryParseColor(string? color, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        string text = NormalizeColor(color, "");
        if (text.Length != 7) return false;
        r = byte.Parse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        g = byte.Parse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        b = byte.Parse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Which monitor fullscreen opens on: the one the window is on, the primary, or the next one (wrapping) when there is more than one.</summary>
    public static int ChooseFullscreenMonitor(string mode, int current, int primary, int count)
    {
        if (count <= 1 || current < 0 || current >= count) return Math.Clamp(current, 0, Math.Max(0, count - 1));
        return NormalizeFullscreenMode(mode) switch
        {
            "Primary" => primary >= 0 && primary < count ? primary : current,
            "Other" => (current + 1) % count,
            _ => current
        };
    }

    /// <summary>
    /// Whether enough of the title bar is on a monitor to grab it: at least <paramref name="grab"/> px of its width and its top edge inside a work area.
    /// </summary>
    public static bool IsReachable(Rect window, IReadOnlyList<Rect> workAreas, double grab = 120)
    {
        double needed = Math.Min(grab, window.Width);
        foreach (var area in workAreas)
        {
            double overlap = Math.Min(window.Right, area.Right) - Math.Max(window.Left, area.Left);
            if (overlap >= needed && window.Top >= area.Top - 1 && window.Top <= area.Bottom - 40) return true;
        }
        return false;
    }

    /// <summary>Moves a window that is no longer reachable fully onto the work area it overlaps most (or the first), shrinking it if it is larger.</summary>
    public static Rect KeepOnScreen(Rect window, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0 || IsReachable(window, workAreas)) return window;
        Rect best = workAreas[0]; double bestOverlap = double.NegativeInfinity;
        foreach (var area in workAreas)
        {
            var overlap = Rect.Intersect(window, area);
            double size = overlap.IsEmpty ? -DistanceSquared(window, area) : overlap.Width * overlap.Height;
            if (size > bestOverlap) { bestOverlap = size; best = area; }
        }
        double width = Math.Min(window.Width, best.Width), height = Math.Min(window.Height, best.Height);
        return new Rect(Math.Clamp(window.Left, best.Left, best.Right - width), Math.Clamp(window.Top, best.Top, best.Bottom - height), width, height);
    }

    private static double DistanceSquared(Rect a, Rect b)
    {
        double dx = (a.Left + a.Width / 2) - (b.Left + b.Width / 2), dy = (a.Top + a.Height / 2) - (b.Top + b.Height / 2);
        return dx * dx + dy * dy;
    }
}

/// <summary>EXIF orientation (1–8) as the rotation and mirroring that show a photo upright: mirror first, then rotate clockwise.</summary>
public static class ExifOrientation
{
    public static (int Rotation, bool Mirror) ToTransform(int orientation) => orientation switch
    {
        2 => (0, true), 3 => (180, false), 4 => (180, true), 5 => (270, true), 6 => (90, false), 7 => (90, true), 8 => (270, false), _ => (0, false)
    };
}
