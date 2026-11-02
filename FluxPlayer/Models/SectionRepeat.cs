namespace FluxPlayer.Models;

/// <summary>Arithmetic for the A-B section repeat: how many times to repeat, moving the section, and the "repeat the last seconds" shortcut.</summary>
public static class SectionRepeat
{
    public const int MaxDelayMs = 10_000, MaxRepeats = 999, MinQuickSeconds = 1, MaxQuickSeconds = 120;

    public static int NormalizeDelay(int milliseconds) => Math.Clamp(milliseconds, 0, MaxDelayMs);
    public static int NormalizeRepeats(int count) => Math.Clamp(count, 0, MaxRepeats);
    public static int NormalizeQuickSeconds(int seconds) => Math.Clamp(seconds, MinQuickSeconds, MaxQuickSeconds);

    /// <summary>True when the section has already been played <paramref name="max"/> times; 0 means repeat forever.</summary>
    public static bool IsFinished(int repeatsDone, int max) => max > 0 && repeatsDone >= max;

    /// <summary>Moves the section to the one just before (-1) or after (+1) it, of the same length; null when there is no room.</summary>
    public static (long A, long B)? Shift(long a, long b, int direction, long length)
    {
        long span = b - a;
        if (a < 0 || span <= 0 || direction == 0) return null;
        long start = direction < 0 ? a - span : a + span;
        if (start < 0 || (length > 0 && start >= length)) return null;
        long end = start + span;
        if (length > 0 && end > length) end = length;
        return end > start ? (start, end) : null;
    }

    /// <summary>The section covering the last <paramref name="seconds"/> seconds up to <paramref name="now"/>; null when nothing has played yet.</summary>
    public static (long A, long B)? Last(long now, int seconds)
    {
        if (now <= 0) return null;
        long start = Math.Max(0, now - NormalizeQuickSeconds(seconds) * 1000L);
        return now > start ? (start, now) : null;
    }
}
