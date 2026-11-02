using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace FluxPlayer.Services;

internal static class RemoteMediaIO
{
    public static HttpClient Client(bool redirects = false) => new(new HttpClientHandler { AllowAutoRedirect = redirects, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static Uri Server(string address)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)) throw new ArgumentException("Use an HTTPS server root without embedded credentials or query parameters (HTTP only on loopback)."); return uri;
    }
    public static async Task<byte[]> Bytes(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode) throw new IOException($"Remote request failed (HTTP {(int)response.StatusCode}).");
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("The response exceeds the size limit.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false); using var output = new MemoryStream(); var buffer = new byte[32768]; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0) { if (output.Length + read > maximum) throw new IOException("The response exceeds the size limit."); await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false); }
        return output.ToArray();
    }
    public static async Task<JsonDocument> Json(HttpClient client, Uri uri, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        return JsonDocument.Parse(await Bytes(response, 8 * 1024 * 1024, timeout.Token).ConfigureAwait(false), new JsonDocumentOptions { MaxDepth = 48 });
    }
    public static async Task CopyResponse(HttpResponseMessage response, string destination, long maximum, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode) throw new IOException($"Remote download failed (HTTP {(int)response.StatusCode}).");
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("The media file exceeds the selected download limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        var buffer = new byte[65536]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { total += count; if (total > maximum) throw new IOException("The download exceeded its size limit."); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
        if (response.Content.Headers.ContentLength is long expected && total != expected) throw new IOException("The remote response ended before the declared file length.");
    }
    public static string SafeName(string name)
    { var result = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && !char.IsControl(c)).Take(100).ToArray()).Trim().TrimEnd('.'); return result == "" ? "Media" : result; }
    public static string Key(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Session-only, range-aware loopback relay. Upstream credentials never enter the queue URL.</summary>
internal sealed class RemoteMediaRelay : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _slots = new(4, 4);
    private readonly Dictionary<string, Func<string?, CancellationToken, Task<HttpResponseMessage>>> _sources = new();
    private readonly HashSet<Task> _clients = new();
    private Task? _loop;
    private Task? _stop;
    private string _prefix = "";
    public string Register(Func<string?, CancellationToken, Task<HttpResponseMessage>> source)
    {
        if (_stop != null) throw new ObjectDisposedException(nameof(RemoteMediaRelay));
        if (_loop == null)
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            _prefix = $"http://127.0.0.1:{port}/"; _listener.Prefixes.Add(_prefix); _listener.Start(); _loop = Loop();
        }
        string key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        lock (_sources) { if (_sources.Count >= 500) throw new IOException("This relay session reached its 500-source limit. Reopen the source panel."); _sources.Add(key, source); }
        return _prefix + key;
    }
    private async Task Loop()
    {
        while (!_life.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            if (!await _slots.WaitAsync(0).ConfigureAwait(false)) { context.Response.StatusCode = 503; context.Response.Close(); continue; }
            Task job = Serve(context); lock (_clients) _clients.Add(job);
            _ = job.ContinueWith(done => { lock (_clients) _clients.Remove(done); _slots.Release(); }, TaskScheduler.Default);
        }
    }
    private async Task Serve(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) || request.Headers["Origin"] != null || request.HttpMethod is not ("GET" or "HEAD")) { context.Response.StatusCode = 403; return; }
            Func<string?, CancellationToken, Task<HttpResponseMessage>>? source; lock (_sources) _sources.TryGetValue(request.Url!.AbsolutePath.Trim('/'), out source);
            if (source == null) { context.Response.StatusCode = 404; return; }
            using var response = await source(request.Headers["Range"], _life.Token).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) return;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            context.Response.Headers["Accept-Ranges"] = "bytes";
            if (response.Content.Headers.ContentRange is { } range) context.Response.Headers["Content-Range"] = range.ToString();
            if (response.Content.Headers.ContentLength is long size) context.Response.ContentLength64 = size; else context.Response.SendChunked = true;
            if (request.HttpMethod == "GET") { await using var stream = await response.Content.ReadAsStreamAsync(_life.Token).ConfigureAwait(false); await stream.CopyToAsync(context.Response.OutputStream, 65536, _life.Token).ConfigureAwait(false); }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            try { context.Response.Close(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { context.Response.Abort(); }
        }
    }
    public ValueTask DisposeAsync() => new(_stop ??= Stop());
    private async Task Stop()
    {
        _life.Cancel(); _listener.Close(); if (_loop != null) await _loop.ConfigureAwait(false);
        Task[] clients; lock (_clients) clients = _clients.ToArray();
        try { await Task.WhenAll(clients).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
        lock (_sources) _sources.Clear();
    }
}
