using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed class KodiConnection : IDisposable
{
    private readonly HttpClient _client = RemoteMediaIO.Client();
    private readonly Uri _endpoint;
    private long _sequence;
    public KodiConnection(string root, string username, string password)
    {
        var server = RemoteMediaIO.Server(root); _endpoint = new Uri(server, "jsonrpc");
        if (username.Length > 256 || username.Contains(':') || username.Any(char.IsControl) || password.Length > 2048) throw new ArgumentException("Use bounded session credentials; username cannot contain a colon.");
        if (username != "" || password != "") _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));
    }
    public async Task<JsonElement> Execute(string key, JsonElement input, CancellationToken token)
    {
        var action = InteropCatalog.Kodi.FirstOrDefault(a => a.Key == key) ?? throw new ArgumentException("Unknown Kodi operation."); InteropCatalog.ValidateKodi(action, input);
        long id = Interlocked.Increment(ref _sequence); byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, method = key, @params = input });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new ByteArrayContent(body) }; request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await RemoteMediaIO.Bytes(response, 8 * 1024 * 1024, timeout.Token).ConfigureAwait(false), new JsonDocumentOptions { MaxDepth = 64 }); var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var returned) || returned.ValueKind != JsonValueKind.Number || !returned.TryGetInt64(out long number) || number != id) throw new IOException("Kodi returned an unrelated RPC response.");
        if (root.TryGetProperty("error", out var error)) throw new IOException("Kodi rejected the operation: " + ServerActionCatalog.Bound(error.ToString(), 2000));
        return root.TryGetProperty("result", out var result) ? result.Clone() : throw new IOException("Kodi returned no RPC result.");
    }
    public void Dispose() { _client.DefaultRequestHeaders.Clear(); _client.Dispose(); }
}
