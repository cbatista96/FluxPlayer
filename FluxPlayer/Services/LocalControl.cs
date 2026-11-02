using FluxPlayer.Models;
using FluxPlayer.ViewModels;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace FluxPlayer.Services;

public sealed record ControlEvent(long Sequence, DateTimeOffset Time, string Name, object? Value);
public sealed record ControlListeners(bool Pipe, bool Tcp, bool Http, int TcpPort = 18764, int HttpPort = 18765);

/// <summary>Opt-in, authenticated loopback/current-user control. Never starts from construction.</summary>
public sealed class LocalControl : IAsyncDisposable
{
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentQueue<ControlEvent> _events = new();
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private readonly SemaphoreSlim _slots = new(4, 4);
    private CancellationTokenSource? _lifetime;
    private TcpListener? _tcp;
    private HttpListener? _http;
    private Task[] _loops = [];
    private Task? _stopTask;
    private long _sequence;
    private int _clientId;
    public string PipeName { get; }
    public string Token { get; private set; } = "";
    public bool Running => _lifetime != null;
    public LocalControl(MainViewModel vm, string directory, Dispatcher dispatcher)
    { _vm = vm; _dispatcher = dispatcher; PipeName = "FluxPlayer-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())))[..12]; }
    public ControlEvent[] Events(long after = 0) => _events.Where(e => e.Sequence > after).ToArray();
    private void Event(string name, object? value) { _events.Enqueue(new(Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, name, value)); while (_events.Count > 256) _events.TryDequeue(out _); }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName is "IsPlaying" or "CurrentIndex" or "Volume" or "IsMuted" or "PlaybackRate" or "WindowTitle") Event("player." + e.PropertyName, null); }
    public async Task Start(ControlListeners options)
    {
        if (Running) throw new InvalidOperationException("Stop the active listeners before changing endpoints.");
        if (!options.Pipe && !options.Tcp && !options.Http || options.TcpPort is < 1024 or > 65535 || options.HttpPort is < 1024 or > 65535 || options.Tcp && options.Http && options.TcpPort == options.HttpPort) throw new ArgumentException("Select a listener and distinct unprivileged ports.");
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var lifetime = new CancellationTokenSource(); _lifetime = lifetime;
        try
        {
            var loops = new List<Task>();
            if (options.Tcp) { _tcp = new TcpListener(IPAddress.Loopback, options.TcpPort); _tcp.Start(4); loops.Add(TcpLoop(lifetime.Token)); }
            if (options.Http) { _http = new HttpListener(); _http.Prefixes.Add($"http://127.0.0.1:{options.HttpPort}/"); _http.Start(); loops.Add(HttpLoop(lifetime.Token)); }
            if (options.Pipe) loops.Add(PipeLoop(lifetime.Token));
            _loops = loops.ToArray(); _vm.PropertyChanged += Changed; Event("control.started", options);
        }
        catch { await Stop(); throw; }
    }
    private void Track(Task task) { int id = Interlocked.Increment(ref _clientId); _clients[id] = task; _ = task.ContinueWith(completed => { _clients.TryRemove(id, out var removed); }, TaskScheduler.Default); }
    private async Task PipeLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null; bool acquired = false;
            try
            {
                await _slots.WaitAsync(token).ConfigureAwait(false); acquired = true;
                pipe = new(PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false); var client = pipe; pipe = null; acquired = false;
                Track(ServeStream(client, token));
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { if (!token.IsCancellationRequested) Event("control.pipe.error", ex.GetType().Name); }
            finally { pipe?.Dispose(); if (acquired) _slots.Release(); }
        }
    }
    private async Task TcpLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _tcp!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (!_slots.Wait(0)) { client.Dispose(); continue; }
                Track(ServeTcp(client, token));
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException) { if (!token.IsCancellationRequested) Event("control.tcp.error", ex.GetType().Name); }
        }
    }
    private async Task ServeTcp(TcpClient client, CancellationToken token) { using (client) await ServeStream(client.GetStream(), token).ConfigureAwait(false); }
    private static async Task<string?> Line(StreamReader reader, CancellationToken token)
    {
        var line = new StringBuilder(); var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) > 0)
        { if (buffer[0] == '\n') return line.ToString().TrimEnd('\r'); if (line.Length >= 65536) throw new InvalidDataException("Request limit exceeded."); line.Append(buffer[0]); }
        return line.Length == 0 ? null : line.ToString();
    }
    private async Task ServeStream(Stream stream, CancellationToken token)
    {
        using (stream)
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                string? line = await Line(reader, timeout.Token).ConfigureAwait(false); if (line == null) break;
                await writer.WriteLineAsync((await Request(line, null, timeout.Token).ConfigureAwait(false)).AsMemory(), timeout.Token).ConfigureAwait(false);
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (!token.IsCancellationRequested) Event("control.client.closed", ex.GetType().Name); }
        finally { _slots.Release(); }
    }
    private bool Authorized(string? value) => Token.Length == 64 && value is { Length: 64 } && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Token), Encoding.UTF8.GetBytes(value));
    private async Task HttpLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await _http!.GetContextAsync().WaitAsync(token).ConfigureAwait(false);
                if (!_slots.Wait(0)) { context.Response.StatusCode = 429; context.Response.Close(); continue; }
                Track(ServeHttp(context, token));
            }
            catch (Exception ex) when (ex is HttpListenerException or OperationCanceledException or ObjectDisposedException) { if (!token.IsCancellationRequested) Event("control.http.error", ex.GetType().Name); }
        }
    }
    private async Task ServeHttp(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            var request = context.Request;
            string? auth = request.Headers["Authorization"];
            if (request.RemoteEndPoint?.Address is not { } address || !IPAddress.IsLoopback(address) || request.Headers["Origin"] != null || auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal) || !Authorized(auth[7..])) { context.Response.StatusCode = 403; return; }
            if (request.Url?.AbsolutePath == "/ws" && request.IsWebSocketRequest)
            { var accepted = await context.AcceptWebSocketAsync(null).ConfigureAwait(false); using var socket = accepted.WebSocket; await ServeSocket(socket, token).ConfigureAwait(false); return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            string text;
            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/state") text = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"state.get\"}";
            else if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/rpc")
            {
                using var memory = new MemoryStream(); var bytes = new byte[4096]; int read;
                while ((read = await request.InputStream.ReadAsync(bytes, timeout.Token).ConfigureAwait(false)) > 0) { if (memory.Length + read > 65536) throw new InvalidDataException("Request limit exceeded."); memory.Write(bytes, 0, read); }
                text = new UTF8Encoding(false, true).GetString(memory.ToArray());
            }
            else { context.Response.StatusCode = 404; return; }
            byte[] output = Encoding.UTF8.GetBytes(await Request(text, Token, timeout.Token).ConfigureAwait(false)); context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength64 = output.Length;
            await context.Response.OutputStream.WriteAsync(output, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Event("control.http.closed", ex.GetType().Name); }
        finally { try { context.Response.Close(); } catch (ObjectDisposedException) { } _slots.Release(); }
    }
    private async Task ServeSocket(WebSocket socket, CancellationToken token)
    {
        var bytes = new byte[4096];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var memory = new MemoryStream(); WebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), timeout.Token).ConfigureAwait(false);
                if (part.MessageType == WebSocketMessageType.Close) return;
                if (part.MessageType != WebSocketMessageType.Text || memory.Length + part.Count > 65536) throw new InvalidDataException("Use bounded JSON text frames.");
                memory.Write(bytes, 0, part.Count);
            } while (!part.EndOfMessage);
            string text = new UTF8Encoding(false, true).GetString(memory.ToArray());
            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(await Request(text, Token, timeout.Token).ConfigureAwait(false))), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("method", out var method) && method.GetString() == "events.subscribe")
            {
                using var subscription = CancellationTokenSource.CreateLinkedTokenSource(token);
                var closing = socket.ReceiveAsync(new ArraySegment<byte>(new byte[1]), subscription.Token);
                try
                {
                    long after = 0;
                    while (!token.IsCancellationRequested)
                    {
                        if (closing.IsCompleted)
                        {
                            var received = await closing.ConfigureAwait(false);
                            await socket.CloseOutputAsync(received.MessageType == WebSocketMessageType.Close ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.PolicyViolation,
                                received.MessageType == WebSocketMessageType.Close ? "Closed" : "Subscription is a server event stream", token).ConfigureAwait(false); return;
                        }
                        foreach (var item in Events(after))
                        {
                            if (closing.IsCompleted) break;
                            await socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", method = "player.event", @params = item })), WebSocketMessageType.Text, true, token).ConfigureAwait(false); after = item.Sequence;
                        }
                        await Task.WhenAny(closing, Task.Delay(400, token)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    subscription.Cancel();
                    try { await closing.ConfigureAwait(false); } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
                }
                return;
            }
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }
    public async Task<string> Request(string text, string? transportToken, CancellationToken token)
    {
        JsonElement? id = null;
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > 65536) throw new InvalidDataException("Request exceeds 64 KiB.");
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 }); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0") throw new ArgumentException("Use a JSON-RPC 2.0 object.");
            if (root.TryGetProperty("id", out var identity) && identity.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null) id = identity.Clone();
            string? supplied = transportToken ?? (root.TryGetProperty("token", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null);
            if (!Authorized(supplied)) throw new UnauthorizedAccessException("Unauthorized.");
            string method = root.GetProperty("method").GetString() ?? ""; var args = root.TryGetProperty("params", out var parameters) ? parameters.Clone() : default;
            object? result;
            if (method == "events.get" || method == "events.subscribe") result = Events();
            else
            {
                string[] paths = method is "open" or "queue.add" ? Sources(args) : [];
                result = await _dispatcher.InvokeAsync(() => Execute(method, args, paths), DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
            }
            Event("command." + method, null); return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = ex is UnauthorizedAccessException ? -32001 : -32602, message = ex.Message } }); }
    }
    private static string[] Sources(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() is < 1 or > 100) throw new ArgumentException("Supply 1–100 explicit media sources.");
        return sources.EnumerateArray().Select(value =>
        {
            string path = value.GetString() ?? "";
            if (MediaFormats.IsNetwork(path)) { var uri = new Uri(path); if (uri.UserInfo != "" || uri.Scheme is not ("http" or "https" or "rtsp" or "rtmp" or "udp" or "rtp")) throw new ArgumentException("Unsupported network source."); }
            else if (!Path.IsPathFullyQualified(path) || !MediaFormats.IsMedia(path) || !File.Exists(path)) throw new ArgumentException("Choose available absolute media paths.");
            return path;
        }).ToArray();
    }
    private object State() => new { title = _vm.CurrentItem?.Title, source = _vm.CurrentItem?.FilePath, playing = _vm.IsPlaying, positionMs = _vm.Engine.GetTime(), durationMs = _vm.Engine.GetDuration(), volume = _vm.Volume, muted = _vm.IsMuted, rate = _vm.PlaybackRate, queueCount = _vm.Playlist.Count };
    private object? Execute(string method, JsonElement args, string[] paths)
    {
        double Number(string property, double min, double max) { double value = args.GetProperty(property).GetDouble(); if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException("Numeric argument outside its bounds."); return value; }
        switch (method)
        {
            case "ping": return "FluxPlayer";
            case "state.get": return State();
            case "play": if (!_vm.HasMedia && _vm.Playlist.Count == 0) throw new InvalidOperationException("Queue media first."); if (!_vm.IsPlaying) _vm.PlayPauseCommand.Execute(null); break;
            case "pause": if (_vm.IsPlaying) _vm.PlayPauseCommand.Execute(null); break;
            case "stop": _vm.StopCommand.Execute(null); break;
            case "next": _vm.NextCommand.Execute(null); break;
            case "previous": _vm.PreviousCommand.Execute(null); break;
            case "seek": _vm.Engine.SeekTo((long)Number("positionMs", 0, 315360000000)); break;
            case "volume": _vm.Volume = Number("value", 0, Math.Min(100, _vm.MaxVolume)); break;
            case "rate": _vm.PlaybackRate = (float)Number("value", .5, 2); break;
            case "mute": if (args.GetProperty("value").GetBoolean() != _vm.IsMuted) _vm.ToggleMuteCommand.Execute(null); break;
            case "open": case "queue.add": _vm.AddCatalogTracks(paths.Select(path => new SavedMediaTrack(path, MediaFormats.IsNetwork(path) ? path : Path.GetFileNameWithoutExtension(path))), method == "open"); break;
            case "queue.clear": _vm.ClearPlaylistCommand.Execute(null); break;
            default: throw new ArgumentException("Unknown control method.");
        }
        return State();
    }
    public async Task ExportJournal(string path, CancellationToken token)
    {
        var rows = Events(); await OfflineMediaJob.Publish(path, ".jsonl", [], staging => File.WriteAllLinesAsync(staging, rows.Select(row => JsonSerializer.Serialize(row)), token), token).ConfigureAwait(false);
    }
    public Task Stop() => _stopTask is { IsCompleted: false } ? _stopTask : _stopTask = StopCore();
    private async Task StopCore()
    {
        var lifetime = _lifetime; if (lifetime == null) return;
        _vm.PropertyChanged -= Changed; lifetime.Cancel(); _tcp?.Stop(); _http?.Close();
        try { await Task.WhenAll(_loops.Concat(_clients.Values)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
        _tcp = null; _http = null; _loops = []; _lifetime = null; Token = ""; Event("control.stopped", null); lifetime.Dispose();
    }
    public ValueTask DisposeAsync() => new(Stop());
}
