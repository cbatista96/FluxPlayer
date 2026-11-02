using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record DownloadableSubtitle(int FileId, string Release, string Language, int Downloads, bool HashMatched);
public sealed class SubtitleDownloads : IDisposable
{
    private readonly HttpClient _api, _files;
    private string _host = "api.opensubtitles.com";
    public SubtitleDownloads(string apiKey, HttpClient? api = null, HttpClient? files = null)
    {
        if (apiKey.Length is < 1 or > 1024 || apiKey.Any(char.IsControl)) throw new ArgumentException("Enter your OpenSubtitles application API key.");
        _api = api ?? RemoteMediaIO.Client(); _files = files ?? RemoteMediaIO.Client();
        _api.DefaultRequestHeaders.Add("Api-Key", apiKey); _api.DefaultRequestHeaders.UserAgent.ParseAdd("FluxPlayer/1.4.0");
    }
    private async Task<JsonDocument> Request(string route, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, "https://" + _host + "/api/v1/" + route);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(40));
        using var response = await _api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if ((int)response.StatusCode is 406 or 429) throw new IOException("The subtitle provider rejected the quota/rate limit. No automatic retry was made.");
        return JsonDocument.Parse(await RemoteMediaIO.Bytes(response, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false));
    }
    public async Task Login(string user, string password, CancellationToken token)
    {
        if (user.Length is < 1 or > 256 || password.Length is < 1 or > 1024) throw new ArgumentException("Enter bounded session credentials.");
        using var doc = await Request("login", new { username = user, password }, token).ConfigureAwait(false);
        string bearer = doc.RootElement.GetProperty("token").GetString() ?? "", host = doc.RootElement.GetProperty("base_url").GetString() ?? "";
        if (bearer.Length is < 1 or > 8192 || bearer.Any(char.IsControl) || host is not ("api.opensubtitles.com" or "vip-api.opensubtitles.com")) throw new IOException("Unsupported authentication response.");
        _host = host; _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
    }
    public static async Task<string> MovieHash(string source, CancellationToken token)
    {
        await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (file.Length < 131072) throw new ArgumentException("File-hash matching requires at least 128 KiB.");
        ulong hash = (ulong)file.Length; byte[] block = new byte[65536];
        foreach (long position in new[] { 0L, file.Length - block.Length })
        { file.Position = position; await file.ReadExactlyAsync(block, token).ConfigureAwait(false); for (int i = 0; i < block.Length; i += 8) hash = unchecked(hash + BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(i, 8))); }
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
    public async Task<DownloadableSubtitle[]> Search(string query, string language, string? source, CancellationToken token)
    {
        if (query.Length is < 1 or > 500 || !Regex.IsMatch(language, "^[a-z]{2,3}(-[A-Za-z]{2,8})?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new ArgumentException("Use a query of 1–500 characters and one language code, such as en or es.");
        string route = "subtitles?query=" + Uri.EscapeDataString(query) + "&languages=" + Uri.EscapeDataString(language) + "&order_by=download_count&order_direction=desc";
        if (source != null) route += "&moviehash=" + await MovieHash(source, token).ConfigureAwait(false);
        using var doc = await Request(route, null, token).ConfigureAwait(false);
        var rows = new List<DownloadableSubtitle>();
        foreach (var result in doc.RootElement.GetProperty("data").EnumerateArray().Take(100))
        {
            var a = result.GetProperty("attributes"); string lang = a.GetProperty("language").GetString() ?? "";
            string title = a.TryGetProperty("release", out var r) ? r.GetString() ?? "" : ""; if (title.Length > 500) title = title[..500];
            int downloads = a.TryGetProperty("download_count", out var n) && n.TryGetInt32(out int count) ? Math.Max(0, count) : 0;
            bool matched = source != null && a.TryGetProperty("moviehash_match", out var m) && m.ValueKind == JsonValueKind.True;
            foreach (var f in a.GetProperty("files").EnumerateArray().Take(10))
                if (f.GetProperty("file_id").TryGetInt32(out int id) && id > 0 && lang == language && rows.Count < 100) rows.Add(new(id, title, lang, downloads, matched));
        }
        return rows.OrderByDescending(r => r.HashMatched).ThenByDescending(r => r.Downloads).ToArray();
    }
    public async Task Download(DownloadableSubtitle row, string destination, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60)); token = deadline.Token;
        if (row.FileId <= 0 || Path.GetExtension(destination).ToLowerInvariant() != ".srt" || File.Exists(destination)) throw new ArgumentException("Select a result and a new SRT destination.");
        using var doc = await Request("download", new { file_id = row.FileId, sub_format = "srt" }, token).ConfigureAwait(false);
        var address = new Uri(doc.RootElement.GetProperty("link").GetString() ?? "", UriKind.Absolute); byte[]? bytes = null;
        for (int i = 0; i < 4; i++)
        {
            if (address.Scheme != "https" || address.UserInfo != "" || !(address.Host == "opensubtitles.com" || address.Host.EndsWith(".opensubtitles.com", StringComparison.OrdinalIgnoreCase) || address.Host == "opensubtitles.org" || address.Host.EndsWith(".opensubtitles.org", StringComparison.OrdinalIgnoreCase))) throw new IOException("The provider returned an unsupported download origin.");
            using var response = await _files.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location) { address = new Uri(address, location); continue; }
            bytes = await RemoteMediaIO.Bytes(response, 4 * 1024 * 1024, token).ConfigureAwait(false); break;
        }
        if (bytes == null) throw new IOException("Too many subtitle download redirects.");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        await OfflineMediaJob.Publish(destination, ".srt", [], async staging =>
        {
            await File.WriteAllTextAsync(staging, text, new UTF8Encoding(false), token).ConfigureAwait(false);
            if ((await SubtitleWorkshop.Read(staging, token).ConfigureAwait(false)).IsEmpty) throw new IOException("The downloaded subtitle document is empty.");
        }, token).ConfigureAwait(false);
    }
    public void Dispose() { _api.Dispose(); _files.Dispose(); }
}
