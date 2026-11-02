using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FluxPlayer.Services;

public sealed record DavRow(string Name, string Address, bool Folder, long Bytes, string ETag);
internal sealed class WebDavSource : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    private readonly Uri _root;
    private readonly string _cache;
    private sealed record CacheEntry(string Path, string ETag, long Bytes, long Ticks);
    public string Root => _root.AbsoluteUri;
    public WebDavSource(string root, string user, string password, string profile)
    {
        _root = RemoteMediaIO.Server(root); _cache = Path.Combine(profile, "webdav-cache", RemoteMediaIO.Key(_root.AbsoluteUri + "\n" + user));
        if (user.Contains(':') || user.Length > 256 || password.Length > 1024) throw new ArgumentException("Use a bounded WebDAV username without a colon.");
        if (user != "") _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
    }
    private Uri Address(string value)
    {
        var uri = new Uri(_root, value);
        if (!_root.IsBaseOf(uri) || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.Scheme != _root.Scheme || uri.Authority != _root.Authority) throw new ArgumentException("Choose a resource inside this WebDAV root."); return uri;
    }
    public async Task<DavRow[]> List(string folder, CancellationToken token)
    {
        Uri uri = Address(folder); using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), uri);
        request.Headers.Add("Depth", "1"); request.Content = new StringContent("<d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/><d:getcontentlength/><d:getetag/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        byte[] bytes = await RemoteMediaIO.Bytes(response, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
        using var input = new MemoryStream(bytes); using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        var doc = XDocument.Load(reader); XNamespace dav = "DAV:"; var rows = new List<DavRow>();
        foreach (var item in doc.Descendants(dav + "response").Take(1002))
        {
            string href = item.Element(dav + "href")?.Value ?? ""; if (href == "") continue;
            Uri target; try { target = Address(new Uri(uri, href).AbsoluteUri); } catch (ArgumentException) { continue; }
            if (target.AbsoluteUri.TrimEnd('/') == uri.AbsoluteUri.TrimEnd('/')) continue;
            var prop = item.Elements(dav + "propstat").FirstOrDefault(p => (p.Element(dav + "status")?.Value ?? "").Contains(" 200 ", StringComparison.Ordinal))?.Element(dav + "prop"); if (prop == null) continue;
            string name = prop.Element(dav + "displayname")?.Value ?? Uri.UnescapeDataString(target.Segments.Last().TrimEnd('/'));
            rows.Add(new(name, target.AbsoluteUri, prop.Element(dav + "resourcetype")?.Element(dav + "collection") != null, long.TryParse(prop.Element(dav + "getcontentlength")?.Value, out long size) ? size : 0, prop.Element(dav + "getetag")?.Value ?? ""));
        }
        if (rows.Count > 1000) throw new IOException("This folder exceeds the 1,000-entry limit. Choose a narrower folder.");
        return rows.OrderByDescending(r => r.Folder).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<DavRow[]> Scan(string folder, CancellationToken token)
    {
        var pending = new Queue<(string Address, int Depth)>(); pending.Enqueue((Address(folder).AbsoluteUri, 0)); var visited = new HashSet<string>(StringComparer.Ordinal); var result = new List<DavRow>();
        while (pending.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested(); if (!visited.Add(current.Address)) continue;
            foreach (var row in await List(current.Address, token).ConfigureAwait(false))
            {
                if (row.Folder && current.Depth < 3) pending.Enqueue((row.Address, current.Depth + 1));
                else if (!row.Folder) { result.Add(row); if (result.Count > 500) throw new IOException("The recursive view exceeds 500 files. Choose a smaller collection."); }
            }
            if (visited.Count + pending.Count > 100) throw new IOException("The recursive view exceeds 100 folders.");
        }
        return result.ToArray();
    }
    public async Task<HttpResponseMessage> Stream(string address, string? range, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Address(address));
        if (range != null && RangeHeaderValue.TryParse(range, out var parsed)) request.Headers.Range = parsed;
        using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); headersDeadline.CancelAfter(TimeSpan.FromSeconds(45));
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false);
    }
    public async Task Download(DavRow row, string destination, CancellationToken token)
    {
        if (row.Folder) throw new ArgumentException("Select a file."); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(20));
        await OfflineMediaJob.Publish(destination, Path.GetExtension(destination), [], async staging => { using var response = await Stream(row.Address, null, deadline.Token).ConfigureAwait(false); await RemoteMediaIO.CopyResponse(response, staging, 1024L * 1024 * 1024, deadline.Token).ConfigureAwait(false); }, deadline.Token).ConfigureAwait(false);
    }
    public async Task Upload(string local, string parent, string name, CancellationToken token)
    {
        var version = SourceVersion.Capture(local); if (version.Bytes > 1024L * 1024 * 1024 || name != RemoteMediaIO.SafeName(name)) throw new ArgumentException("Use a safe new filename and a source ≤1 GiB.");
        Uri folder = Address(parent.TrimEnd('/') + "/"); Uri destination = Address(new Uri(folder, Uri.EscapeDataString(name)).AbsoluteUri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(20));
        await using var input = File.OpenRead(version.Path); using var request = new HttpRequestMessage(HttpMethod.Put, destination) { Content = new StreamContent(input) }; request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException($"Upload refused (HTTP {(int)response.StatusCode}); existing remote files are not replaced."); version.Verify();
    }
    public async Task CreateFolder(string parent, string name, CancellationToken token)
    {
        if (name is "" or "." or ".." || name != RemoteMediaIO.SafeName(name)) throw new ArgumentException("Choose a safe new collection name.");
        Uri folder = Address(parent.TrimEnd('/') + "/"); using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), Address(new Uri(folder, Uri.EscapeDataString(name) + "/").AbsoluteUri)); request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await _client.SendAsync(request, deadline.Token).ConfigureAwait(false); if (response.StatusCode != HttpStatusCode.Created) throw new IOException($"Collection creation refused (HTTP {(int)response.StatusCode}).");
    }
    public async Task<DavRow[]> ReadPlaylist(DavRow row, CancellationToken token)
    {
        if (Path.GetExtension(new Uri(row.Address).AbsolutePath).ToLowerInvariant() is not (".m3u" or ".m3u8")) throw new ArgumentException("Select an M3U/M3U8 file inside the connected root.");
        using var response = await Stream(row.Address, null, token).ConfigureAwait(false); var bytes = await RemoteMediaIO.Bytes(response, 1024 * 1024, token).ConfigureAwait(false); var list = new List<DavRow>();
        foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith('#')).Take(101))
        {
            Uri uri = Address(new Uri(new Uri(row.Address), line).AbsoluteUri); list.Add(new(Uri.UnescapeDataString(uri.Segments.Last()), uri.AbsoluteUri, false, 0, ""));
        }
        if (list.Count > 100) throw new IOException("Use playlists with at most 100 items."); return list.ToArray();
    }
    public async Task<string> CachedCopy(DavRow row, CancellationToken token)
    {
        Directory.CreateDirectory(_cache); string key = RemoteMediaIO.Key(Address(row.Address).AbsoluteUri); string metadata = Path.Combine(_cache, key + ".json"); CacheEntry? saved = null;
        if (File.Exists(metadata) && new FileInfo(metadata).Length <= 8192) saved = JsonSerializer.Deserialize<CacheEntry>(await File.ReadAllTextAsync(metadata, token).ConfigureAwait(false));
        bool valid = saved != null && Path.GetDirectoryName(Path.GetFullPath(saved.Path)) == Path.GetFullPath(_cache) && File.Exists(saved.Path) && new FileInfo(saved.Path).Length == saved.Bytes && new FileInfo(saved.Path).LastWriteTimeUtc.Ticks == saved.Ticks;
        using var request = new HttpRequestMessage(HttpMethod.Get, Address(row.Address)); if (valid && EntityTagHeaderValue.TryParse(saved!.ETag, out var cachedTag)) request.Headers.IfNoneMatch.Add(cachedTag);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(20)); using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && valid) return saved!.Path;
        string extension = Path.GetExtension(new Uri(row.Address).AbsolutePath); if (extension.Length > 12 || extension.Any(c => c != '.' && !char.IsAsciiLetterOrDigit(c))) extension = ".media";
        string output = Path.Combine(_cache, key + "-" + Guid.NewGuid().ToString("N") + extension);
        await OfflineMediaJob.Publish(output, extension, [], staging => RemoteMediaIO.CopyResponse(response, staging, 1024L * 1024 * 1024, deadline.Token), deadline.Token).ConfigureAwait(false);
        var info = new FileInfo(output); var next = new CacheEntry(output, response.Headers.ETag?.ToString() ?? "", info.Length, info.LastWriteTimeUtc.Ticks);
        string temp = metadata + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next), token).ConfigureAwait(false); File.Move(temp, metadata, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
        return output;
    }
    public async Task ResumeDownload(DavRow row, string destination, CancellationToken token)
    {
        // Partial state is always app-owned; user output is published only after completion.
        destination = Path.GetFullPath(destination); if (File.Exists(destination) || Directory.Exists(destination) || !Directory.Exists(Path.GetDirectoryName(destination))) throw new IOException("Choose a new file in an existing folder.");
        Directory.CreateDirectory(_cache); string key = RemoteMediaIO.Key(row.Address + "\n" + destination); string part = Path.Combine(_cache, key + ".partial"); string tag = part + ".etag";
        // One owner must control the data and its validator together, including
        // separate source panels/processes using the same profile and target.
        using var stateLock = new FileStream(part + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(20));
        if (File.Exists(tag) && new FileInfo(tag).Length > 2048) throw new IOException("The owned partial ETag metadata exceeds its limit.");
        long offset = File.Exists(part) ? new FileInfo(part).Length : 0; string etag = File.Exists(tag) ? await File.ReadAllTextAsync(tag, token).ConfigureAwait(false) : "";
        if (!EntityTagHeaderValue.TryParse(etag, out var resumeTag) || resumeTag.IsWeak) etag = "";
        if (offset > 1024L * 1024 * 1024 || etag.Length > 512) throw new IOException("The owned partial download metadata is invalid.");
        using var request = new HttpRequestMessage(HttpMethod.Get, Address(row.Address));
        if (offset > 0 && etag != "") { request.Headers.Range = new RangeHeaderValue(offset, null); request.Headers.TryAddWithoutValidation("If-Range", etag); }
        using var initialResponse = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        // A canceled publication can leave an already complete owned partial.
        // A fresh GET also safely recovers a partial longer than a changed file.
        using var restart = initialResponse.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0 && etag != "" ? new HttpRequestMessage(HttpMethod.Get, Address(row.Address)) : null;
        using var restartedResponse = restart != null ? await _client.SendAsync(restart, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false) : null;
        var response = restartedResponse ?? initialResponse;
        bool append = response.StatusCode == HttpStatusCode.PartialContent && offset > 0 && etag != "";
        if (response.StatusCode == HttpStatusCode.PartialContent && !append) throw new IOException("The server returned an unsolicited partial response.");
        if (append && response.Content.Headers.ContentRange?.From != offset) throw new IOException("The server returned an unexpected resume range.");
        if (append && response.Headers.ETag != null && response.Headers.ETag.ToString() != etag) throw new IOException("The remote ETag changed during range resumption.");
        if (!response.IsSuccessStatusCode) throw new IOException($"Download refused (HTTP {(int)response.StatusCode}).");
        if (!append) offset = 0; string nextTag = response.Headers.ETag?.ToString() ?? ""; await File.WriteAllTextAsync(tag, nextTag, token).ConfigureAwait(false);
        long received = 0;
        await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
        await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
        {
            var buffer = new byte[65536]; int read;
            while ((read = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0) { received += read; if (offset + received > 1024L * 1024 * 1024) throw new IOException("The download exceeds 1 GiB."); await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false); }
        }
        if (response.Content.Headers.ContentLength is long expected && received != expected || response.Content.Headers.ContentRange?.Length is long total && total != offset + received) throw new IOException("The download is incomplete; the owned partial file was retained.");
        await OfflineMediaJob.Publish(destination, Path.GetExtension(destination), [], staging => Task.Run(() => File.Copy(part, staging, false), deadline.Token), deadline.Token).ConfigureAwait(false);
        File.Delete(part); File.Delete(tag);
    }
    public void Dispose() { _client.DefaultRequestHeaders.Authorization = null; _client.Dispose(); }
}
