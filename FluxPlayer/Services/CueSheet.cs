using System.IO;
using System.Text.RegularExpressions;
using System.Text;

namespace FluxPlayer.Services;

public sealed record CueTrack(string Source, int Number, string Title, string Artist, long StartMs);

public static partial class CueSheet
{
    public static CueTrack[] Read(string path, bool requireExistingSources = true)
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
                if (index.Success && number > 0 && source.Length > 0 && (!requireExistingSources || File.Exists(source)))
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

    public static bool TryParseTimestamp(string? value, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        var parts = value.Split(':');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Any(c => !char.IsAsciiDigit(c)))
            || !long.TryParse(parts[0], out long minutes) || minutes > 1_000_000
            || !int.TryParse(parts[1], out int seconds) || seconds >= 60
            || !int.TryParse(parts[2], out int frames) || frames >= 75) return false;
        milliseconds = minutes * 60_000 + seconds * 1000 + frames * 1000 / 75;
        return true;
    }

    public static string FormatTimestamp(long milliseconds)
    {
        if (milliseconds is < 0 or > 60_000_000_000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        // Reading an INDEX floors fractional milliseconds. Round back to the nearest 1/75-second frame.
        long frames = (milliseconds * 75 + 500) / 1000;
        return FormattableString.Invariant($"{frames / 4500:00}:{frames / 75 % 60:00}:{frames % 75:00}");
    }

    public static void Write(string destination, IEnumerable<CueTrack> tracks, bool overwrite = false)
    {
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .cue destination file.");
        var items = tracks.Take(100).ToArray();
        if (items.Length is < 1 or > 99) throw new InvalidDataException("A CUE sheet must contain between 1 and 99 tracks.");
        if (!overwrite && File.Exists(destination)) throw new IOException("The CUE file already exists. Choose a new name.");
        var lastPositions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var validated = new List<CueTrack>(items.Length);
        int previousNumber = 0;
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Source) || item.Number <= previousNumber || item.Number > 99)
                throw new InvalidDataException("Track numbers must increase and stay between 1 and 99.");
            string source = Path.GetFullPath(item.Source);
            if (!File.Exists(source)) throw new FileNotFoundException("A CUE source file is unavailable.", source);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The destination cannot replace a source file.");
            if (item.StartMs is < 0 or > 60_000_000_000)
                throw new InvalidDataException("A CUE position is outside the supported range.");
            TryParseTimestamp(FormatTimestamp(item.StartMs), out long normalizedPosition);
            if (lastPositions.TryGetValue(source, out long previous) && normalizedPosition <= previous)
                throw new InvalidDataException("Track positions must increase within each source file.");
            foreach (string? text in new[] { item.Title, item.Artist, source })
                if (text == null || text.Length > 32767 || text.Any(c => c == '"' || char.IsControl(c)))
                    throw new InvalidDataException("CUE text cannot contain quotes or control characters.");
            lastPositions[source] = normalizedPosition; previousNumber = item.Number;
            validated.Add(item with { Source = source, StartMs = normalizedPosition });
        }

        string folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, ".flux-cue-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false)))
            {
                writer.WriteLine("REM Generated by FluxPlayer");
                string current = "";
                foreach (var track in validated)
                {
                    if (!string.Equals(current, track.Source, StringComparison.OrdinalIgnoreCase))
                    {
                        current = track.Source;
                        string kind = Path.GetExtension(current).ToLowerInvariant() switch { ".mp3" => "MP3", ".aif" or ".aiff" => "AIFF", _ => "WAVE" };
                        writer.WriteLine($"FILE \"{Path.GetRelativePath(folder, current)}\" {kind}");
                    }
                    writer.WriteLine($"  TRACK {track.Number:00} AUDIO");
                    writer.WriteLine($"    TITLE \"{track.Title}\"");
                    writer.WriteLine($"    PERFORMER \"{track.Artist}\"");
                    writer.WriteLine($"    INDEX 01 {FormatTimestamp(track.StartMs)}");
                }
            }
            File.Move(temporary, destination, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    [GeneratedRegex("^FILE\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex FileEntry();
    [GeneratedRegex(@"^INDEX\s+01\s+(\d+):(\d{2}):(\d{2})", RegexOptions.IgnoreCase)] private static partial Regex IndexEntry();
}
