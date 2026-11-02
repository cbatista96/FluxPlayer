using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

internal sealed partial class SubsonicSource
{
    private sealed record PlaylistActionReview(string Signature, int Displayed, DateTimeOffset At);
    private readonly ConcurrentDictionary<string, PlaylistActionReview> _actionPlaylistReviews = new(StringComparer.Ordinal);
    private static string PlaylistSignature(JsonElement body)
    {
        if (!body.TryGetProperty("playlist", out var playlist) || playlist.ValueKind != JsonValueKind.Object) throw new IOException("The server did not return a playlist for review.");
        if (!playlist.TryGetProperty("entry", out var entries)) return Convert.ToHexString(SHA256.HashData(System.Array.Empty<byte>()));
        if (entries.ValueKind != JsonValueKind.Array) throw new IOException("The server did not return valid playlist entries for review.");
        var ids = entries.EnumerateArray().Take(10001).Select(e => e.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number ? id.ToString() : throw new IOException("A playlist entry has no stable ID.")).ToArray();
        if (ids.Any(id => id.Length is < 1 or > 512 || id.Any(char.IsControl))) throw new IOException("A playlist entry ID exceeds the review bound.");
        if (ids.Length > 10000) throw new IOException("Review/removal in this panel is limited to playlists of at most 10,000 entries.");
        string signature = string.Concat(ids.Select(id => id.Length.ToString(CultureInfo.InvariantCulture) + ":" + id)); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
    }
    public async Task<ServerActionRow[]> ExecuteAction(string key, IReadOnlyDictionary<string, string> input, CancellationToken token)
    {
        var definition = ServerActionCatalog.Subsonic.FirstOrDefault(d => d.Key == key) ?? throw new ArgumentException("Unknown Subsonic operation.");
        var values = ServerActionCatalog.Validate(definition, input); var parameters = new List<(string Key, string Value)>();
        string V(string name) => values[name]; void Add(string name, string value) => parameters.Add((name, value));
        string method; string[] path;
        switch (key)
        {
            case "genres": method = "getGenres"; path = ["genres", "genre"]; break;
            case "genre-songs": method = "getSongsByGenre"; Add("genre", V("genre")); Add("count", "100"); Add("offset", V("offset")); path = ["songsByGenre", "song"]; break;
            case "random-songs": method = "getRandomSongs"; Add("size", "100"); path = ["randomSongs", "song"]; break;
            case "top-songs": method = "getTopSongs"; Add("artist", V("artist")); Add("count", "100"); path = ["topSongs", "song"]; break;
            case "similar-songs": method = "getSimilarSongs2"; Add("id", V("id")); Add("count", "100"); path = ["similarSongs2", "song"]; break;
            case "artist-info": method = "getArtistInfo2"; Add("id", V("id")); Add("count", "20"); Add("includeNotPresent", "false"); path = ["artistInfo2"]; break;
            case "album-info": method = "getAlbumInfo2"; Add("id", V("id")); path = ["albumInfo"]; break;
            case "song-info": method = "getSong"; Add("id", V("id")); path = ["song"]; break;
            case "search-albums": case "search-artists":
                method = "search3"; Add("query", V("query")); Add("songCount", "0"); Add("albumCount", key == "search-albums" ? "100" : "0"); Add("artistCount", key == "search-artists" ? "100" : "0"); path = ["searchResult3", key == "search-albums" ? "album" : "artist"]; break;
            case "playlist-entries": method = "getPlaylist"; Add("id", V("id")); path = ["playlist", "entry"]; break;
            case "rated-albums": case "frequent-albums": case "year-albums": case "genre-albums":
                method = "getAlbumList2"; Add("size", "100"); Add("offset", V("offset"));
                Add("type", key switch { "rated-albums" => "highest", "frequent-albums" => "frequent", "year-albums" => "byYear", _ => "byGenre" });
                if (key == "year-albums") { Add("fromYear", V("from")); Add("toYear", V("to")); } if (key == "genre-albums") Add("genre", V("genre")); path = ["albumList2", "album"]; break;
            case "star": case "unstar": method = key; Add("id", V("id")); path = []; break;
            case "rating": method = "setRating"; Add("id", V("id")); Add("rating", V("rating")); path = []; break;
            case "now-playing": method = "scrobble"; Add("id", V("id")); Add("submission", "false"); path = []; break;
            case "scrobble":
                long time = long.Parse(V("time"), CultureInfo.InvariantCulture), now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (time > now || time < now - 14L * 86400000) throw new ArgumentException("Use an actual listen time within the past fourteen days.");
                method = "scrobble"; Add("id", V("id")); Add("time", V("time")); Add("submission", "true"); path = []; break;
            case "playlist-create": method = "createPlaylist"; Add("name", V("name")); foreach (string id in ServerActionCatalog.Lines(V("songs"))) Add("songId", id); path = ["playlist"]; break;
            case "playlist-add": method = "updatePlaylist"; Add("playlistId", V("id")); foreach (string id in ServerActionCatalog.Lines(V("songs"))) Add("songIdToAdd", id); path = []; break;
            case "playlist-rename": method = "updatePlaylist"; Add("playlistId", V("id")); Add("name", V("name")); path = []; break;
            case "playlist-visibility": method = "updatePlaylist"; Add("playlistId", V("id")); Add("public", V("public")); path = []; break;
            case "playlist-remove": method = "updatePlaylist"; Add("playlistId", V("id")); foreach (string index in ServerActionCatalog.Lines(V("indices")).OrderByDescending(i => int.Parse(i, CultureInfo.InvariantCulture))) Add("songIndexToRemove", index); path = []; break;
            case "bookmarks": method = "getBookmarks"; path = ["bookmarks", "bookmark"]; break;
            case "bookmark-save": method = "createBookmark"; Add("id", V("id")); Add("position", V("position")); Add("comment", V("comment")); path = []; break;
            case "bookmark-remove": method = "deleteBookmark"; Add("id", V("id")); path = []; break;
            case "lyrics": method = "getLyrics"; Add("artist", V("artist")); Add("title", V("title")); path = ["lyrics"]; break;
            case "music-folders": method = "getMusicFolders"; path = ["musicFolders", "musicFolder"]; break;
            default: throw new ArgumentException("Unsupported Subsonic operation.");
        }
        if (key == "playlist-remove")
        {
            if (!_actionPlaylistReviews.TryRemove(V("id"), out var review) || DateTimeOffset.UtcNow - review.At > TimeSpan.FromMinutes(5)) throw new ArgumentException("Read Remote playlist occurrence inspection in this session immediately before removal.");
            if (ServerActionCatalog.Lines(V("indices")).Any(index => int.Parse(index, CultureInfo.InvariantCulture) >= review.Displayed)) throw new ArgumentException("Remove only positions displayed in the last occurrence review (first 100 entries).");
            using var current = await Request("getPlaylist", token, ("id", V("id"))).ConfigureAwait(false);
            if (PlaylistSignature(current.RootElement.GetProperty("subsonic-response")) != review.Signature) throw new IOException("Playlist order changed. Read its occurrences again and review the positions.");
        }
        token.ThrowIfCancellationRequested();
        using var document = await Request(method, token, parameters.ToArray()).ConfigureAwait(false); var body = document.RootElement.GetProperty("subsonic-response");
        if (definition.Writes && path.Length == 0) return ServerActionCatalog.Written(definition.Name, values.GetValueOrDefault("id", ""));
        if (key == "bookmarks" && body.TryGetProperty("bookmarks", out var bookmarks) && bookmarks.TryGetProperty("bookmark", out var array) && array.ValueKind == JsonValueKind.Array)
            return array.EnumerateArray().Take(100).Select(b => { var entry = b.TryGetProperty("entry", out var e) ? ServerActionCatalog.JsonRow(e) : new ServerActionRow("", "", "", ""); string detail = ServerActionCatalog.JsonRow(b).Detail; return entry with { Kind = "Bookmark", Detail = detail + " · " + entry.Detail }; }).ToArray();
        var rows = ServerActionCatalog.JsonRows(body, path);
        if (key == "playlist-entries") { token.ThrowIfCancellationRequested(); if (_actionPlaylistReviews.Count >= 16) _actionPlaylistReviews.Clear(); _actionPlaylistReviews[V("id")] = new(PlaylistSignature(body), rows.Length, DateTimeOffset.UtcNow); }
        return key == "playlist-entries" ? rows.Select((row, index) => row with { Occurrence = index.ToString(CultureInfo.InvariantCulture) }).ToArray() : rows;
    }
}
