using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed record ChapterImport(ChapterInfo[] Chapters, string Note);

/// <summary>Flat navigation chapters for one media timeline. No media-container mutation.</summary>
public static partial class ChapterDocument
{
    public const int MaximumChapters = 10_000;
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const long MaximumTimeMs = 999 * 3_600_000L + 3_599_999;

    public static bool TryParseTime(string? text, out long ms)
    {
        ms = 0;
        var match = TimePattern().Match(text?.Trim() ?? "");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int hours) || hours > 999) return false;
        int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (minutes > 59 || seconds > 59) return false;
        string fraction = match.Groups[4].Value.PadRight(3, '0');
        ms = hours * 3_600_000L + minutes * 60_000L + seconds * 1000L + int.Parse(fraction[..3], CultureInfo.InvariantCulture);
        return true;
    }

    public static string FormatTime(long ms)
    {
        if (ms < 0 || ms > MaximumTimeMs) throw new ArgumentOutOfRangeException(nameof(ms));
        return FormattableString.Invariant($"{ms / 3_600_000:00}:{ms / 60_000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}");
    }

    public static ChapterInfo[] Validate(IEnumerable<ChapterInfo> chapters, long durationMs = 0)
    {
        var result = chapters.Take(MaximumChapters + 1).ToArray();
        if (result.Length is < 1 or > MaximumChapters) throw new InvalidDataException($"Use 1–{MaximumChapters:N0} chapters.");
        long previous = -1;
        for (int index = 0; index < result.Length; index++)
        {
            var chapter = result[index];
            if (chapter == null || chapter.TimeMs < 0 || chapter.TimeMs > MaximumTimeMs || chapter.TimeMs <= previous)
                throw new InvalidDataException("Chapter times must be distinct and increase from zero or later, within 999 hours.");
            if (durationMs > 0 && chapter.TimeMs >= durationMs) throw new InvalidDataException("A chapter starts at or beyond the end of the current media.");
            if (string.IsNullOrWhiteSpace(chapter.Name) || chapter.Name.Length > 512 || chapter.Name.Any(char.IsControl))
                throw new InvalidDataException("Each chapter title must contain 1–512 printable characters.");
            result[index] = chapter with { Index = index, Name = chapter.Name.Trim() }; previous = chapter.TimeMs;
        }
        return result;
    }

    public static ChapterImport Read(string path, CancellationToken token = default)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumBytes) throw new InvalidDataException("The chapter file exceeds 4 MiB.");
        using var source = new StreamReader(file, Encoding.UTF8, true, 4096, leaveOpen: true);
        int first = source.Read();
        while (first >= 0 && (char.IsWhiteSpace((char)first) || first == '\uFEFF'))
        {
            token.ThrowIfCancellationRequested();
            if (file.Position > MaximumBytes) throw new InvalidDataException("The chapter file exceeds 4 MiB.");
            first = source.Read();
        }
        if (first == '<')
        {
            // Parse original bytes so the XML declaration/BOM controls character decoding.
            file.Position = 0; return ReadXml(file, token);
        }
        var content = new StringBuilder(); if (first >= 0) content.Append((char)first);
        var buffer = new char[4096]; int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (content.Length + count > MaximumBytes || file.Position > MaximumBytes) throw new InvalidDataException("The chapter file exceeds 4 MiB.");
            content.Append(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return ReadSimple(content.ToString(), token);
    }

    private static ChapterImport ReadSimple(string text, CancellationToken token)
    {
        var times = new SortedDictionary<int, long>(); var names = new Dictionary<int, string>();
        foreach (string raw in text.Split('\n'))
        {
            token.ThrowIfCancellationRequested();
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
            var match = SimpleLine().Match(line);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number) || number < 1 || number > MaximumChapters)
                throw new InvalidDataException("Use OGM chapter lines such as CHAPTER01=00:00:00.000 and CHAPTER01NAME=Opening.");
            if (match.Groups[2].Success)
            { if (!names.TryAdd(number, match.Groups[3].Value)) throw new InvalidDataException("The chapter file repeats a title key."); }
            else
            {
                if (!TryParseTime(match.Groups[3].Value, out long ms) || !times.TryAdd(number, ms))
                    throw new InvalidDataException("The chapter file contains an invalid or repeated time key.");
            }
        }
        if (names.Keys.Any(number => !times.ContainsKey(number))) throw new InvalidDataException("A chapter title has no matching time.");
        return new(Validate(times.Select((pair, index) => new ChapterInfo(index, names.GetValueOrDefault(pair.Key, $"Chapter {pair.Key}"), pair.Value))), "OGM simple chapters, with millisecond timing.");
    }

    private static XmlReaderSettings XmlSettings() => new()
    { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaximumBytes, IgnoreComments = true };

    private static ChapterImport ReadXml(Stream input, CancellationToken token)
    {
        try
        {
            using (var scan = XmlReader.Create(input, XmlSettings()))
            {
                int atoms = 0;
                while (scan.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (scan.Depth > 32 || (scan.NodeType == XmlNodeType.Element && scan.LocalName == "ChapterAtom" && ++atoms > MaximumChapters))
                        throw new InvalidDataException("The chapter XML exceeds its depth or chapter limit.");
                }
            }
            input.Position = 0;
            using var reader = XmlReader.Create(input, XmlSettings());
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "Chapters") throw new InvalidDataException("Choose a Matroska Chapters XML document.");
            var editions = document.Root.Elements().Where(element => element.Name.LocalName == "EditionEntry").ToArray();
            var edition = editions.FirstOrDefault(element => Value(element, "EditionFlagDefault") == "1") ?? editions.FirstOrDefault()
                ?? throw new InvalidDataException("The chapter XML has no edition.");
            if (Value(edition, "EditionFlagOrdered") == "1" || edition.Descendants().Any(element => element.Name.LocalName is "ChapterSegmentUID" or "ChapterSegmentEditionUID"))
                throw new InvalidDataException("Ordered or linked-segment chapters require container playback support and cannot be attached to one timeline.");
            var chapters = new List<ChapterInfo>();
            foreach (var atom in edition.Elements().Where(element => element.Name.LocalName == "ChapterAtom"))
            {
                token.ThrowIfCancellationRequested();
                if (Value(atom, "ChapterFlagEnabled") == "0" || Value(atom, "ChapterFlagHidden") == "1") continue;
                if (!TryParseTime(Value(atom, "ChapterTimeStart"), out long ms)) throw new InvalidDataException("A chapter start time is invalid.");
                string? name = atom.Elements().Where(element => element.Name.LocalName == "ChapterDisplay")
                    .Select(element => Value(element, "ChapterString")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                chapters.Add(new(chapters.Count, name ?? $"Chapter {chapters.Count + 1}", ms));
            }
            return new(Validate(chapters), "Imported visible top-level chapters from the default or first edition. Millisecond timing; nested chapters, alternate displays and container metadata are not retained in a saved copy.");
        }
        catch (XmlException e) { throw new InvalidDataException("The chapter XML is invalid. External entities are not loaded.", e); }
    }

    private static string? Value(XElement element, string name) => element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim();

    public static void Write(string destination, IEnumerable<ChapterInfo> chapters, bool xml, CancellationToken token = default)
    {
        var validated = Validate(chapters); token.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if ((xml && extension != ".xml") || (!xml && extension is not (".txt" or ".ogm")))
            throw new InvalidDataException("Use .xml for Matroska chapters or .txt/.ogm for simple chapters.");
        if (File.Exists(destination)) throw new IOException("The chapter file already exists. Choose a new destination.");
        string folder = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, ".flux-chapters-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (xml)
                {
                    var document = new XDocument(new XElement("Chapters", new XElement("EditionEntry", validated.Select(chapter =>
                        new XElement("ChapterAtom", new XElement("ChapterTimeStart", FormatTime(chapter.TimeMs)),
                            new XElement("ChapterDisplay", new XElement("ChapterString", chapter.Name), new XElement("ChapterLanguage", "und")))))));
                    document.Save(writer);
                }
                else
                {
                    foreach (var chapter in validated)
                    {
                        token.ThrowIfCancellationRequested();
                        writer.WriteLine($"CHAPTER{chapter.Index + 1:00}={FormatTime(chapter.TimeMs)}");
                        writer.WriteLine($"CHAPTER{chapter.Index + 1:00}NAME={chapter.Name}");
                    }
                }
            }
            if (new FileInfo(temporary).Length > MaximumBytes) throw new InvalidDataException("The saved chapters exceed 4 MiB. Use fewer or shorter titles.");
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [GeneratedRegex(@"^(\d{1,3}):([0-9]{2}):([0-9]{2})(?:[.,]([0-9]{1,9}))?$")]
    private static partial Regex TimePattern();
    [GeneratedRegex(@"^CHAPTER([0-9]{1,5})(NAME)?=(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleLine();
}
