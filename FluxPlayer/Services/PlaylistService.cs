using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FluxPlayer.Models;

namespace FluxPlayer.Services;

public static partial class PlaylistService
{
    public const int MaxEntries = 100_000;
    public static string[] Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("The playlist exceeds the 32 MB limit.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        IEnumerable<string> sources;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".xspf":
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    sources = XDocument.Load(reader).Descendants().Where(x => x.Name.LocalName == "location").Select(x => x.Value).Take(MaxEntries).ToArray();
                break;
            case ".pls":
                sources = File.ReadLines(path).Select(line => PlsEntry().Match(line.Trim()))
                    .Where(m => m.Success).Take(MaxEntries).OrderBy(m => long.TryParse(m.Groups[1].Value, out long index) ? index : long.MaxValue)
                    .Select(m => m.Groups[2].Value);
                break;
            default:
                sources = File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
                break;
        }
        return sources.Take(MaxEntries).Select(s => Resolve(s, directory)).OfType<string>().ToArray();
    }

    private static string? Resolve(string source, string directory, bool requireExisting = true)
    {
        source = source.Trim();
        if (MediaFormats.IsNetwork(source)) return source;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile) source = uri.LocalPath;
            string full = Path.GetFullPath(source, directory);
            return !requireExisting || File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    public static SavedMediaTrack[] ReadEntries(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("The playlist exceeds 32 MiB.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var entries = new List<(string Path, string Title)>();
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".xspf":
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    foreach (var track in XDocument.Load(reader).Descendants().Where(item => item.Name.LocalName == "track").Take(MaxEntries + 1))
                    {
                        string? location = track.Elements().FirstOrDefault(item => item.Name.LocalName == "location")?.Value;
                        if (location != null) entries.Add((location, track.Elements().FirstOrDefault(item => item.Name.LocalName == "title")?.Value ?? ""));
                    }
                break;
            case ".pls":
                var files = new SortedDictionary<long, string>(); var titles = new Dictionary<long, string>();
                foreach (string line in File.ReadLines(path))
                {
                    var match = Regex.Match(line.Trim(), @"^(File|Title)(\d+)\s*=(.*)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    if (!match.Success || !long.TryParse(match.Groups[2].Value, out long index)) continue;
                    if (match.Groups[1].Value.Equals("File", StringComparison.OrdinalIgnoreCase)) files[index] = match.Groups[3].Value;
                    else titles[index] = match.Groups[3].Value;
                    if (files.Count > MaxEntries || titles.Count > MaxEntries) throw new InvalidDataException("Too many playlist entries.");
                }
                entries.AddRange(files.Select(pair => (pair.Value, titles.GetValueOrDefault(pair.Key, ""))));
                break;
            default:
                string title = "";
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase)) { int comma = line.IndexOf(','); title = comma < 0 ? "" : line[(comma + 1)..]; }
                    else if (line.Length > 0 && !line.StartsWith('#')) { entries.Add((line, title)); title = ""; }
                    if (entries.Count > MaxEntries) throw new InvalidDataException("Too many playlist entries.");
                }
                break;
        }
        if (entries.Count > MaxEntries) throw new InvalidDataException("Too many playlist entries.");
        return entries.Select(entry => (Path: Resolve(entry.Path, directory, false), entry.Title)).Where(entry => entry.Path != null)
            .Select(entry => new SavedMediaTrack(entry.Path!, string.IsNullOrWhiteSpace(entry.Title) ? MediaFormats.IsNetwork(entry.Path!) ? entry.Path! : Path.GetFileNameWithoutExtension(entry.Path!) : entry.Title)).ToArray();
    }

    public static Task WriteCopyAsync(string path, IEnumerable<SavedMediaTrack> tracks, CancellationToken token = default)
    {
        var entries = tracks.Take(MaxEntries + 1).ToArray();
        if (entries.Length > MaxEntries || entries.Any(track => track == null || !MediaCatalogService.ValidSource(track.Path) || track.Title == null || track.Title.Length > 4096)) throw new InvalidDataException("Invalid playlist entries.");
        return Task.Run(() =>
        {
            string destination = Path.GetFullPath(path), directory = Path.GetDirectoryName(destination)!;
            if (Path.GetExtension(destination).ToLowerInvariant() is not (".m3u" or ".m3u8" or ".pls" or ".xspf")) throw new InvalidDataException("Choose M3U8, M3U, PLS or XSPF.");
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new playlist filename.");
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + Path.GetExtension(destination));
            try { Write(temp, entries.Select(track => new MediaItem { FilePath = track.Path, Title = track.Title })); token.ThrowIfCancellationRequested(); File.Move(temp, destination, false); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }, token);
    }

    public static void Write(string path, IEnumerable<MediaItem> items)
    {
        var tracks = items.Take(MaxEntries).ToArray();
        string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
        if (Path.GetExtension(path).Equals(".xspf", StringComparison.OrdinalIgnoreCase))
        {
            XNamespace ns = "http://xspf.org/ns/0/";
            new XDocument(new XElement(ns + "playlist", new XAttribute("version", "1"),
                new XElement(ns + "trackList", tracks.Select(t => new XElement(ns + "track",
                    new XElement(ns + "location", MediaFormats.IsNetwork(t.FilePath) ? t.FilePath : new Uri(t.FilePath).AbsoluteUri),
                    new XElement(ns + "title", Clean(t.Title))))))).Save(path);
            return;
        }
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        if (Path.GetExtension(path).Equals(".pls", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteLine("[playlist]");
            for (int i = 0; i < tracks.Length; i++)
            {
                writer.WriteLine($"File{i + 1}={Clean(tracks[i].FilePath)}");
                writer.WriteLine($"Title{i + 1}={Clean(tracks[i].Title)}");
                writer.WriteLine($"Length{i + 1}=-1");
            }
            writer.WriteLine($"NumberOfEntries={tracks.Length}\nVersion=2");
        }
        else
        {
            writer.WriteLine("#EXTM3U");
            foreach (var item in tracks)
            {
                writer.WriteLine($"#EXTINF:-1,{Clean(item.Title)}");
                writer.WriteLine(Clean(item.FilePath));
            }
        }
    }

    [GeneratedRegex(@"^File(\d+)\s*=(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlsEntry();
}
