using System.IO;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record CueTrack(string Source, int Number, string Title, string Artist, long StartMs);

public static partial class CueSheet
{
    public static CueTrack[] Read(string path)
    {
        if (new FileInfo(path).Length > 16_000_000) throw new InvalidDataException("The CUE sheet exceeds 16 MB.");
        var tracks = new List<CueTrack>();
        string source = "", title = "", artist = "", albumArtist = "";
        int number = 0;
        foreach (string raw in File.ReadLines(path).Take(100_000))
        {
            string line = raw.Trim();
            var file = FileEntry().Match(line);
            if (file.Success) source = Path.GetFullPath(file.Groups[1].Value, Path.GetDirectoryName(Path.GetFullPath(path))!);
            else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            { int.TryParse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1), out number); title = ""; artist = albumArtist; }
            else if (line.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase)) title = line[6..].Trim('"');
            else if (line.StartsWith("PERFORMER ", StringComparison.OrdinalIgnoreCase))
            { artist = line[10..].Trim('"'); if (number == 0) albumArtist = artist; }
            else
            {
                var index = IndexEntry().Match(line);
                if (index.Success && number > 0 && source.Length > 0 && File.Exists(source))
                {
                    if (!long.TryParse(index.Groups[1].Value, out long minutes) || minutes > 1_000_000) continue;
                    long seconds = long.Parse(index.Groups[2].Value), frames = long.Parse(index.Groups[3].Value);
                    if (seconds < 60 && frames < 75)
                        tracks.Add(new(source, number, title.Length > 0 ? title : $"Track {number}", artist, minutes * 60_000 + seconds * 1000 + frames * 1000 / 75));
                }
            }
        }
        return tracks.ToArray();
    }
    [GeneratedRegex("^FILE\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex FileEntry();
    [GeneratedRegex(@"^INDEX\s+01\s+(\d+):(\d{2}):(\d{2})", RegexOptions.IgnoreCase)] private static partial Regex IndexEntry();
}
