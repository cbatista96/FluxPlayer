using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record RemoteLibraryPreview(byte[] Bytes, string ETag, bool Missing, int Entries, string Hash);

/// <summary>Explicit two-way catalogue merge over an ETag-capable HTTP/WebDAV object; media never uploads.</summary>
public sealed class RemoteLibrarySync : IDisposable
{
    private readonly HttpClient _client;
    private readonly Uri _address;
    private const int MaximumBytes = 64 * 1024 * 1024;
    public RemoteLibrarySync(string address, string user, string password, HttpClient? transport = null)
    {
        _address = new Uri(address, UriKind.Absolute);
        if (!(_address.Scheme == "https" || _address.Scheme == "http" && _address.IsLoopback) || _address.UserInfo != "" || _address.Fragment != "" || address.Length > 4096 || user.Length > 256 || user.Contains(':') || password.Length > 2048) throw new ArgumentException("Choose an HTTPS/WebDAV JSON resource (HTTP only on loopback) and bounded session credentials.");
        _client = transport ?? RemoteMediaIO.Client();
        if (user != "") _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
    }
    public async Task<RemoteLibraryPreview> Preview(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await _client.GetAsync(_address, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return new([], "", true, 0, "");
        byte[] bytes = await RemoteMediaIO.Bytes(response, MaximumBytes, deadline.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (doc.RootElement.GetProperty("Version").GetInt32() != 1 || !doc.RootElement.TryGetProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 200000) throw new InvalidDataException("The remote object is not a supported FluxPlayer library backup.");
        string etag = response.Headers.ETag?.ToString() ?? "";
        if (etag == "" || response.Headers.ETag!.IsWeak) throw new IOException("Remote synchronization requires strong ETags and conditional PUT support to detect concurrent edits.");
        return new(bytes, etag, false, entries.GetArrayLength(), Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public async Task<LibraryService.ImportResult?> Synchronize(LibraryService library, RemoteLibraryPreview preview, string workingDirectory, CancellationToken token)
    {
        if (!preview.Missing && (preview.Bytes.Length > MaximumBytes || Convert.ToHexString(SHA256.HashData(preview.Bytes)) != preview.Hash || !EntityTagHeaderValue.TryParse(preview.ETag, out var tag) || tag.IsWeak)) throw new ArgumentException("The preview changed or has no strong ETag.");
        var fresh = await Preview(token).ConfigureAwait(false);
        if (fresh.Missing != preview.Missing || fresh.ETag != preview.ETag || fresh.Hash != preview.Hash) throw new IOException("The remote catalogue changed. Create a new preview.");
        Directory.CreateDirectory(workingDirectory); string temporary = Path.Combine(workingDirectory, "sync-" + Guid.NewGuid().ToString("N") + ".json");
        LibraryService.ImportResult? result = null;
        try
        {
            if (!preview.Missing) { await File.WriteAllBytesAsync(temporary, preview.Bytes, token).ConfigureAwait(false); result = await library.ImportBackupAsync(temporary, false, token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested(); await library.ExportBackupAsync(temporary).ConfigureAwait(false);
            if (new FileInfo(temporary).Length > MaximumBytes) throw new IOException("The merged catalogue exceeds 64 MiB.");
            using var request = new HttpRequestMessage(HttpMethod.Put, _address);
            if (preview.Missing) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any); else request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(preview.ETag));
            request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(temporary, token).ConfigureAwait(false)); request.Content.Headers.ContentType = new("application/json");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict) throw new IOException("Remote conflict: the local merge remains saved, but no remote overwrite was accepted. Preview again.");
            response.EnsureSuccessStatusCode(); return result;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => _client.Dispose();
}
