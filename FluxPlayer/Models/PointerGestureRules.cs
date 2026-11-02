namespace FluxPlayer.Models;

/// <summary>Actions a mouse button or wheel can trigger on the video, and the arithmetic of the drag gestures.</summary>
public static class PointerGestureRules
{
    public static readonly string[] ClickActions = ["PlayPause", "ToggleFullscreen", "ToggleMute", "SeekBack", "SeekForward", "None"];
    public static readonly string[] WheelActions = ["Volume", "Seek", "Speed", "None"];
    public static readonly string[] DragActions = ["None", "Pan", "Zoom", "SubtitlePosition"];

    public const float MinHoldRate = 1.25f, MaxHoldRate = 4f;
    public const int MinHoldDelayMs = 150, MaxHoldDelayMs = 2000, MaxSubtitleMargin = 400;

    public static string Normalize(string? value, string[] allowed, string fallback) =>
        allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    /// <summary>An empty side action means "same as the plain double-click".</summary>
    public static string NormalizeSideClick(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Normalize(value, ClickActions, "");

    /// <summary>0 turns the hold-to-change-speed gesture off; otherwise the rate is kept inside a range the player supports.</summary>
    public static float NormalizeHoldRate(float rate) =>
        !float.IsFinite(rate) || rate < 1 ? 0 : Math.Clamp(rate, MinHoldRate, MaxHoldRate);

    public static int NormalizeHoldDelay(int milliseconds) => Math.Clamp(milliseconds, MinHoldDelayMs, MaxHoldDelayMs);

    /// <summary>Which double-click action applies to a click at <paramref name="x"/> in a video <paramref name="width"/> wide: left third, right third, or the centre.</summary>
    public static string DoubleClickFor(AppSettings settings, double x, double width)
    {
        string plain = settings.DoubleClickAction;
        if (!(width > 0) || !double.IsFinite(x)) return plain;
        double position = x / width;
        string side = position < 1d / 3 ? settings.LeftDoubleClickAction : position > 2d / 3 ? settings.RightDoubleClickAction : "";
        return string.IsNullOrEmpty(side) ? plain : side;
    }

    public static string DragActionFor(AppSettings settings, bool ctrl, bool alt, bool shift) =>
        ctrl ? settings.CtrlDragAction : alt ? settings.AltDragAction : shift ? settings.ShiftDragAction : "None";

    /// <summary>Dragging the picture to the right reveals more of its left side. Panning only exists while zoomed in.</summary>
    public static double PanDelta(double dragPixels, double viewPixels, double zoom) =>
        !(viewPixels > 0) || zoom <= 1.001 ? 0 : -2 * dragPixels / viewPixels / (zoom - 1);

    /// <summary>Dragging up zooms in; 200 px is about one doubling.</summary>
    public static double ZoomAfterDrag(double zoom, double dragPixelsUp) =>
        Math.Clamp(zoom * Math.Pow(2, dragPixelsUp / 200), 1, 8);

    /// <summary>Dragging up moves the subtitles up (a bigger bottom margin).</summary>
    public static int SubtitleMarginAfterDrag(int margin, double dragPixelsUp) =>
        (int)Math.Clamp(Math.Round(margin + dragPixelsUp), 0, MaxSubtitleMargin);
}
