using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed record ServerActionField(string Key, string Label, string Kind = "text", string Default = "", long Minimum = 0, long Maximum = 0, ImmutableArray<string> Choices = default);
public sealed record ServerActionDefinition(string Key, string Name, string Group, bool Writes, string Original, string Scope, ImmutableArray<ServerActionField> Fields)
{ public override string ToString() => Name; }
public sealed record ServerActionRow(string Id, string Title, string Kind, string Detail, string Occurrence = "");
public sealed record ServerActionReport(string Provider, string Operation, string Scope, DateTimeOffset RetrievedUtc, ImmutableArray<ServerActionRow> Rows);

/// <summary>Explicit operations only. Definitions constrain inputs; adapters own fixed endpoints and HTTP methods.</summary>
public static class ServerActionCatalog
{
    private static ServerActionField F(string key, string label, string kind = "text", string value = "", long min = 0, long max = 0) => new(key, label, kind, value, min, max);
    private static ServerActionField C(string key, string label, params string[] choices) => new(key, label, "choice", choices[0], Choices: choices.ToImmutableArray());
    private static ServerActionDefinition A(string key, string name, string group, bool write, string original, string scope, params ServerActionField[] fields) => new(key, name, group, write, original, scope, fields.ToImmutableArray());
    public static ImmutableArray<ServerActionDefinition> Subsonic { get; } = [
        A("genres", "Genre catalog with server counts", "Browse", false, "R0135", "Read the server genre catalog and its song/album counts."),
        A("genre-songs", "Tracks by remote genre", "Browse", false, "R0135", "Request one bounded genre-song page; no local genre inference.", F("genre", "Genre"), F("offset", "Page offset", "integer", "0", 0, 1000000)),
        A("random-songs", "Random remote song selection", "Discovery", false, "R0134", "Request up to 100 server-selected random songs."),
        A("top-songs", "Artist top-song discovery", "Discovery", false, "R0135", "Read server/Last.fm-backed top songs for an explicitly supplied artist name.", F("artist", "Artist name")),
        A("similar-songs", "Related tracks from a song seed", "Discovery", false, "R0523", "Request getSimilarSongs2 for a selected song ID; results depend on server similarity support.", F("id", "Seed song ID")),
        A("artist-info", "Remote artist biography and links", "Metadata", false, "R0127", "Read bounded artist biography and descriptive links without opening external links.", F("id", "Artist ID")),
        A("album-info", "Remote album notes and links", "Metadata", false, "R0485", "Read server album notes and MusicBrainz/Last.fm references.", F("id", "Album ID")),
        A("song-info", "Detailed remote song metadata", "Metadata", false, "R0232", "Read selected song metadata, duration, track/disc and rating fields.", F("id", "Song ID")),
        A("search-albums", "Remote album text search", "Search", false, "R0135", "Use search3 album results rather than the existing track-only search.", F("query", "Album search")),
        A("search-artists", "Remote artist text search", "Search", false, "R0135", "Use search3 artist results rather than the existing track-only search.", F("query", "Artist search")),
        A("rated-albums", "Highest-rated remote albums", "Discovery", false, "R0134", "Request a highest-rated album page from the server.", F("offset", "Page offset", "integer", "0", 0, 1000000)),
        A("frequent-albums", "Frequently played remote albums", "Discovery", false, "R0109", "Request server most-played albums, independent of FluxPlayer local history.", F("offset", "Page offset", "integer", "0", 0, 1000000)),
        A("playlist-entries", "Remote playlist occurrence inspection", "Playlists", false, "R0181", "Read ordered song entries with zero-based occurrence positions for reviewed removal.", F("id", "Playlist ID")),
        A("year-albums", "Remote album year-range browsing", "Browse", false, "R0135", "Request one page between explicit release-year bounds.", F("from", "From year", "integer", "2000", 0, 9999), F("to", "To year", "integer", "2026", 0, 9999), F("offset", "Page offset", "integer", "0", 0, 1000000)),
        A("genre-albums", "Remote albums by genre", "Browse", false, "R0135", "Request one getAlbumList2 byGenre page.", F("genre", "Genre"), F("offset", "Page offset", "integer", "0", 0, 1000000)),
        A("star", "Add a remote song favorite", "Favorites and ratings", true, "R0180", "Explicitly star one remote song; does not synchronize the local favorites index.", F("id", "Song ID")),
        A("unstar", "Remove a remote song favorite", "Favorites and ratings", true, "R0180", "Explicitly unstar one remote song without deleting media.", F("id", "Song ID")),
        A("rating", "Set or clear a remote song rating", "Favorites and ratings", true, "R0180", "Set the server's 0–5 rating; zero clears it. No local-rating synchronization.", F("id", "Song ID"), F("rating", "Rating (0 clears)", "integer", "3", 0, 5)),
        A("now-playing", "Explicit server playing-now report", "Listening reports", true, "R0215", "Send a single scrobble submission=false. Not automatic background reporting.", F("id", "Actually playing song ID")),
        A("scrobble", "Reviewed remote listening submission", "Listening reports", true, "R0215", "Submit one explicitly confirmed listen using a supplied past Unix-millisecond time. No automatic retry after ambiguous failure.", F("id", "Listened song ID"), F("time", "Listen start (Unix milliseconds)", "integer", "0", 1, 4102444800000)),
        A("playlist-create", "Create a remote song playlist", "Playlists", true, "R0181", "Create a named server playlist from 1–50 ordered song IDs.", F("name", "New playlist name"), F("songs", "Ordered song IDs (one per line)", "ids")),
        A("playlist-add", "Append songs to a remote playlist", "Playlists", true, "R0181", "Append 1–50 explicitly ordered song IDs without replacing existing entries.", F("id", "Playlist ID"), F("songs", "Song IDs to append (one per line)", "ids")),
        A("playlist-rename", "Rename a remote playlist", "Playlists", true, "R0181", "Update only a selected remote playlist's name.", F("id", "Playlist ID"), F("name", "New playlist name")),
        A("playlist-visibility", "Set remote playlist visibility", "Playlists", true, "R0181", "Explicit public/private flag for the selected server playlist.", F("id", "Playlist ID"), C("public", "Visibility", "false", "true")),
        A("playlist-remove", "Remove selected remote playlist positions", "Playlists", true, "R0181", "Remove 1–50 displayed zero-based positions after a fresh occurrence review and order recheck; not media deletion. Server-side atomic concurrency is not guaranteed.", F("id", "Playlist ID"), F("indices", "Zero-based positions (one per line)", "indices")),
        A("bookmarks", "Read remote audiobook bookmarks", "Bookmarks", false, "R0221", "Read server bookmarks and positions, bounded to 100 displayed entries."),
        A("bookmark-save", "Save a remote media bookmark", "Bookmarks", true, "R0221", "Create/update the server bookmark for an item with a millisecond position.", F("id", "Song or audiobook ID"), F("position", "Position (ms)", "integer", "0", 0, 86400000), F("comment", "Bookmark comment")),
        A("bookmark-remove", "Remove a remote media bookmark", "Bookmarks", true, "R0221", "Remove only the selected server bookmark, leaving the media intact.", F("id", "Song or audiobook ID")),
        A("lyrics", "Read server-provided song lyrics", "Metadata", false, "R0051", "Request authored lyrics by artist/title; not transcription or provider scraping.", F("artist", "Artist"), F("title", "Song title")),
        A("music-folders", "Browse remote music-folder roots", "Browse", false, "R0068", "Read available logical music folders and their server IDs.")
    ];
    // Each definition is implemented separately for the two server protocols.
    public static ImmutableArray<ServerActionDefinition> Personal { get; } = [
        A("genres", "Server genre navigation", "Browse", false, "R1275", "Read a bounded genre catalog for the selected user."),
        A("artists", "Server music-artist navigation", "Browse", false, "R1275", "Read music artist entities, distinct from album-root navigation."),
        A("album-artists", "Server album-artist navigation", "Browse", false, "R1275", "Read server album-artist entities with stable IDs."),
        A("years", "Server release-year navigation", "Browse", false, "R1275", "Read available production-year facets."),
        A("studios", "Server studio navigation", "Browse", false, "R1275", "Read studio entities for movie and television browsing."),
        A("tags", "Server descriptive-tag facets", "Browse", false, "R1275", "Read tag names from item-filter metadata; no tag editing."),
        A("random", "Randomized server media discovery", "Discovery", false, "R0134", "Request a bounded random-order recursive media view."),
        A("latest", "Latest additions for the selected user", "Discovery", false, "R1275", "Read selected-user latest media, bounded to 100 displayed rows."),
        A("next-up", "Server next-up television episodes", "Discovery", false, "R1275", "Read the server's next-up episode view; not automatic queue replacement."),
        A("similar", "Server related-item discovery", "Discovery", false, "R0523", "Read similar media for a selected seed item ID.", F("id", "Seed item ID")),
        A("features", "Selected-item special-feature browsing", "Metadata", false, "R1275", "Read a selected item's local special-feature entries.", F("id", "Item ID")),
        A("trailers", "Selected-item local-trailer browsing", "Metadata", false, "R1275", "Read server-local trailers; does not contact commercial trailer services.", F("id", "Item ID")),
        A("details", "Selected-user item metadata inspection", "Metadata", false, "R0127", "Read descriptive item and user-state fields through the selected server account.", F("id", "Item ID")),
        A("favorite", "Mark a server item favorite", "Favorites and state", true, "R0180", "Set one server favorite explicitly, without automatic local synchronization.", F("id", "Item ID")),
        A("unfavorite", "Remove a server item favorite", "Favorites and state", true, "R0180", "Clear one server favorite without deleting media.", F("id", "Item ID")),
        A("played", "Mark a server item played", "Favorites and state", true, "R0724", "Explicitly mark watched/listened state for one server item.", F("id", "Item ID")),
        A("unplayed", "Mark a server item unplayed", "Favorites and state", true, "R0724", "Explicitly clear watched/listened state for one server item.", F("id", "Item ID")),
        A("like", "Set a server item like or dislike", "Favorites and state", true, "R0180", "Write the server's boolean Likes rating; not a numeric star score.", F("id", "Item ID"), C("likes", "Liked", "true", "false")),
        A("rating-clear", "Clear a server personal rating", "Favorites and state", true, "R0180", "Remove the selected user's server rating for one item.", F("id", "Item ID")),
        A("playlist-create", "Create a user-owned server playlist", "Playlists", true, "R0181", "Create a playlist from 1–50 explicit ordered item IDs, without replacing an existing playlist.", F("name", "Playlist name"), F("items", "Ordered item IDs (one per line)", "ids"), C("type", "Media type", "Audio", "Video"))
    ];
    public static ImmutableArray<ServerActionDefinition> Plex { get; } = [
        A("details", "Plex selected-item metadata inspection", "Metadata", false, "R0538", "Inspect bounded descriptive and user-state attributes for a metadata ID.", F("id", "Metadata rating key")),
        A("leaves", "Plex recursive episode and track leaves", "Browse", false, "R0538", "Read up to 100 leaf items beneath a show, season, artist or album.", F("id", "Parent metadata rating key")),
        A("collections", "Plex library collection browsing", "Browse", false, "R0538", "List collections for a selected section.", F("section", "Library section ID")),
        A("collection-items", "Plex collection-member browsing", "Browse", false, "R0538", "Read a collection's children rather than normal library-directory children.", F("id", "Collection rating key")),
        A("genres", "Plex genre facets", "Browse", false, "R0538", "Read section genre facets and filter keys.", F("section", "Library section ID")),
        A("countries", "Plex production-country facets", "Browse", false, "R0538", "Read section country facets.", F("section", "Library section ID")),
        A("decades", "Plex release-decade facets", "Browse", false, "R0538", "Read section decade facets.", F("section", "Library section ID")),
        A("directors", "Plex director facets", "Browse", false, "R0538", "Read section director facets, subject to media type.", F("section", "Library section ID")),
        A("actors", "Plex cast-member facets", "Browse", false, "R0538", "Read section actor facets, subject to media type.", F("section", "Library section ID")),
        A("labels", "Plex personal-label facets", "Browse", false, "R0538", "Read existing section label facets; no global metadata writes.", F("section", "Library section ID")),
        A("unwatched", "Plex unwatched-item view", "Discovery", false, "R0724", "Request the section's unplayed-only media view.", F("section", "Library section ID")),
        A("playlist-entries", "Plex playlist occurrence inspection", "Playlists", false, "R0181", "Read playlist entries with playlistItemID occurrence identifiers for subsequent remove/move operations.", F("id", "Playlist rating key")),
        A("similar", "Plex related-item browsing", "Discovery", false, "R0523", "Read server similar items for a seed metadata ID.", F("id", "Seed rating key")),
        A("played", "Plex explicit played-state marking", "Favorites and state", true, "R0724", "Send one scrobble state operation using the library identifier.", F("id", "Item rating key")),
        A("unplayed", "Plex explicit unplayed-state marking", "Favorites and state", true, "R0724", "Send one unscrobble operation; no media deletion.", F("id", "Item rating key")),
        A("rating", "Plex personal rating update", "Favorites and state", true, "R0180", "Set one user rating from 0 to 10; does not update FluxPlayer's 0–5 ratings.", F("id", "Item rating key"), F("rating", "Rating (0–10)", "integer", "5", 0, 10)),
        A("playlist-rename", "Plex playlist renaming", "Playlists", true, "R0181", "Change only a selected playlist's title.", F("id", "Playlist rating key"), F("name", "New title")),
        A("playlist-remove", "Plex playlist occurrence removal", "Playlists", true, "R0181", "Remove one playlistItemID occurrence; the media rating key is not an occurrence ID.", F("id", "Playlist rating key"), F("entry", "Playlist item occurrence ID")),
        A("playlist-move", "Plex playlist occurrence reordering", "Playlists", true, "R0181", "Move one playlistItemID after another; zero means first position.", F("id", "Playlist rating key"), F("entry", "Occurrence to move"), F("after", "After occurrence ID (0 = first)")),
        A("playlist-create", "Plex playlist creation from media", "Playlists", true, "R0181", "Build one playlist from a selected local Plex metadata key and server identity.", F("name", "New playlist title"), F("item", "Initial media rating key"), C("type", "Playlist media type", "audio", "video"))
    ];
    public static ImmutableArray<ServerActionDefinition> Upnp { get; } = [
        A("metadata", "UPnP selected-object metadata", "Content directory", false, "R0059", "BrowseMetadata for one selected object without browsing its children.", F("id", "Object ID", value: "0")),
        A("search-capabilities", "UPnP searchable-field capabilities", "Content directory", false, "R0059", "Read SearchCaps from the selected ContentDirectory service."),
        A("sort-capabilities", "UPnP sortable-field capabilities", "Content directory", false, "R0059", "Read SortCaps from the selected ContentDirectory service."),
        A("update-id", "UPnP content-directory revision snapshot", "Content directory", false, "R0059", "Read SystemUpdateID explicitly; no event subscription or polling."),
        A("media-info", "UPnP renderer media information", "Renderer inspection", false, "R0059", "Read current/next URI and track-count metadata for instance zero."),
        A("device-capabilities", "UPnP renderer media capabilities", "Renderer inspection", false, "R0059", "Read supported playback/recording media and quality modes."),
        A("transport-settings", "UPnP renderer transport settings", "Renderer inspection", false, "R0059", "Read play mode and recording-quality mode."),
        A("transport-actions", "UPnP currently available transport actions", "Renderer inspection", false, "R0059", "Read device-reported currently valid AVTransport commands."),
        A("play-mode", "UPnP renderer repeat and shuffle mode", "Renderer control", true, "R0059", "Request a standardized play mode; device support is conditional.", C("mode", "Play mode", "NORMAL", "REPEAT_ONE", "REPEAT_ALL", "RANDOM", "SHUFFLE")),
        A("next-uri", "UPnP next-resource staging", "Renderer control", true, "R0059", "SetNextAVTransportURI for an explicit HTTP(S) resource with empty metadata; not synchronized or guaranteed gapless playback.", F("uri", "Next resource URL", "url"))
    ];
    public static ImmutableArray<ServerActionDefinition> For(string provider) => provider switch { "Subsonic" => Subsonic, "Jellyfin" or "Emby" => Personal, "Plex" => Plex, "UPnP" => Upnp, _ => throw new ArgumentException("Unknown provider.") };
    public static Dictionary<string, string> Validate(ServerActionDefinition definition, IReadOnlyDictionary<string, string> input)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in definition.Fields)
        {
            string value = input.TryGetValue(field.Key, out var found) ? found.Trim() : field.Default;
            if (value.Length is < 1 or > 32768 || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new ArgumentException("Enter " + field.Label + ".");
            if (field.Kind == "integer") { if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) || number < field.Minimum || number > field.Maximum) throw new ArgumentException("Check " + field.Label + "."); value = number.ToString(CultureInfo.InvariantCulture); }
            else if (field.Kind == "choice") { if (!field.Choices.Contains(value)) throw new ArgumentException("Choose a supported " + field.Label + "."); }
            else if (field.Kind is "ids" or "indices") { var values = Lines(value); if (values.Length is < 1 or > 50 || values.Any(v => v.Length > 512 || v.Any(char.IsControl)) || field.Kind == "indices" && values.Any(v => !int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index is < 0 or > 1000000)) throw new ArgumentException("Enter 1–50 valid values for " + field.Label + "."); if (field.Kind == "indices") { values = values.Select(v => int.Parse(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)).ToArray(); if (values.Distinct().Count() != values.Length) throw new ArgumentException("Do not repeat a playlist index."); value = string.Join('\n', values); } }
            else if (field.Kind == "url") { if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo != "" || uri.Fragment != "" || value.Length > 4096) throw new ArgumentException("Use an HTTP(S) resource URL without credentials or fragments."); }
            else if (value.Length > 512 || value.Any(char.IsControl)) throw new ArgumentException("Keep " + field.Label + " within 512 characters without control characters.");
            output[field.Key] = value;
        }
        if (output.TryGetValue("from", out var from) && output.TryGetValue("to", out var to) && long.Parse(from, CultureInfo.InvariantCulture) > long.Parse(to, CultureInfo.InvariantCulture)) throw new ArgumentException("From year must not exceed To year.");
        return output;
    }
    internal static string[] Lines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    internal static string Bound(string? value, int max = 4096) => value == null ? "" : value[..Math.Min(value.Length, max)];
    internal static ServerActionRow[] JsonRows(JsonElement root, params string[] container)
    {
        foreach (string key in container) { if (!root.TryGetProperty(key, out var next)) return []; root = next; }
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Take(100).Select(JsonRow).ToArray();
        return root.ValueKind == JsonValueKind.Object ? [JsonRow(root)] : [];
    }
    internal static ServerActionRow JsonRow(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String) return new(Bound(item.GetString()), Bound(item.GetString()), "Facet", "");
        if (item.ValueKind != JsonValueKind.Object) return new("", Bound(item.ToString()), "Value", "");
        string Read(params string[] names) { foreach (string name in names) if (item.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null)) return Bound(value.ToString()); return ""; }
        string[] allowed = ["artist", "album", "genre", "year", "duration", "track", "discNumber", "songCount", "albumCount", "playCount", "userRating", "position", "comment", "biography", "notes", "musicBrainzId", "lastFmUrl", "mbid", "title", "artistCount", "value", "Type", "ProductionYear", "RunTimeTicks", "OfficialRating", "CommunityRating", "Overview", "Album", "AlbumArtist", "IndexNumber", "ParentIndexNumber"];
        var details = item.EnumerateObject().Where(p => allowed.Contains(p.Name) && p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)).Select(p => p.Name + ": " + Bound(p.Value.ToString(), 1500)).ToList();
        if (item.TryGetProperty("UserData", out var user) && user.ValueKind == JsonValueKind.Object) foreach (string key in new[] { "Played", "IsFavorite", "Likes", "PlaybackPositionTicks", "PlayCount" }) if (user.TryGetProperty(key, out var value)) details.Add(key + ": " + Bound(value.ToString(), 100));
        return new(Read("Id", "id"), Read("Name", "title", "name", "value"), Read("Type", "type"), Bound(string.Join(" · ", details)));
    }
    internal static ServerActionRow[] XmlRows(XElement root)
    {
        string A(XElement node, string key) => Bound((string?)node.Attribute(key));
        return root.DescendantsAndSelf().Where(e => e.Name.LocalName is "Directory" or "Video" or "Track" or "Playlist" or "Collection").Take(100).Select(e => new ServerActionRow(A(e, "ratingKey") is { Length: > 0 } id ? id : A(e, "key"), A(e, "title"), A(e, "type"), Bound(string.Join(" · ", e.Attributes().Where(a => new[] { "year", "duration", "viewCount", "userRating", "parentTitle", "grandparentTitle", "summary", "leafCount", "viewedLeafCount" }.Contains(a.Name.LocalName)).Select(a => a.Name.LocalName + ": " + Bound(a.Value, 1500)))), A(e, "playlistItemID"))).ToArray();
    }
    internal static ServerActionRow[] Written(string operation, string id = "") => [new(id, "Request accepted", "Remote change", operation + ". Refresh the relevant server view to inspect its current state.")];
    public static Task Export(ServerActionReport report, string path, CancellationToken token)
    {
        if (report.Rows.Length > 100) throw new ArgumentException("The report exceeds its row bound."); string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }); if (System.Text.Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) throw new IOException("The report exceeds 2 MiB.");
        return OfflineMediaJob.Publish(path, ".json", [], staging => File.WriteAllTextAsync(staging, json, token), token);
    }
}
