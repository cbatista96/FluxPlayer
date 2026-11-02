using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace FluxPlayer.Services;

internal sealed partial class PersonalMediaServer
{
    public async Task<ServerActionRow[]> ExecuteAction(string key, IReadOnlyDictionary<string, string> input, CancellationToken token)
    {
        if (_kind == PersonalServerKind.Plex) return await ExecutePlexAction(key, input, token).ConfigureAwait(false);
        var definition = ServerActionCatalog.Personal.FirstOrDefault(d => d.Key == key) ?? throw new ArgumentException("Unknown library action.");
        var values = ServerActionCatalog.Validate(definition, input); string V(string name) => values[name];
        string id = values.GetValueOrDefault("id", ""); if (id.Length > 0) ActionIdentifier(id);
        ActionIdentifier(_user); string user = "UserId=" + E(_user), legacy = "Users/" + E(_user) + "/"; bool jellyfin = _kind == PersonalServerKind.Jellyfin;
        string route;
        if (definition.Writes)
        {
            HttpMethod method = key is "unfavorite" or "unplayed" or "rating-clear" ? HttpMethod.Delete : HttpMethod.Post; object? payload = null;
            route = key switch
            {
                "favorite" or "unfavorite" => jellyfin ? "UserFavoriteItems/" + E(id) + "?" + user : legacy + "FavoriteItems/" + E(id),
                "played" or "unplayed" => jellyfin ? "UserPlayedItems/" + E(id) + "?" + user : legacy + "PlayedItems/" + E(id),
                "like" or "rating-clear" => (jellyfin ? "UserItems/" + E(id) + "/Rating?" + user : legacy + "Items/" + E(id) + "/Rating?UserId=" + E(_user)) + (key == "like" ? "&Likes=" + V("likes") : ""),
                "playlist-create" => "Playlists", _ => throw new ArgumentException("Unknown library change.")
            };
            if (key == "playlist-create")
            {
                string[] ids = ServerActionCatalog.Lines(V("items")); foreach (string itemId in ids) ActionIdentifier(itemId);
                if (jellyfin) { foreach (string itemId in ids) if (!Guid.TryParse(itemId, out _)) throw new ArgumentException("Jellyfin playlist item IDs must be UUIDs."); payload = new { Name = V("name"), Ids = ids, UserId = _user, MediaType = V("type"), IsPublic = false }; }
                else route += "?UserId=" + E(_user) + "&Name=" + E(V("name")) + "&Ids=" + E(string.Join(',', ids)) + "&MediaType=" + E(V("type"));
            }
            byte[] bytes = await ActionRequest(method, route, payload, token).ConfigureAwait(false);
            if (key == "playlist-create" && bytes.Length > 0) { using var response = JsonDocument.Parse(bytes); var rows = ServerActionCatalog.JsonRows(response.RootElement); return rows.Select(row => row with { Title = V("name"), Kind = "Created playlist" }).ToArray(); }
            return ServerActionCatalog.Written(definition.Name, id);
        }
        string items = jellyfin ? "Items?" + user : legacy + "Items?UserId=" + E(_user);
        route = key switch
        {
            "genres" => "Genres?" + user + "&Limit=100&Recursive=true",
            "artists" => "Artists?" + user + "&Limit=100&Recursive=true",
            "album-artists" => "Artists/AlbumArtists?" + user + "&Limit=100&Recursive=true",
            "years" => "Years?" + user + "&Limit=100&Recursive=true",
            "studios" => "Studios?" + user + "&Limit=100&Recursive=true",
            "tags" => "Items/Filters2?" + user,
            "random" => items + "&Recursive=true&Limit=100&SortBy=Random&IncludeItemTypes=Movie,Episode,Audio",
            "latest" => jellyfin ? "Items/Latest?" + user + "&Limit=100" : legacy + "Items/Latest?Limit=100",
            "next-up" => "Shows/NextUp?" + user + "&Limit=100",
            "similar" => "Items/" + E(id) + "/Similar?" + user + "&Limit=100",
            "features" => jellyfin ? "Items/" + E(id) + "/SpecialFeatures?" + user : legacy + "Items/" + E(id) + "/SpecialFeatures",
            "trailers" => jellyfin ? "Items/" + E(id) + "/LocalTrailers?" + user : legacy + "Items/" + E(id) + "/LocalTrailers",
            "details" => jellyfin ? "Items/" + E(id) + "?" + user : legacy + "Items/" + E(id),
            _ => throw new ArgumentException("Unknown library view.")
        };
        using var document = await Json(route, token).ConfigureAwait(false); var root = document.RootElement;
        if (key == "tags") return ServerActionCatalog.JsonRows(root, "Tags");
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Items", out _) ? ServerActionCatalog.JsonRows(root, "Items") : ServerActionCatalog.JsonRows(root);
    }
    private static void ActionIdentifier(string value)
    { if (value.Length is < 1 or > 128 || value is "." or ".." || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))) throw new ArgumentException("Use an available server item/user identifier."); }
    private async Task<byte[]> ActionRequest(HttpMethod method, string route, object? payload, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var request = new HttpRequestMessage(method, Endpoint(route)); if (payload != null) request.Content = JsonContent.Create(payload);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        return await RemoteMediaIO.Bytes(response, 2 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
    }
    private async Task<ServerActionRow[]> ExecutePlexAction(string key, IReadOnlyDictionary<string, string> input, CancellationToken token)
    {
        var definition = ServerActionCatalog.Plex.FirstOrDefault(d => d.Key == key) ?? throw new ArgumentException("Unknown Plex operation.");
        var values = ServerActionCatalog.Validate(definition, input); string V(string name) => values[name];
        foreach (string field in new[] { "id", "section", "entry", "after", "item" }) if (values.TryGetValue(field, out var number) && (number.Length is < 1 or > 20 || !number.All(char.IsAsciiDigit))) throw new ArgumentException("Use a numeric Plex " + field + " identifier.");
        string id = values.GetValueOrDefault("id", ""), section = values.GetValueOrDefault("section", ""), route;
        if (definition.Writes)
        {
            HttpMethod method = key == "playlist-remove" ? HttpMethod.Delete : key == "playlist-create" ? HttpMethod.Post : HttpMethod.Put;
            route = key switch
            {
                "played" => ":/scrobble?identifier=com.plexapp.plugins.library&key=" + E(id),
                "unplayed" => ":/unscrobble?identifier=com.plexapp.plugins.library&key=" + E(id),
                "rating" => ":/rate?identifier=com.plexapp.plugins.library&key=" + E(id) + "&rating=" + E(V("rating")),
                "playlist-rename" => "playlists/" + E(id) + "?title=" + E(V("name")),
                "playlist-remove" => "playlists/" + E(id) + "/items/" + E(V("entry")),
                "playlist-move" => "playlists/" + E(id) + "/items/" + E(V("entry")) + "/move?after=" + E(V("after")),
                "playlist-create" => "playlists", _ => throw new ArgumentException("Unknown Plex change.")
            };
            if (key == "playlist-create")
            {
                var identity = await Xml("identity", token).ConfigureAwait(false); string machine = (string?)identity.Attribute("machineIdentifier") ?? ""; ActionIdentifier(machine);
                string uri = "server://" + machine + "/com.plexapp.plugins.library/library/metadata/" + V("item"); route += "?title=" + E(V("name")) + "&type=" + E(V("type")) + "&smart=0&uri=" + E(uri);
            }
            // Prefix ':' routes with './' so URI resolution cannot treat ':' as a scheme.
            byte[] bytes = await ActionRequest(method, route.StartsWith(":", StringComparison.Ordinal) ? "./" + route : route, null, token).ConfigureAwait(false);
            if (key == "playlist-create" && bytes.Length > 0) { var root = ParsePlexActionXml(bytes); return ServerActionCatalog.XmlRows(root); }
            return ServerActionCatalog.Written(definition.Name, id);
        }
        route = key switch
        {
            "details" => "library/metadata/" + E(id),
            "leaves" => "library/metadata/" + E(id) + "/allLeaves",
            "collections" => "library/sections/" + E(section) + "/collections",
            "collection-items" => "library/collections/" + E(id) + "/children",
            "genres" or "countries" or "decades" or "directors" or "actors" or "labels" => "library/sections/" + E(section) + "/" + (key switch { "genres" => "genre", "countries" => "country", "decades" => "decade", "directors" => "director", "actors" => "actor", _ => "label" }),
            "unwatched" => "library/sections/" + E(section) + "/all?unwatched=1",
            "playlist-entries" => "playlists/" + E(id) + "/items",
            "similar" => "library/metadata/" + E(id) + "/similar?count=100",
            _ => throw new ArgumentException("Unknown Plex view.")
        };
        route += (route.Contains('?') ? "&" : "?") + "X-Plex-Container-Start=0&X-Plex-Container-Size=100";
        return ServerActionCatalog.XmlRows(await Xml(route, token).ConfigureAwait(false));
    }
    private static System.Xml.Linq.XElement ParsePlexActionXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 }); return System.Xml.Linq.XElement.Load(reader);
    }
}
