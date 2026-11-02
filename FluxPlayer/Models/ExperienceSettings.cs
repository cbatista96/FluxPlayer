namespace FluxPlayer.Models;

public sealed record ExperienceSettings(string Accent = "", string Background = "", double BackgroundOpacity = .2, bool DockEqualizer = false, string DockSide = "Right")
{
    public void Validate()
    {
        if (Accent == null || Accent != "" && (Accent.Length != 7 || Accent[0] != '#' || !Accent.AsSpan(1).ToArray().All(Uri.IsHexDigit)) || Background == null || Background != "" && !System.IO.Path.IsPathFullyQualified(Background) || !double.IsFinite(BackgroundOpacity) || BackgroundOpacity is < .05 or > .5 || DockSide is not ("Left" or "Right")) throw new ArgumentException("Use #RRGGBB or default accent, a local background and opacity 0.05–0.5.");
    }
}
