using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace FluxPlayer.Services;

/// <summary>One explicitly selected local file, exposed only through an unguessable session URL.</summary>
public sealed class MediaFileShare : IAsyncDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _life;
    private Task? _loop;
    private readonly ConcurrentDictionary<int, Task> _jobs = new();
    private readonly SemaphoreSlim _slots = new(4, 4);
    private int _sequence;
    private string _path = "", _route = "", _authority = "";
    public string Address { get; private set; } = "";
    public string ContentType { get; private set; } = "application/octet-stream";
    public void Start(string file, string address, int port)
    {
        if (_listener != null) throw new InvalidOperationException("Stop the existing media share first.");
        if (!MobileRemote.Interfaces().Contains(address) || port is < 1024 or > 65535) throw new ArgumentException("Choose a listed private interface and port 1024–65535.");
        _path = FfmpegJob.Source(file); _authority = address + ":" + port;
        _route = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "/media";
        ContentType = Mime(file); var listener = new HttpListener(); listener.Prefixes.Add("http://" + _authority + "/");
        try { listener.Start(); } catch { listener.Close(); throw; }
        _listener = listener; _life = new(); Address = "http://" + _authority + _route; _loop = Loop(listener, _life.Token);
    }
    public static string Mime(string file) => Path.GetExtension(file).ToLowerInvariant() switch { ".mp4" or ".m4v" => "video/mp4", ".mkv" => "video/x-matroska", ".webm" => "video/webm", ".mp3" => "audio/mpeg", ".flac" => "audio/flac", ".wav" => "audio/wav", ".m4a" => "audio/mp4", ".ogg" or ".opus" => "audio/ogg", _ => "application/octet-stream" };
    public static (long Start, long End)? Range(string? header, long length)
    {
        if (header == null) return null;
        if (length <= 0) throw new ArgumentException("Empty media has no byte ranges.");
        if (!header.StartsWith("bytes=", StringComparison.Ordinal) || header.Contains(',')) throw new ArgumentException("Only one byte range is supported.");
        string[] parts = header[6..].Split('-');
        bool Number(string s, out long n) => long.TryParse(s, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out n);
        if (parts.Length != 2) throw new ArgumentException("Invalid byte range.");
        long start, end;
        if (parts[0] == "") { if (!Number(parts[1], out long suffix) || suffix <= 0) throw new ArgumentException("Invalid suffix range."); start = Math.Max(0, length - suffix); end = length - 1; }
        else { if (!Number(parts[0], out start) || start >= length || (parts[1] != "" && !Number(parts[1], out _))) throw new ArgumentException("Invalid byte range."); end = parts[1] == "" ? length - 1 : Math.Min(length - 1, long.Parse(parts[1], CultureInfo.InvariantCulture)); }
        if (end < start) throw new ArgumentException("Invalid byte range."); return (start, end);
    }
    private async Task Loop(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(token).ConfigureAwait(false); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or OperationCanceledException) { break; }
            if (!_slots.Wait(0)) { context.Response.StatusCode = 503; context.Response.Close(); continue; }
            int id = Interlocked.Increment(ref _sequence); Task task = Serve(context, token); _jobs[id] = task;
            _ = task.ContinueWith(done => { _ = done.Exception; _jobs.TryRemove(id, out _); _slots.Release(); }, TaskScheduler.Default);
        }
    }
    private async Task Serve(HttpListenerContext context, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45)); token = deadline.Token;
        using var cancel = token.Register(() => { try { context.Response.Abort(); } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or IOException) { } });
        try
        {
            var request = context.Request; var response = context.Response; response.KeepAlive = false;
            if (request.Url?.AbsolutePath != _route || !string.Equals(request.Headers["Host"], _authority, StringComparison.OrdinalIgnoreCase)) { response.StatusCode = 404; return; }
            if (request.HttpMethod is not ("GET" or "HEAD")) { response.StatusCode = 405; return; }
            await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            response.ContentType = ContentType; response.Headers["Accept-Ranges"] = "bytes"; response.Headers["Cache-Control"] = "no-store"; response.Headers["transferMode.dlna.org"] = "Streaming";
            (long Start, long End)? range;
            try { range = Range(request.Headers["Range"], file.Length); }
            catch (ArgumentException) { response.StatusCode = 416; response.Headers["Content-Range"] = "bytes */" + file.Length; return; }
            long start = range?.Start ?? 0, count = range == null ? file.Length : range.Value.End - start + 1;
            if (range != null) { response.StatusCode = 206; response.Headers["Content-Range"] = $"bytes {start}-{range.Value.End}/{file.Length}"; }
            response.ContentLength64 = count; if (request.HttpMethod == "HEAD") return; file.Position = start;
            byte[] buffer = new byte[65536];
            while (count > 0) { int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), token).ConfigureAwait(false); if (read == 0) throw new EndOfStreamException(); await response.OutputStream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); count -= read; deadline.CancelAfter(TimeSpan.FromSeconds(45)); }
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
        finally { try { context.Response.Close(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { } }
    }
    public async ValueTask DisposeAsync()
    {
        if (_listener == null) return; _life!.Cancel(); _listener.Close();
        if (_loop != null) await _loop.ConfigureAwait(false); await Task.WhenAll(_jobs.Values.ToArray()).ConfigureAwait(false);
        _listener = null; _life.Dispose(); _life = null; Address = "";
    }
}
