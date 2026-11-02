using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record PlayerEnhancements
{
    public bool SmartShuffle { get; init; }
    public bool RelatedAutoplay { get; init; }
    public bool MouseGestures { get; init; }
    public bool TouchGestures { get; init; }
    public HudLayout Hud { get; init; } = new();
    public FluxPlayer.Services.GpuRenderPolicy GpuPolicy { get; init; } = new();
    public bool OsdEnabled { get; init; } = true;
    public double OsdFontSize { get; init; } = 17;
    public int OsdMilliseconds { get; init; } = 1600;
    public string OsdPosition { get; init; } = "Center";
    public string OsdColor { get; init; } = "#FFF2F2F2";
    public ImmutableArray<string> ToolbarOrder { get; init; } = ["Subtitles", "Picture", "Loops", "Capture"];
    public ImmutableArray<string> HiddenToolbar { get; init; } = [];
    public ImmutableDictionary<string, string> GlobalHotkeys { get; init; } = ImmutableDictionary<string, string>.Empty;
    public void Validate()
    {
        if (Hud == null) throw new ArgumentException("HUD preferences are required.");
        Hud.Validate();
        if (GpuPolicy == null) throw new ArgumentException("GPU policy is required.");
        GpuPolicy.Validate();
        string[] groups = ["Subtitles", "Picture", "Loops", "Capture"];
        if (!double.IsFinite(OsdFontSize) || OsdFontSize is < 12 or > 32 || OsdMilliseconds is < 400 or > 10000 || OsdPosition is not ("Center" or "Top" or "Bottom") || OsdColor == null || OsdColor.Length != 9 || OsdColor[0] != '#' || !OsdColor.AsSpan(1).ToArray().All(Uri.IsHexDigit)
            || ToolbarOrder.IsDefault || ToolbarOrder.Length != 4 || ToolbarOrder.Distinct().Count() != 4 || ToolbarOrder.Any(item => !groups.Contains(item)) || HiddenToolbar.IsDefault || HiddenToolbar.Length > 4 || HiddenToolbar.Any(item => !groups.Contains(item)) || GlobalHotkeys == null || GlobalHotkeys.Count > 6) throw new ArgumentException("Invalid player presentation preferences.");
        if (GlobalHotkeys.Keys.Any(key => !FluxPlayer.Services.GlobalHotkeys.Actions.Contains(key))) throw new ArgumentException("Unknown global transport action.");
        try { if (GlobalHotkeys.Values.Select(FluxPlayer.Services.GlobalHotkeys.Parse).Distinct().Count() != GlobalHotkeys.Count) throw new ArgumentException("Global shortcuts must be unique."); } catch (Exception ex) when (ex is NotSupportedException or FormatException) { throw new ArgumentException("Invalid global shortcut.", ex); }
    }
}
public static class CustomAspect
{
    public static string Parse(string input)
    {
        var parts = input.Trim().Split(':'); if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) || x is < 1 or > 10000 || y is < 1 or > 10000 || (double)x / y is < .1 or > 10) throw new ArgumentException("Enter an aspect ratio X:Y (1–10,000 per side; ratio 0.1–10).");
        int a = x, b = y; while (b != 0) (a, b) = (b, a % b); return $"{x / a}:{y / a}";
    }
}
