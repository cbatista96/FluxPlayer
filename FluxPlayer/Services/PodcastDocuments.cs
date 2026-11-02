using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public static class PodcastDocuments
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static Uri Address(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 8192 || address.Any(char.IsControl) || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Use an HTTP or HTTPS address without embedded credentials.");
        return uri;
    }
    private static XDocument Read(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The feed document exceeds 4 MiB.");
        using var input = new MemoryStream(bytes); using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes });
        return XDocument.Load(reader);
    }
    public static (string Title, PodcastEpisode[] Episodes) Parse(byte[] bytes, string feedId, Uri baseUri)
    {
        var document = Read(bytes); var root = document.Root ?? throw new InvalidDataException("The feed is empty.");
        bool atom = root.Name == XName.Get("feed", "http://www.w3.org/2005/Atom");
        var container = atom ? root : root.Name.LocalName == "rss" ? root.Element("channel") : null;
        if (container == null) throw new InvalidDataException("Use an RSS 2.0 or Atom podcast feed.");
        string Value(XElement element, string name) => element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim() ?? "";
        string title = Value(container, "title"); if (title.Length == 0) title = baseUri.Host;
        if (title.Length > 2048) throw new InvalidDataException("The feed title is too long.");
        var episodes = new List<PodcastEpisode>(); var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in container.Elements().Where(item => item.Name.LocalName == (atom ? "entry" : "item")).Take(1001))
        {
            var link = item.Elements().FirstOrDefault(child => atom ? child.Name.LocalName == "link" && (string?)child.Attribute("rel") == "enclosure" : child.Name.LocalName == "enclosure");
            string? address = (string?)link?.Attribute(atom ? "href" : "url"); if (string.IsNullOrWhiteSpace(address)) continue;
            string type = (string?)link?.Attribute("type") ?? ""; if (type.Length > 0 && !type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) && type != "application/octet-stream") continue;
            if (!Uri.TryCreate(baseUri, address, out var resolved)) continue; string url = Address(resolved.AbsoluteUri).AbsoluteUri; if (!urls.Add(url)) continue;
            string name = Value(item, "title"); if (name.Length == 0) name = "Untitled episode"; if (name.Length > 2048) throw new InvalidDataException("An episode title is too long.");
            string date = Value(item, atom ? "published" : "pubDate"); if (date.Length == 0 && atom) date = Value(item, "updated");
            DateTimeOffset? published = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
            string duration = Value(item, "duration"); long ms = 0;
            if (duration.Contains(':') && TimeSpan.TryParseExact(duration, new[] { @"h\:mm\:ss", @"hh\:mm\:ss", @"m\:ss", @"mm\:ss" }, CultureInfo.InvariantCulture, out var time)) ms = (long)time.TotalMilliseconds;
            else if (long.TryParse(duration, out long seconds) && seconds is >= 0 and <= 31_536_000) ms = seconds * 1000;
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(feedId + "\n" + url))).ToLowerInvariant();
            episodes.Add(new(id, feedId, name, url, published, Math.Clamp(ms, 0, 31_536_000_000), 0, false, null));
        }
        if (episodes.Count > 1000) throw new InvalidDataException("Import up to 1,000 episodes per feed refresh.");
        return (title, episodes.ToArray());
    }
    public static string[] ReadOpml(byte[] bytes)
    {
        var document = Read(bytes);
        if (document.Root?.Name != "opml") throw new InvalidDataException("Choose an OPML subscription document.");
        var result = document.Descendants("outline").Select(item => (string?)item.Attribute("xmlUrl")).OfType<string>()
            .Select(address => Address(address).AbsoluteUri).Distinct(StringComparer.Ordinal).Take(101).ToArray();
        if (result.Length > 100) throw new InvalidDataException("Import up to 100 subscriptions.");
        return result;
    }
    public static Task WriteOpmlCopy(string path, IEnumerable<PodcastFeed> feeds, CancellationToken token = default) => Task.Run(() =>
    {
        var entries = feeds.Take(101).ToArray(); if (entries.Length > 100) throw new InvalidDataException("Export up to 100 subscriptions.");
        string destination = Path.GetFullPath(path); if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new OPML filename.");
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            new XDocument(new XElement("opml", new XAttribute("version", "2.0"), new XElement("head", new XElement("title", "FluxPlayer subscriptions")),
                new XElement("body", entries.Select(feed => new XElement("outline", new XAttribute("type", "rss"), new XAttribute("text", feed.Title), new XAttribute("xmlUrl", Address(feed.Url).AbsoluteUri)))))).Save(temp);
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }, token);
}
