using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public enum PersonalServerKind { Jellyfin, Emby, Plex }
public sealed record ServerMediaRow(string Id, string Title, string Kind, bool Folder, string Artist = "", string Album = "", string Resource = "", string Extension = "");

internal sealed partial class PersonalMediaServer : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    private readonly Uri _root;
    private readonly PersonalServerKind _kind;
    private string _user;
    public PersonalMediaServer(PersonalServerKind kind, string root, string userId, string token)
    {
        _kind = kind; _root = RemoteMediaIO.Server(root); _user = userId.Trim();
        if (token.Length is < 1 or > 2048 || token.Any(char.IsControl)) throw new ArgumentException("Enter a bounded session token without control characters.");
        if (kind == PersonalServerKind.Jellyfin) _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Client=\"FluxPlayer\", Device=\"Windows\", DeviceId=\"" + Guid.NewGuid().ToString("N") + "\", Version=\"1.4.0\", Token=\"" + token.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
        else _client.DefaultRequestHeaders.Add(kind == PersonalServerKind.Plex ? "X-Plex-Token" : "X-Emby-Token", token);
        if (kind == PersonalServerKind.Plex) { _client.DefaultRequestHeaders.Add("X-Plex-Product", "FluxPlayer"); _client.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", Guid.NewGuid().ToString("N")); _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml")); }
    }
    private Uri Endpoint(string route)
    {
        var uri = new Uri(_root, route.TrimStart('/')); if (uri.Scheme != _root.Scheme || uri.Authority != _root.Authority || uri.UserInfo != "") throw new ArgumentException("The server returned a foreign media endpoint."); return uri;
    }
    private static string E(string value) => Uri.EscapeDataString(value);
    private static string S(JsonElement value, string key) => value.TryGetProperty(key, out var item) ? item.ToString()[..Math.Min(4096, item.ToString().Length)] : "";
    private async Task<JsonDocument> Json(string route, CancellationToken token) => await RemoteMediaIO.Json(_client, Endpoint(route), token).ConfigureAwait(false);
    private async Task<XElement> Xml(string route, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45)); using var response = await _client.GetAsync(Endpoint(route), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        using var input = new MemoryStream(await RemoteMediaIO.Bytes(response, 8 * 1024 * 1024, timeout.Token).ConfigureAwait(false)); using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 }); return XElement.Load(reader);
    }
    public async Task Connect(CancellationToken token)
    {
        if (_kind == PersonalServerKind.Plex) { _ = await Xml("identity", token).ConfigureAwait(false); return; }
        using var doc = await Json(_user == "" ? "Users/Me" : "Users/" + E(_user), token).ConfigureAwait(false); if (_user == "") _user = S(doc.RootElement, "Id");
        if (_user.Length is < 1 or > 128 || _user.Any(char.IsControl)) throw new IOException("The server did not identify the selected user.");
    }
    public async Task<ServerMediaRow[]> Browse(string view, string id, string query, CancellationToken token)
    {
        if (id.Length > 512 || query.Length > 500) throw new ArgumentException("Keep IDs and search text within their limits.");
        if (_kind == PersonalServerKind.Plex)
        {
            string route = view switch
            {
                "Roots" => "library/sections/all", "Children" when id.StartsWith("section:", StringComparison.Ordinal) => "library/sections/" + E(id[8..]) + "/all",
                "Children" => "library/metadata/" + E(id) + "/children", "Search" => "search?query=" + E(query), "Resume" => "library/onDeck",
                "Recent" => "library/recentlyAdded", "Playlists" => "playlists", "Playlist tracks" => "playlists/" + E(id) + "/items", _ => throw new ArgumentException("Choose a supported Plex view.")
            };
            var root = await Xml(route, token).ConfigureAwait(false);
            string A(XElement node, string name) => ((string?)node.Attribute(name) ?? "")[..Math.Min(4096, ((string?)node.Attribute(name) ?? "").Length)];
            return root.Descendants().Where(n => n.Name.LocalName is "Directory" or "Video" or "Track" or "Playlist").Take(200).Select(node =>
            {
                string type = A(node, "type"); bool folder = node.Name.LocalName is "Directory" or "Playlist";
                var part = node.Descendants().FirstOrDefault(n => n.Name.LocalName == "Part"); string resource = part == null ? "" : A(part, "key");
                string key = A(node, "ratingKey"); if (view == "Roots") key = "section:" + A(node, "key");
                return new ServerMediaRow(key, A(node, "title"), type == "" ? node.Name.LocalName : type, folder, A(node, "grandparentTitle"), A(node, "parentTitle"), resource, part?.Parent == null ? "" : A(part.Parent, "container"));
            }).ToArray();
        }
        string routeBase = "Users/" + E(_user) + "/";
        string request = view switch
        {
            "Roots" => routeBase + "Views", "Resume" => routeBase + "Items/Resume?Limit=100&MediaTypes=Video,Audio",
            "Children" => routeBase + "Items?ParentId=" + E(id) + "&Limit=200&Fields=MediaSources",
            "Movies" => routeBase + "Items?Recursive=true&IncludeItemTypes=Movie&Limit=200&Fields=MediaSources",
            "Series" => routeBase + "Items?Recursive=true&IncludeItemTypes=Series&Limit=200",
            "Music" => routeBase + "Items?Recursive=true&IncludeItemTypes=MusicAlbum&Limit=200",
            "Favorites" => routeBase + "Items?Recursive=true&Filters=IsFavorite&Limit=200&Fields=MediaSources",
            "Search" => routeBase + "Items?Recursive=true&SearchTerm=" + E(query) + "&Limit=100&Fields=MediaSources",
            _ => throw new ArgumentException("Choose a supported library view.")
        };
        if (_kind == PersonalServerKind.Jellyfin)
        {
            if (view == "Roots") request = "UserViews?UserId=" + E(_user);
            else if (view == "Resume") request = "UserItems/Resume?UserId=" + E(_user) + "&Limit=100&MediaTypes=Video,Audio";
            else request = request.Replace(routeBase + "Items?", "Items?UserId=" + E(_user) + "&", StringComparison.Ordinal);
        }
        using var result = await Json(request, token).ConfigureAwait(false);
        if (!result.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray().Take(200).Select(item =>
        {
            string type = S(item, "Type"); bool folder = item.TryGetProperty("IsFolder", out var flag) && flag.ValueKind == JsonValueKind.True || type is "Series" or "Season" or "MusicAlbum" or "MusicArtist" or "CollectionFolder";
            string extension = item.TryGetProperty("MediaSources", out var sources) && sources.ValueKind == JsonValueKind.Array && sources.GetArrayLength() > 0 ? S(sources[0], "Container") : "";
            return new ServerMediaRow(S(item, "Id"), S(item, "Name"), type, folder, item.TryGetProperty("Artists", out var artists) && artists.ValueKind == JsonValueKind.Array ? string.Join("; ", artists.EnumerateArray().Take(20).Select(a => a.ToString())) : "", S(item, "Album"), "", extension);
        }).ToArray();
    }
    public async Task<HttpResponseMessage> Stream(ServerMediaRow row, string? range, bool download, CancellationToken token)
    {
        if (row.Folder) throw new ArgumentException("Select a media item.");
        string route = _kind == PersonalServerKind.Plex ? row.Resource : download ? "Items/" + E(row.Id) + "/Download" : (row.Kind == "Audio" ? "Audio/" : "Videos/") + E(row.Id) + "/stream?static=true";
        if (route == "") throw new IOException("This item has no directly exposed media part.");
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(route)); if (range != null && RangeHeaderValue.TryParse(range, out var parsed)) request.Headers.Range = parsed;
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(token); headers.CancelAfter(TimeSpan.FromSeconds(45)); return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
    }
    public async Task Download(ServerMediaRow row, string destination, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(30));
        await OfflineMediaJob.Publish(destination, Path.GetExtension(destination), [], async staging => { using var response = await Stream(row, null, true, deadline.Token).ConfigureAwait(false); await RemoteMediaIO.CopyResponse(response, staging, 2L * 1024 * 1024 * 1024, deadline.Token).ConfigureAwait(false); }, deadline.Token).ConfigureAwait(false);
    }
    public void Dispose() { _client.DefaultRequestHeaders.Clear(); _client.Dispose(); }
}
