using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record SubtitleCue(long StartMs, long EndMs, string Text)
{
    public string Label => $"{TimeSpan.FromMilliseconds(StartMs):hh\\:mm\\:ss}  {Text}";
}

public static partial class SubtitleDocument
{
    public static SubtitleCue[] Read(string path)
    {
        if (new FileInfo(path).Length > 16_000_000) throw new InvalidDataException("The subtitle file exceeds 16 MB.");
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        var cues = new List<SubtitleCue>();
        if (System.IO.Path.GetExtension(path).ToLowerInvariant() is ".ass" or ".ssa")
        {
            foreach (var line in text.Split('\n').Where(l => l.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = line[9..].Split(',', 10);
                if (parts.Length == 10 && TryTime(parts[1], out long start) && TryTime(parts[2], out long end))
                    cues.Add(new(start, end, AssTags().Replace(parts[9], "").Replace("\\N", " ").Replace("\\n", " ")));
            }
        }
        else
        {
            foreach (Match match in TimingBlock().Matches(text))
            {
                if (TryTime(match.Groups[1].Value, out long start) && TryTime(match.Groups[2].Value, out long end))
                    cues.Add(new(start, end, HtmlTags().Replace(match.Groups[3].Value, "").Replace('\n', ' ').Trim()));
            }
        }
        return cues.Where(c => c.StartMs >= 0 && c.EndMs >= c.StartMs).OrderBy(c => c.StartMs).Take(100_000).ToArray();
    }

    private static bool TryTime(string input, out long ms)
    {
        ms = 0;
        var parts = input.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3) return false;
        if (!double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds)
            || !int.TryParse(parts[^2], out int minutes) || seconds is < 0 or >= 60 || minutes is < 0 or >= 60) return false;
        int hours = 0;
        if (parts.Length == 3 && (!int.TryParse(parts[0], out hours) || hours < 0)) return false;
        ms = hours * 3_600_000L + minutes * 60_000L + (long)(seconds * 1000);
        return true;
    }

    [GeneratedRegex(@"(?m)^(\d{1,3}:\d{2}(?::\d{2})?[,.]\d{1,3})\s*-->\s*(\d{1,3}:\d{2}(?::\d{2})?[,.]\d{1,3})[^\n]*\n([\s\S]*?)(?=\n\s*\n|\z)")]
    private static partial Regex TimingBlock();
    [GeneratedRegex(@"\{[^}]*\}")] private static partial Regex AssTags();
    [GeneratedRegex(@"<[^>]*>")] private static partial Regex HtmlTags();
}
