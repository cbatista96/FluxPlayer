using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record CueIndex(int Number, long Frames);
public sealed record CueDocumentTrack(int Number, string Mode, string Source, string Title, string Artist, ImmutableArray<CueIndex> Indexes)
{
    public long StartMs => Indexes.Single(index => index.Number == 1).Frames * 1000 / 75;
    public string Start => CueSheet.FormatTimestamp(StartMs);
}
public sealed record CueReference(int Line, string Source, string Prefix, string Suffix);
public sealed record CueDocumentData(string Text, string SourcePath, ImmutableArray<CueDocumentTrack> Tracks, ImmutableArray<CueReference> References);

/// <summary>Preserves original directives and whitespace. Playback still uses audio INDEX 01 only.</summary>
public static partial class CueDocument
{
    public const int MaximumBytes = 4 * 1024 * 1024, MaximumLines = 20_000;
    private sealed class Track(int number, string mode, string source, string artist)
    {
        public int Number = number; public string Mode = mode, Source = source, Title = "", Artist = artist;
        public List<CueIndex> Indexes = new(); public HashSet<string> Singletons = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly HashSet<string> TextFields = new(new[] { "TITLE", "PERFORMER", "SONGWRITER", "COMPOSER", "ARRANGER", "MESSAGE", "DISC_ID", "GENRE", "TOC_INFO1", "TOC_INFO2", "UPC_EAN", "SIZE_INFO", "ISRC" }, StringComparer.OrdinalIgnoreCase);
    static CueDocument() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static bool RequiresPreservation(string text)
    {
        bool track = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim(); if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries); string key = parts[0].ToUpperInvariant();
            if (key == "TRACK") { track = true; continue; }
            if (key == "INDEX") { if (parts.Length < 2 || parts[1] != "01") return true; continue; }
            if (key is "TITLE" or "PERFORMER") { if (!track) return true; continue; }
            if (key != "FILE") return true;
        }
        return false;
    }
    public static async Task<string> ReadAsync(string path, bool windows1252 = false, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (file.Length > MaximumBytes) throw new InvalidDataException("The advanced CUE editor supports up to 4 MiB.");
        Encoding encoding = windows1252 ? Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) : new UTF8Encoding(false, true);
        using var reader = new StreamReader(file, encoding, detectEncodingFromByteOrderMarks: true);
        var result = new StringBuilder(); char[] buffer = new char[8192]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        { if (result.Length + count > MaximumBytes) throw new InvalidDataException("The CUE text is too large."); result.Append(buffer, 0, count); }
        return result.ToString();
    }
    public static CueDocumentData Parse(string text, string sourcePath, CancellationToken token = default)
    {
        if (text.Length > MaximumBytes || Encoding.UTF8.GetByteCount(text) > MaximumBytes || text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
            throw new InvalidDataException("CUE text contains unsupported control characters or exceeds 4 MiB.");
        sourcePath = Path.GetFullPath(sourcePath); string directory = Path.GetDirectoryName(sourcePath)!;
        string[] lines = text.Split('\n'); if (lines.Length > MaximumLines) throw new InvalidDataException("Use up to 20,000 CUE lines.");
        var tracks = ImmutableArray.CreateBuilder<CueDocumentTrack>(); var references = ImmutableArray.CreateBuilder<CueReference>();
        string source = "", albumArtist = ""; Track? track = null; long lastFileFrame = -1, lastTrackStart = -1; int previousNumber = 0; bool cdTextFile = false;
        void Finish()
        {
            if (track == null) return;
            if (!track.Indexes.Any(index => index.Number == 1)) throw new InvalidDataException($"Track {track.Number} requires INDEX 01.");
            long start = track.Indexes.Single(index => index.Number == 1).Frames;
            if (start <= lastTrackStart) throw new InvalidDataException("INDEX 01 times must increase within a FILE."); lastTrackStart = start;
            tracks.Add(new(track.Number, track.Mode, track.Source, track.Title.Length == 0 ? $"Track {track.Number}" : track.Title, track.Artist, track.Indexes.ToImmutableArray())); track = null;
        }
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            token.ThrowIfCancellationRequested(); string raw = lines[lineIndex].TrimEnd('\r'), line = raw.Trim(); if (line.Length == 0) continue;
            int separator = line.IndexOfAny(new[] { ' ', '\t' }); string key = (separator < 0 ? line : line[..separator]).ToUpperInvariant(), value = separator < 0 ? "" : line[(separator + 1)..].Trim();
            try
            {
                if (key == "REM") continue;
                if (key is "FILE" or "CDTEXTFILE")
                {
                    var match = ReferenceLine().Match(raw); if (!match.Success) throw new InvalidDataException("Use a quoted filename and a FILE format.");
                    string path = Unquote(match.Groups[2].Value); if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new InvalidDataException("Invalid referenced path.");
                    string fullPath = Path.GetFullPath(path, directory); string suffix = match.Groups[3].Value;
                    if (key == "FILE")
                    {
                        string kind = suffix.Trim().ToUpperInvariant(); if (kind is not ("WAVE" or "AIFF" or "MP3" or "FLAC" or "BINARY" or "MOTOROLA")) throw new InvalidDataException("Unsupported FILE format label.");
                        Finish(); source = fullPath; lastFileFrame = lastTrackStart = -1;
                    }
                    else { if (cdTextFile || suffix.Trim().Length > 0) throw new InvalidDataException("Use one CDTEXTFILE filename."); cdTextFile = true; }
                    references.Add(new(lineIndex, fullPath, match.Groups[1].Value, suffix)); continue;
                }
                if (key == "TRACK")
                {
                    Finish(); var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (source.Length == 0 || parts.Length != 2 || !int.TryParse(parts[0], out int number) || number <= previousNumber || number > 99
                        || !TrackMode().IsMatch(parts[1])) throw new InvalidDataException("TRACK needs an increasing number 01–99, a preceding FILE and a supported mode.");
                    previousNumber = number; track = new(number, parts[1].ToUpperInvariant(), source, albumArtist); continue;
                }
                if (key == "INDEX")
                {
                    var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (track == null || parts.Length != 2 || !int.TryParse(parts[0], out int number) || number is < 0 or > 99 || !TryFrames(parts[1], out long frames)
                        || track.Indexes.Count > 0 && (number <= track.Indexes[^1].Number || frames < track.Indexes[^1].Frames) || frames < lastFileFrame)
                        throw new InvalidDataException("INDEX numbers must increase within a track and timestamps cannot go backward within a FILE.");
                    track.Indexes.Add(new(number, frames)); lastFileFrame = frames; continue;
                }
                if (key is "PREGAP" or "POSTGAP")
                {
                    if (track == null || !TryFrames(value, out _) || !track.Singletons.Add(key)) throw new InvalidDataException("Use one valid PREGAP/POSTGAP per track."); continue;
                }
                if (key == "FLAGS")
                {
                    var flags = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (track == null || flags.Length == 0 || !track.Singletons.Add(key) || flags.Any(flag => flag.ToUpperInvariant() is not ("PRE" or "DCP" or "4CH" or "SCMS")) || flags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != flags.Length)
                        throw new InvalidDataException("Use distinct PRE/DCP/4CH/SCMS flags inside a track."); continue;
                }
                if (key == "CATALOG") { string catalog = Unquote(value); if (catalog.Length != 13 || !catalog.All(char.IsAsciiDigit)) throw new InvalidDataException("CATALOG needs 13 digits."); continue; }
                if (TextFields.Contains(key))
                {
                    string content = Unquote(value);
                    if (key == "TITLE" && track != null) track.Title = content;
                    if (key == "PERFORMER") { if (track == null) albumArtist = content; else track.Artist = content; }
                }
                // Unrecognized directives remain verbatim. They are not executed or interpreted.
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException)
            { throw new InvalidDataException($"CUE line {lineIndex + 1}: {e.Message}", e); }
        }
        Finish(); if (tracks.Count == 0) throw new InvalidDataException("Add at least one track with INDEX 01.");
        return new(text, sourcePath, tracks.ToImmutable(), references.ToImmutable());
    }
    private static string Unquote(string value)
    {
        if (value.StartsWith('"')) { if (value.Length < 2 || !value.EndsWith('"') || value[1..^1].Contains('"')) throw new InvalidDataException("Quoted text needs matching quotes."); return value[1..^1]; }
        if (value.Contains('"')) throw new InvalidDataException("Unexpected quote in CUE text."); return value;
    }
    public static bool TryFrames(string text, out long frames)
    {
        frames = 0; if (!CueSheet.TryParseTimestamp(text, out _)) return false;
        var parts = text.Split(':'); frames = long.Parse(parts[0], CultureInfo.InvariantCulture) * 4500 + int.Parse(parts[1], CultureInfo.InvariantCulture) * 75 + int.Parse(parts[2], CultureInfo.InvariantCulture); return true;
    }
    public static async Task WriteCopyAsync(string destination, CueDocumentData document, CancellationToken token = default)
    {
        destination = Path.GetFullPath(destination); document = Parse(document.Text, document.SourcePath, token);
        if (!Path.GetExtension(destination).Equals(".cue", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a .cue destination.");
        if (string.Equals(destination, document.SourcePath, StringComparison.OrdinalIgnoreCase) || document.References.Any(reference => string.Equals(destination, reference.Source, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Choose a new copy path, different from the original and all referenced files.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The destination already exists. Choose a new name.");
        string directory = Path.GetDirectoryName(destination)!; if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
        string[] lines = document.Text.Split('\n');
        foreach (var reference in document.References)
        {
            string path = Path.GetRelativePath(directory, reference.Source);
            if (path.Any(character => character == '"' || char.IsControl(character))) throw new InvalidDataException("A referenced path cannot be represented safely in CUE text.");
            bool cr = lines[reference.Line].EndsWith('\r'); lines[reference.Line] = reference.Prefix + "\"" + path + "\"" + reference.Suffix + (cr ? "\r" : "");
        }
        string output = string.Join('\n', lines); Parse(output, destination, token);
        string temporary = Path.Combine(directory, ".flux-cue-advanced-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            token.ThrowIfCancellationRequested();
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true)))
                await writer.WriteAsync(output.AsMemory(), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    [GeneratedRegex("^(\\s*(?:FILE|CDTEXTFILE)\\s+)(\"[^\"]*\"|[^\\s\"]+)(.*)$", RegexOptions.IgnoreCase)] private static partial Regex ReferenceLine();
    [GeneratedRegex(@"^(AUDIO|MODE1/(2048|2352)|MODE2/(2336|2048|2342|2332|2352))$", RegexOptions.IgnoreCase)] private static partial Regex TrackMode();
}
