using FluxPlayer.Models;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record RemoteMusicRow(string Kind, string Id, string Title, string Artist = "", string Album = "", string Extension = "", long Bytes = 0);
internal sealed partial class SubsonicSource : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    private readonly Uri _root;
    private string _user, _password;
    public string Server => _root.ToString();
    public SubsonicSource(string address, string user, string password)
    { _root = RemoteMediaIO.Server(address); if (user.Length is < 1 or > 256 || password.Length > 1024) throw new ArgumentException("Enter a username and bounded session password."); _user = user; _password = password; }
    private Uri Endpoint(string method, params (string Key, string Value)[] args)
    {
        string salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string token = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(_password + salt))).ToLowerInvariant();
        var query = new[] { ("u", _user), ("t", token), ("s", salt), ("v", "1.16.1"), ("c", "FluxPlayer"), ("f", "json") }.Concat(args);
        return new Uri(_root, "rest/" + method + ".view?" + string.Join('&', query.Select(a => Uri.EscapeDataString(a.Item1) + "=" + Uri.EscapeDataString(a.Item2))));
    }
    private async Task<JsonDocument> Request(string method, CancellationToken token, params (string Key, string Value)[] args)
    {
        var result = await RemoteMediaIO.Json(_client, Endpoint(method, args), token).ConfigureAwait(false);
        if (!result.RootElement.TryGetProperty("subsonic-response", out var body) || !body.TryGetProperty("status", out var status) || status.GetString() != "ok") { result.Dispose(); throw new IOException("The Subsonic server rejected this request. Check server compatibility and session credentials."); }
        return result;
    }
    private static string S(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.ToString() : "";
    private static RemoteMusicRow Row(JsonElement item, string kind) => new(kind, S(item, "id"), S(item, "title") is { Length: > 0 } title ? title : S(item, "name"), S(item, "artist"), S(item, "album"), S(item, "suffix"), item.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) ? bytes : 0);
    private static IEnumerable<JsonElement> Array(JsonElement item, string key) => item.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : Enumerable.Empty<JsonElement>();
    public async Task Ping(CancellationToken token) { using var doc = await Request("ping", token).ConfigureAwait(false); }
    public async Task<RemoteMusicRow[]> Browse(string action, string id, CancellationToken token)
    {
        string method = action switch { "Artists" => "getArtists", "Artist albums" => "getArtist", "Album tracks" => "getAlbum", "Favorites" => "getStarred2", "Playlists" => "getPlaylists", "Playlist tracks" => "getPlaylist", _ => throw new ArgumentException("Choose a catalog action.") };
        using var doc = await Request(method, token, ("id", id)).ConfigureAwait(false); var body = doc.RootElement.GetProperty("subsonic-response"); IEnumerable<RemoteMusicRow> rows = [];
        if (action == "Artists") rows = Array(body.GetProperty("artists"), "index").SelectMany(i => Array(i, "artist")).Select(i => Row(i, "Artist"));
        if (action == "Artist albums") rows = Array(body.GetProperty("artist"), "album").Select(i => Row(i, "Album"));
        if (action == "Album tracks") rows = Array(body.GetProperty("album"), "song").Select(i => Row(i, "Track"));
        if (action == "Favorites") rows = Array(body.GetProperty("starred2"), "song").Select(i => Row(i, "Track"));
        if (action == "Playlists") rows = Array(body.GetProperty("playlists"), "playlist").Select(i => Row(i, "Playlist"));
        if (action == "Playlist tracks") rows = Array(body.GetProperty("playlist"), "entry").Select(i => Row(i, "Track"));
        return rows.Take(500).ToArray();
    }
    public async Task<RemoteMusicRow[]> Search(string text, CancellationToken token)
    {
        if (text.Length is < 1 or > 500) throw new ArgumentException("Enter a search of 1–500 characters.");
        using var doc = await Request("search3", token, ("query", text), ("songCount", "100"), ("albumCount", "0"), ("artistCount", "0")).ConfigureAwait(false);
        return Array(doc.RootElement.GetProperty("subsonic-response").GetProperty("searchResult3"), "song").Select(i => Row(i, "Track")).Take(100).ToArray();
    }
    public async Task<RemoteMusicRow[]> Albums(string type, CancellationToken token)
    {
        if (type is not ("newest" or "recent" or "random")) throw new ArgumentException("Choose a supported album view.");
        using var doc = await Request("getAlbumList2", token, ("type", type), ("size", "100")).ConfigureAwait(false);
        return Array(doc.RootElement.GetProperty("subsonic-response").GetProperty("albumList2"), "album").Select(i => Row(i, "Album")).Take(100).ToArray();
    }
    public async Task<HttpResponseMessage> Stream(string id, int bitrate, string? range, CancellationToken token)
    {
        if (id.Length is < 1 or > 512 || bitrate is not (0 or 64 or 128 or 192 or 320)) throw new ArgumentException("Choose a valid track and supported bitrate.");
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("stream", ("id", id), ("maxBitRate", bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("format", bitrate == 0 ? "raw" : "mp3")));
        if (range != null && System.Net.Http.Headers.RangeHeaderValue.TryParse(range, out var parsed)) request.Headers.Range = parsed;
        using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); headersDeadline.CancelAfter(TimeSpan.FromSeconds(45));
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType is "application/json" or "text/xml" or "application/xml") { response.Dispose(); throw new IOException("The server returned an API error instead of a media stream."); }
        return response;
    }
    public async Task Download(RemoteMusicRow row, string destination, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(20));
        await OfflineMediaJob.Publish(destination, Path.GetExtension(destination), [], async staging => { using var response = await Stream(row.Id, 0, null, deadline.Token).ConfigureAwait(false); await RemoteMediaIO.CopyResponse(response, staging, 1024L * 1024 * 1024, deadline.Token).ConfigureAwait(false); }, deadline.Token).ConfigureAwait(false);
    }
    internal static RemoteMusicRow[] DownloadSelection(IEnumerable<RemoteMusicRow> rows)
    {
        var selected = rows.Where(row => row.Kind == "Track").DistinctBy(row => row.Id, StringComparer.Ordinal).Take(51).ToArray();
        if (selected.Length is < 1 or > 50) throw new ArgumentException("Select 1–50 distinct tracks.");
        return selected;
    }
    public void Dispose() { _client.Dispose(); _password = _user = ""; _actionPlaylistReviews.Clear(); }
}
