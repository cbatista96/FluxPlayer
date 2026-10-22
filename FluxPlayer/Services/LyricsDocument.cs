using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed partial class LyricsDocument
{
    public IReadOnlyList<(long Time, string Text)> Lines { get; }
    private LyricsDocument((long, string)[] lines) => Lines = lines;

    public static LyricsDocument Parse(string text)
    {
        var lines = new List<(long, string)>();
        long offset = 0;
        foreach (string line in text.Split('\n'))
        {
            var offsetMatch = Offset().Match(line);
            if (offsetMatch.Success) long.TryParse(offsetMatch.Groups[1].Value, out offset);
            var matches = Timestamp().Matches(line);
            string lyric = Timestamp().Replace(line, "").Trim();
            foreach (Match match in matches)
            {
                long minutes = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds < 60) lines.Add((minutes * 60_000 + (long)(seconds * 1000), lyric));
            }
        }
        return new(lines.Select(l => (Math.Max(0, l.Item1 - offset), l.Item2)).OrderBy(l => l.Item1).ToArray());
    }

    public string LineAt(long ms)
    {
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Lines[mid].Time <= ms) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found >= 0 ? Lines[found].Text : "";
    }

    public static LyricsDocument? LoadSidecar(string mediaPath)
    {
        try
        {
            string path = Path.ChangeExtension(mediaPath, ".lrc");
            return File.Exists(path) && new FileInfo(path).Length <= 2_000_000 ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    [GeneratedRegex(@"\[(\d{1,5}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();
}
