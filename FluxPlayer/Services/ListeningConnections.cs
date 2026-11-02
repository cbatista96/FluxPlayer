using FluxPlayer.Models;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record ListenDraft(string Id, string Provider, string Owner, string Artist, string Track, string Album, long Started, long DurationSeconds, long PlayedSeconds, string State = "Pending");
public sealed record ListenOutboxData(ImmutableArray<ListenDraft> Rows);
public sealed record ListeningRow(string Artist, string Track, string Album, string Detail, string RecordingId = "");
public sealed class ListenOutbox
{
    private readonly ProfileDocument<ListenOutboxData> _store;
    public ListenOutbox(string profile) => _store = new(Path.Combine(profile, "listen-outbox.json"), () => new([]), data =>
    { if (data.Rows.IsDefault || data.Rows.Length > 500 || data.Rows.Select(r => r.Id).Distinct().Count() != data.Rows.Length) throw new ArgumentException("Keep at most 500 distinct outbox entries."); foreach (var row in data.Rows) Validate(row); }, 2 * 1024 * 1024);
    public static void Validate(ListenDraft row)
    {
        if (row == null || !Guid.TryParseExact(row.Id, "N", out _) || row.Provider is not ("ListenBrainz" or "Last.fm") || string.IsNullOrWhiteSpace(row.Owner) || row.Owner.Length > 256 || string.IsNullOrWhiteSpace(row.Artist) || row.Artist.Length > 1000 || string.IsNullOrWhiteSpace(row.Track) || row.Track.Length > 1000 || row.Album == null || row.Album.Length > 1000 || row.Started < 0 || row.Started > DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds() || row.DurationSeconds is < 31 or > 86400 || row.PlayedSeconds < Math.Min((row.DurationSeconds + 1) / 2, 240) || row.PlayedSeconds > 86400 || row.State is not ("Pending" or "Submitted" or "Uncertain")) throw new ArgumentException("Use an identified owner/track, a past start time, duration over 30 seconds and listened time of at least half the track or four minutes.");
    }
    public Task<ListenOutboxData> Read(CancellationToken token) => _store.Read(token);
    public Task Stage(IEnumerable<ListenDraft> input, CancellationToken token)
    {
        var rows = input.Take(501).ToArray(); foreach (var row in rows) Validate(row);
        return _store.Edit(data => new(data.Rows.Concat(rows).GroupBy(r => (r.Provider, r.Owner, r.Artist, r.Track, r.Started)).Select(g => g.First()).ToImmutableArray()), token);
    }
    public Task State(string id, string value, CancellationToken token) => _store.Edit(data => new(data.Rows.Select(r => r.Id == id ? r with { State = value } : r).ToImmutableArray()), token);
    public Task Remove(IEnumerable<string> ids, CancellationToken token) { var selected = ids.ToHashSet(StringComparer.Ordinal); return _store.Edit(data => new(data.Rows.Where(r => !selected.Contains(r.Id)).ToImmutableArray()), token); }
    public static ListenDraft[] FromHistory(IEnumerable<PlaybackHistoryEntry> history, string provider, string owner) => history.Where(r => r.Kind == "Audio" && r.DurationMs >= 31000 && r.PlayedMs / 1000 >= Math.Min((r.DurationMs / 1000 + 1) / 2, 240) && r.Artist != "" && r.Title != "" && (provider != "Last.fm" || r.StartedUtc >= DateTimeOffset.UtcNow.AddDays(-14))).Take(100).Select(r => new ListenDraft(Guid.NewGuid().ToString("N"), provider, owner, r.Artist, r.Title, "", r.StartedUtc.ToUnixTimeSeconds(), r.DurationMs / 1000, r.PlayedMs / 1000)).ToArray();
}

internal sealed class ListenBrainzConnection : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    public string Owner { get; private set; } = "";
    public ListenBrainzConnection(string token) { if (token.Length is < 1 or > 256 || token.Any(char.IsControl)) throw new ArgumentException("Enter a bounded ListenBrainz token."); _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", token); _client.DefaultRequestHeaders.UserAgent.ParseAdd("FluxPlayer/1.4.0 (+https://github.com/cbatista96/FluxPlayer)"); }
    private async Task<JsonDocument> Request(string path, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, "https://api.listenbrainz.org/1/" + path); if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(40)); using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); byte[] bytes = await RemoteMediaIO.Bytes(response, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false); return JsonDocument.Parse(bytes.Length == 0 ? "{}" : Encoding.UTF8.GetString(bytes));
    }
    public async Task Validate(CancellationToken token) { using var doc = await Request("validate-token", null, token).ConfigureAwait(false); if (!doc.RootElement.TryGetProperty("valid", out var valid) || valid.ValueKind != JsonValueKind.True) throw new IOException("ListenBrainz rejected the session token."); Owner = doc.RootElement.GetProperty("user_name").GetString() ?? ""; }
    public async Task Submit(ListenDraft row, bool now, CancellationToken token)
    {
        if (!now) ListenOutbox.Validate(row); if (Owner == "" || row.Owner != Owner) throw new ArgumentException("Validate the matching owner before submitting.");
        var listen = new Dictionary<string, object> { ["track_metadata"] = new { artist_name = row.Artist, track_name = row.Track, release_name = row.Album, additional_info = new { media_player = "FluxPlayer", submission_client = "FluxPlayer", duration_ms = row.DurationSeconds * 1000 } } }; if (!now) listen["listened_at"] = row.Started;
        using var response = await Request("submit-listens", new { listen_type = now ? "playing_now" : "single", payload = new[] { listen } }, token).ConfigureAwait(false);
    }
    public async Task<ListeningRow[]> Browse(string view, CancellationToken token)
    {
        if (Owner == "") throw new ArgumentException("Validate the session token first."); string user = Uri.EscapeDataString(Owner);
        string path = view switch { "Recent" => "user/" + user + "/listens?count=100", "Playing now" => "user/" + user + "/playing-now", "Top artists" => "stats/user/" + user + "/artists?count=100&range=all_time", "Top recordings" => "stats/user/" + user + "/recordings?count=100&range=all_time", _ => throw new ArgumentException("Choose a listening view.") };
        using var doc = await Request(path, null, token).ConfigureAwait(false); if (!doc.RootElement.TryGetProperty("payload", out var payload)) return [];
        string field = view == "Top artists" ? "artists" : view == "Top recordings" ? "recordings" : "listens"; if (!payload.TryGetProperty(field, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        string S(JsonElement item, string key) => item.TryGetProperty(key, out var value) ? value.ToString() : "";
        return array.EnumerateArray().Take(100).Select(item => { var metadata = item.TryGetProperty("track_metadata", out var m) ? m : item; return new ListeningRow(S(metadata, "artist_name"), S(metadata, "track_name"), S(metadata, "release_name"), view == "Recent" ? S(item, "listened_at") : S(item, "listen_count"), S(metadata, "recording_mbid")); }).ToArray();
    }
    public async Task Feedback(string mbid, int score, CancellationToken token) { if (!Guid.TryParse(mbid, out var id) || score is < -1 or > 1 || Owner == "") throw new ArgumentException("Validate the token and enter a recording MBID; score is −1/0/+1."); using var doc = await Request("feedback/recording-feedback", new { recording_mbid = id.ToString(), score }, token).ConfigureAwait(false); }
    public void Dispose() { _client.DefaultRequestHeaders.Clear(); _client.Dispose(); Owner = ""; }
}

internal sealed class LastFmConnection : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    private string _key, _secret, _session = "", _pending = "";
    public string Owner { get; private set; } = "";
    public LastFmConnection(string key, string secret) { if (key.Length is < 1 or > 512 || secret.Length is < 1 or > 512 || key.Any(char.IsControl) || secret.Any(char.IsControl)) throw new ArgumentException("Enter your bounded Last.fm application key and secret."); _key = key; _secret = secret; }
    private async Task<JsonDocument> Request(string method, IEnumerable<(string Key, string Value)> fields, bool signed, bool post, CancellationToken token)
    {
        var values = fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal); values["method"] = method; values["api_key"] = _key;
        if (signed) { string input = string.Concat(values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + p.Value)) + _secret; values["api_sig"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant(); }
        values["format"] = "json"; using var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, "https://ws.audioscrobbler.com/2.0/" + (post ? "" : "?" + string.Join('&', values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))))); if (post) request.Content = new FormUrlEncodedContent(values);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(40)); using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); var doc = JsonDocument.Parse(await RemoteMediaIO.Bytes(response, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false));
        if (doc.RootElement.TryGetProperty("error", out var error)) { string code = error.ToString(); doc.Dispose(); throw new IOException("Last.fm rejected the request (API code " + code + ")."); } return doc;
    }
    public async Task<string> Approval(CancellationToken token) { using var doc = await Request("auth.getToken", [], true, false, token).ConfigureAwait(false); _pending = doc.RootElement.GetProperty("token").GetString() ?? ""; return "https://www.last.fm/api/auth/?api_key=" + Uri.EscapeDataString(_key) + "&token=" + Uri.EscapeDataString(_pending); }
    public async Task Exchange(CancellationToken token) { if (_pending == "") throw new ArgumentException("Request and approve a token first."); using var doc = await Request("auth.getSession", [("token", _pending)], true, false, token).ConfigureAwait(false); var session = doc.RootElement.GetProperty("session"); _session = session.GetProperty("key").GetString() ?? ""; Owner = session.GetProperty("name").GetString() ?? ""; _pending = ""; }
    public async Task Submit(ListenDraft row, bool now, CancellationToken token)
    {
        if (_session == "" || row.Owner != Owner) throw new ArgumentException("Authorize the matching Last.fm account first."); if (!now) ListenOutbox.Validate(row);
        var fields = new List<(string, string)> { ("artist", row.Artist), ("track", row.Track), ("album", row.Album), ("duration", row.DurationSeconds.ToString(CultureInfo.InvariantCulture)), ("sk", _session) }; if (!now) fields.Add(("timestamp", row.Started.ToString(CultureInfo.InvariantCulture)));
        using var doc = await Request(now ? "track.updateNowPlaying" : "track.scrobble", fields, true, true, token).ConfigureAwait(false);
        if (!now && doc.RootElement.TryGetProperty("scrobbles", out var scrobbles) && scrobbles.TryGetProperty("@attr", out var attr) && attr.TryGetProperty("ignored", out var ignored) && ignored.ToString() != "0") throw new IOException("Last.fm ignored this submission; it was not marked submitted.");
    }
    public async Task<ListeningRow[]> Browse(string view, string artist, string track, CancellationToken token)
    {
        string method = view switch { "Recent" => "user.getRecentTracks", "Loved" => "user.getLovedTracks", "Similar artists" => "artist.getSimilar", "Similar tracks" => "track.getSimilar", _ => throw new ArgumentException("Choose a Last.fm view.") };
        var fields = new List<(string, string)> { ("limit", "100") }; if (view is "Recent" or "Loved") { if (Owner == "") throw new ArgumentException("Authorize an account first."); fields.Add(("user", Owner)); } else { fields.Add(("artist", artist)); if (view == "Similar tracks") fields.Add(("track", track)); }
        using var doc = await Request(method, fields, false, false, token).ConfigureAwait(false); string container = view switch { "Recent" => "recenttracks", "Loved" => "lovedtracks", "Similar artists" => "similarartists", _ => "similartracks" }; if (!doc.RootElement.TryGetProperty(container, out var body)) return [];
        string field = view == "Similar artists" ? "artist" : "track"; if (!body.TryGetProperty(field, out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        string S(JsonElement item, string key) => item.TryGetProperty(key, out var value) ? value.ValueKind == JsonValueKind.Object ? value.TryGetProperty("#text", out var text) ? text.ToString() : value.TryGetProperty("name", out var name) ? name.ToString() : "" : value.ToString() : "";
        return rows.EnumerateArray().Take(100).Select(r => new ListeningRow(view == "Similar artists" ? S(r, "name") : S(r, "artist"), view == "Similar artists" ? "" : S(r, "name"), S(r, "album"), S(r, "match"), S(r, "mbid"))).ToArray();
    }
    public async Task Love(string artist, string track, bool love, CancellationToken token) { if (_session == "" || artist == "" || track == "") throw new ArgumentException("Authorize and identify a track."); using var doc = await Request(love ? "track.love" : "track.unlove", [("artist", artist), ("track", track), ("sk", _session)], true, true, token).ConfigureAwait(false); }
    public void Dispose() { _client.Dispose(); _key = _secret = _session = _pending = Owner = ""; }
}
