using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record MusicBrainzRow(string Kind, string Id, string Title, string Artist = "", string Album = "", string Date = "", int Track = 0, string Details = "");
public sealed record CoverCopy(byte[] Bytes, string Extension);
internal sealed class MusicBrainzSource : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client(true);
    private readonly HttpClient _artworkClient = RemoteMediaIO.Client();
    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static DateTimeOffset _next;
    public MusicBrainzSource()
    {
        const string agent = "FluxPlayer/1.4.0 (+https://github.com/cbatista96/FluxPlayer)";
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(agent); _artworkClient.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
    }
    internal static Uri CoverAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo != "" || !uri.IsDefaultPort || !(uri.Host == "archive.org" || uri.Host.EndsWith(".archive.org", StringComparison.OrdinalIgnoreCase) || uri.Host == "coverartarchive.org")) throw new IOException("The archive returned an unsupported artwork origin.");
        // The official archive still publishes legacy HTTP image links in JSON.
        // Request the HTTPS equivalent without weakening TLS for the session.
        return uri.Scheme == "http" ? new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri : uri;
    }
    private async Task<JsonDocument> Request(string route, CancellationToken token)
    {
        await RateGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var wait = _next - DateTimeOffset.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
            _next = DateTimeOffset.UtcNow.AddMilliseconds(1100);
            return await RemoteMediaIO.Json(_client, new Uri("https://musicbrainz.org/ws/2/" + route + (route.Contains('?') ? "&" : "?") + "fmt=json"), token).ConfigureAwait(false);
        }
        finally { RateGate.Release(); }
    }
    private static string Id(string id) => Guid.TryParse(id, out var value) ? value.ToString() : throw new ArgumentException("Choose a valid MusicBrainz identifier.");
    private static string S(JsonElement item, string key) => item.TryGetProperty(key, out var value) ? value.ToString() : "";
    private static IEnumerable<JsonElement> A(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static string Artist(JsonElement item) => string.Concat(A(item, "artist-credit").Select(c => S(c, "name") + S(c, "joinphrase")));
    public async Task<MusicBrainzRow[]> Search(string kind, string query, CancellationToken token)
    {
        if (kind is not ("recording" or "release" or "artist") || query.Length is < 1 or > 500) throw new ArgumentException("Choose recording/release/artist and a bounded query.");
        using var doc = await Request(kind + "?query=" + Uri.EscapeDataString(query) + "&limit=50", token).ConfigureAwait(false);
        return A(doc.RootElement, kind + "s").Take(50).Select(i => new MusicBrainzRow(kind, S(i, "id"), kind == "artist" ? S(i, "name") : S(i, "title"), Artist(i), "", S(i, "date"), 0, S(i, "disambiguation"))).ToArray();
    }
    public async Task<MusicBrainzRow[]> Discography(string artist, CancellationToken token)
    {
        using var doc = await Request("release?artist=" + Id(artist) + "&inc=artist-credits&limit=100", token).ConfigureAwait(false);
        return A(doc.RootElement, "releases").Take(100).Select(i => new MusicBrainzRow("release", S(i, "id"), S(i, "title"), Artist(i), "", S(i, "date"))).ToArray();
    }
    public async Task<MusicBrainzRow[]> Tracks(string release, CancellationToken token)
    {
        using var doc = await Request("release/" + Id(release) + "?inc=recordings+artist-credits", token).ConfigureAwait(false); var root = doc.RootElement;
        return A(root, "media").SelectMany(media => A(media, "tracks").Select(track => new MusicBrainzRow("track", track.TryGetProperty("recording", out var recording) ? S(recording, "id") : "", S(track, "title"), Artist(track) is { Length: > 0 } artist ? artist : Artist(root), S(root, "title"), S(root, "date"), track.TryGetProperty("position", out var position) && position.TryGetInt32(out int number) ? number : 0, "Disc " + S(media, "position")))).Take(500).ToArray();
    }
    public async Task<MusicBrainzRow[]> Relations(string kind, string id, CancellationToken token)
    {
        if (kind is not ("artist" or "recording")) throw new ArgumentException("Choose an artist or recording.");
        using var doc = await Request(kind + "/" + Id(id) + "?inc=artist-rels+url-rels+work-rels", token).ConfigureAwait(false);
        return A(doc.RootElement, "relations").Take(200).Select(relation =>
        {
            string target = S(relation, "target-type"); var entity = relation.TryGetProperty(target, out var value) ? value : default;
            return new MusicBrainzRow("relation", entity.ValueKind == JsonValueKind.Object ? S(entity, "id") : "", S(relation, "type"), "", "", "", 0, entity.ValueKind == JsonValueKind.Object ? (target == "url" ? S(entity, "resource") : S(entity, "name") is { Length: > 0 } name ? name : S(entity, "title")) : target);
        }).ToArray();
    }
    public async Task<CoverCopy> Cover(string release, bool back, CancellationToken token)
    {
        using var doc = await RemoteMediaIO.Json(_client, new Uri("https://coverartarchive.org/release/" + Id(release)), token).ConfigureAwait(false);
        var image = A(doc.RootElement, "images").FirstOrDefault(i => i.TryGetProperty(back ? "back" : "front", out var flag) && flag.ValueKind == JsonValueKind.True);
        if (image.ValueKind != JsonValueKind.Object) throw new IOException("The archive does not list that cover side.");
        string address = image.TryGetProperty("thumbnails", out var thumbs) && thumbs.TryGetProperty("500", out var small) ? small.GetString() ?? "" : S(image, "image");
        Uri uri = CoverAddress(address); byte[]? bytes = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            using var response = await _artworkClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            { if (redirects == 5) throw new IOException("The artwork exceeded its redirect limit."); uri = CoverAddress(new Uri(uri, next).AbsoluteUri); continue; }
            bytes = await RemoteMediaIO.Bytes(response, 10 * 1024 * 1024, timeout.Token).ConfigureAwait(false); break;
        }
        if (bytes == null) throw new IOException("The artwork did not return image bytes.");
        string extension = bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? ".jpg" : bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? ".png" : throw new IOException("Only JPEG/PNG cover copies are accepted.");
        return new(bytes, extension);
    }
    public static MetadataPatch Patch(MusicBrainzRow row, bool title, bool artist, bool album, bool number, bool date)
    {
        if (row.Kind is not ("track" or "recording")) throw new ArgumentException("Choose a recording or release track, then review its proposed fields.");
        var extra = ImmutableDictionary<string, string>.Empty;
        if (number && row.Track > 0) extra = extra.Add("track", row.Track.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (date && row.Date.Length >= 4 && int.TryParse(row.Date[..4], out int year) && year is > 0 and <= 9999) extra = extra.Add("year", row.Date[..4]);
        var patch = new MetadataPatch(title ? row.Title : null, artist && row.Artist != "" ? row.Artist : null, album && row.Album != "" ? row.Album : null, Extra: extra); patch.Validate(); return patch;
    }
    public void Dispose() { _client.Dispose(); _artworkClient.Dispose(); }
}
