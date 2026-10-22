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

    private static string? Resolve(string source, string directory)
    {
        source = source.Trim();
        if (MediaFormats.IsNetwork(source)) return source;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile) source = uri.LocalPath;
            string full = Path.GetFullPath(source, directory);
            return File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
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
