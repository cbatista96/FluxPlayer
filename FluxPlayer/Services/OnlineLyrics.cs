using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record OnlineLyric(long Id, string Title, string Artist, string Album, double Seconds, bool Instrumental, string Plain, string Synced)
{ public bool HasSynced => Synced.Length > 0; public bool HasPlain => Plain.Length > 0; }

/// <summary>Explicit LRCLIB reads. No publish endpoint, background lookup or automatic attachment.</summary>
public sealed class OnlineLyrics : IDisposable
{
    private readonly HttpClient _client;
    public OnlineLyrics() : this(RemoteMediaIO.Client()) { }
    /// <summary>The service owns and disposes the supplied transport.</summary>
    public OnlineLyrics(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("FluxPlayer/1.4.0 (+https://github.com/cbatista96/FluxPlayer)");
    }
    private static string E(string text) { if (text.Length is < 1 or > 500 || text.Any(char.IsControl)) throw new ArgumentException("Enter metadata of 1–500 characters."); return Uri.EscapeDataString(text); }
    public Task<OnlineLyric[]> Search(string query, CancellationToken token) => Read("search?q=" + E(query.Trim()), token);
    public Task<OnlineLyric[]> Match(string title, string artist, string album, double seconds, CancellationToken token)
    {
        if (!double.IsFinite(seconds) || seconds is < 1 or > 3600) throw new ArgumentException("Exact matching requires a duration of 1–3,600 seconds.");
        string route = "get?track_name=" + E(title.Trim()) + "&artist_name=" + E(artist.Trim()) + "&duration=" + seconds.ToString("0.###", CultureInfo.InvariantCulture); if (album.Trim() != "") route += "&album_name=" + E(album.Trim()); return Read(route, token);
    }
    private async Task<OnlineLyric[]> Read(string route, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _client.GetAsync(new Uri("https://lrclib.net/api/" + route), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return []; if ((int)response.StatusCode == 429) throw new IOException("LRCLIB rate limit reached. Wait before another explicit search; no automatic retry was made.");
        using var doc = JsonDocument.Parse(await RemoteMediaIO.Bytes(response, 8 * 1024 * 1024, timeout.Token).ConfigureAwait(false));
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Take(100).ToArray() : [doc.RootElement];
        return rows.Select(row =>
        {
            string S(string key, int max = 500) { string value = row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : ""; if (value.Contains('\0') || value.Length > max || Encoding.UTF8.GetByteCount(value) > LyricFile.MaximumBytes) throw new IOException("A lyrics record exceeds its bounds."); return value; }
            long id = row.GetProperty("id").GetInt64(); double seconds = row.GetProperty("duration").GetDouble();
            if (id < 0 || !double.IsFinite(seconds) || seconds < 0 || seconds > 86400) throw new IOException("A lyrics record has an invalid identifier or duration.");
            return new OnlineLyric(id, S("trackName"), S("artistName"), S("albumName"), seconds, row.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True, S("plainLyrics", LyricFile.MaximumBytes), S("syncedLyrics", LyricFile.MaximumBytes));
        }).ToArray();
    }
    public static Task Export(OnlineLyric row, bool synced, string destination, CancellationToken token)
    {
        if (synced) return LyricFile.WriteCopyAsync(destination, row.HasSynced ? row.Synced : throw new ArgumentException("This result has no synchronized lyrics."), token);
        if (!row.HasPlain) throw new ArgumentException("This result has no plain text lyrics."); return OfflineMediaJob.Publish(destination, ".txt", [], staging => File.WriteAllTextAsync(staging, row.Plain, new UTF8Encoding(false), token), token);
    }
    public void Dispose() => _client.Dispose();
}
