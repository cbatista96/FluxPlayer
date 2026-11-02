using System.Globalization;
using System.Text;

namespace FluxPlayer.Models;

/// <summary>How a position is written when it is copied to the clipboard or saved with bookmarks.</summary>
public static class TimeText
{
    public static readonly string[] Styles = ["Clock", "ClockMilliseconds", "Seconds"];

    public static string NormalizeStyle(string? style) =>
        Styles.FirstOrDefault(item => string.Equals(item, style, StringComparison.OrdinalIgnoreCase)) ?? "Clock";

    public static string Format(long milliseconds, string? style)
    {
        long ms = Math.Max(0, milliseconds);
        var span = TimeSpan.FromMilliseconds(ms);
        return NormalizeStyle(style) switch
        {
            "ClockMilliseconds" => $"{(long)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds:000}",
            "Seconds" => (ms / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
            _ => $"{(long)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
        };
    }
}

/// <summary>Bookmark lists as plain text (one time per line), plus the batch edits the bookmark window offers.</summary>
public static class BookmarkTools
{
    public const int MaximumBookmarks = 1000, MaximumFileBytes = 1024 * 1024;
    private const long MinimumGapMs = 1000;

    /// <summary>One "hh:mm:ss.fff" per line; lines starting with # are comments.</summary>
    public static string Export(IEnumerable<long> bookmarks, string? title = null)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title)) text.Append("# ").Append(title.Replace('\r', ' ').Replace('\n', ' ')).Append('\n');
        foreach (long ms in bookmarks.OrderBy(value => value)) text.Append(TimeText.Format(ms, "ClockMilliseconds")).Append('\n');
        return text.ToString();
    }

    /// <summary>Reads bookmarks written by <see cref="Export"/> (also plain seconds or m:ss); anything unreadable is skipped. Positions past <paramref name="lengthMs"/> are dropped when it is known.</summary>
    public static List<long> Import(string text, long lengthMs = 0)
    {
        var result = new List<long>();
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOfAny(['\t', ' ']);
            if (tab > 0) line = line[..tab];
            if (!Windows.GoToTimeWindow.TryParse(line, out long ms)) continue;
            if (lengthMs > 0 && ms > lengthMs) continue;
            result.Add(ms);
        }
        return Normalize(result);
    }

    /// <summary>Sorted, at least a second apart, capped in number.</summary>
    public static List<long> Normalize(IEnumerable<long> bookmarks)
    {
        var result = new List<long>();
        foreach (long ms in bookmarks.Where(value => value >= 0).Order())
        {
            if (result.Count > 0 && ms - result[^1] < MinimumGapMs) continue;
            if (result.Count >= MaximumBookmarks) break;
            result.Add(ms);
        }
        return result;
    }

    /// <summary>Moves the selected bookmarks (or all when <paramref name="selected"/> is null) by <paramref name="deltaMs"/>, clamped into the video.</summary>
    public static List<long> Shift(IEnumerable<long> bookmarks, long deltaMs, long lengthMs, ISet<long>? selected = null) =>
        Normalize(bookmarks.Select(ms => selected == null || selected.Contains(ms)
            ? Math.Clamp(ms + deltaMs, 0, lengthMs > 0 ? lengthMs : long.MaxValue) : ms));

    public static List<long> Remove(IEnumerable<long> bookmarks, ISet<long> removed) => bookmarks.Where(ms => !removed.Contains(ms)).ToList();

    /// <summary>Bookmarks whose clock text contains <paramref name="filter"/> (empty keeps all).</summary>
    public static List<long> Filter(IEnumerable<long> bookmarks, string? filter)
    {
        string text = (filter ?? "").Trim();
        return text.Length == 0 ? bookmarks.ToList() : bookmarks.Where(ms => TimeText.Format(ms, "ClockMilliseconds").Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
