using FluxPlayer.ViewModels;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace FluxPlayer.Services;

/// <summary>Opt-in, IPv4 interface-scoped browser remote. Only queued playback can be controlled.</summary>
public sealed class MobileRemote(MainViewModel vm, Dispatcher dispatcher) : IAsyncDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _life;
    private Task? _loop;
    private readonly SemaphoreSlim _clients = new(4, 4);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<int, Task> _jobs = new();
    private int _sequence;
    private string _authority = "", _secret = "";
    public string Address { get; private set; } = "";
    public string PairingCode => _secret;
    private static bool Private(IPAddress address)
    { var b = address.GetAddressBytes(); return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (IPAddress.IsLoopback(address) || b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168); }
    public static string[] Interfaces() => NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(Private).Select(a => a.ToString()).Append("127.0.0.1").Distinct().Order().ToArray();
    public void Start(string address, int port)
    {
        if (!_lifecycle.Wait(0)) throw new ArgumentException("Wait for the previous remote session to stop.");
        try
        {
        if (_listener != null) throw new ArgumentException("Stop the current remote before starting another.");
        if (!IPAddress.TryParse(address, out var ip) || !Private(ip) || !Interfaces().Contains(ip.ToString()) || port is < 1024 or > 65535) throw new ArgumentException("Choose a listed private/loopback IPv4 address and port 1024–65535.");
        _authority = ip + ":" + port; var listener = new HttpListener(); listener.Prefixes.Add("http://" + _authority + "/"); listener.TimeoutManager.EntityBody = TimeSpan.FromSeconds(10); listener.TimeoutManager.IdleConnection = TimeSpan.FromSeconds(20);
        try { listener.Start(); } catch { listener.Close(); throw; }
        _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); Address = "http://" + _authority + "/"; _listener = listener; _life = new CancellationTokenSource(); _loop = Loop(listener, _life.Token);
        }
        finally { _lifecycle.Release(); }
    }
    private async Task Loop(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException) { break; }
            if (!_clients.Wait(0)) { context.Response.StatusCode = 503; context.Response.Close(); continue; }
            int id = Interlocked.Increment(ref _sequence); var job = Serve(context, token); _jobs[id] = job;
            _ = job.ContinueWith(done => { _ = done.Exception; _jobs.TryRemove(id, out _); _clients.Release(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private bool Authorized(string? value)
    { string prefix = "Bearer "; if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 64 || _secret.Length != 64) return false; return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value[prefix.Length..]), Encoding.ASCII.GetBytes(_secret)); }
    private async Task Serve(HttpListenerContext context, CancellationToken lifetime)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime); deadline.CancelAfter(TimeSpan.FromSeconds(12)); var token = deadline.Token;
        using var cancel = token.Register(() => { try { context.Request.InputStream.Close(); context.Response.Abort(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException) { } });
        try
        {
            var request = context.Request; var response = context.Response; response.KeepAlive = false; response.Headers["Cache-Control"] = "no-store"; response.Headers["X-Content-Type-Options"] = "nosniff"; response.Headers["Referrer-Policy"] = "no-referrer";
            if (!string.Equals(request.Headers["Host"], _authority, StringComparison.OrdinalIgnoreCase)) { response.StatusCode = 403; return; }
            string? origin = request.Headers["Origin"]; if (origin != null && !string.Equals(origin, "http://" + _authority, StringComparison.OrdinalIgnoreCase)) { response.StatusCode = 403; return; }
            string path = request.Url?.AbsolutePath ?? "";
            if (request.HttpMethod == "GET" && path == "/")
            {
                string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)); response.Headers["Content-Security-Policy"] = "default-src 'none'; connect-src 'self'; style-src 'unsafe-inline'; script-src 'nonce-" + nonce + "'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
                using var stream = typeof(MobileRemote).Assembly.GetManifestResourceStream("FluxPlayer.Assets.mobile-remote.html") ?? throw new IOException("Remote page is missing."); using var reader = new StreamReader(stream); string html = (await reader.ReadToEndAsync(token).ConfigureAwait(false)).Replace("{{nonce}}", nonce, StringComparison.Ordinal); await Write(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), token).ConfigureAwait(false); return;
            }
            if (!Authorized(request.Headers["Authorization"])) { response.StatusCode = 401; return; }
            if (request.HttpMethod == "GET" && path == "/state") { var snapshot = await dispatcher.InvokeAsync(State, DispatcherPriority.Background, token).Task.ConfigureAwait(false); await Write(response, "application/json", JsonSerializer.SerializeToUtf8Bytes(snapshot), token).ConfigureAwait(false); return; }
            if (request.HttpMethod != "POST" || path != "/control") { response.StatusCode = 404; return; }
            if (request.ContentLength64 is < 1 or > 1024 || request.ContentType?.Split(';')[0].Trim() != "application/json") { response.StatusCode = 400; return; }
            byte[] bytes = new byte[(int)request.ContentLength64]; await request.InputStream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("command", out var commandValue) || commandValue.ValueKind != JsonValueKind.String)
                throw new ArgumentException("A command string is required.");
            string command = commandValue.GetString() ?? "";
            double number = 0;
            if (root.TryGetProperty("value", out var value) && (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out number)))
                throw new ArgumentException("A numeric value is required.");
            if (!double.IsFinite(number)) throw new ArgumentException("Invalid value.");
            var result = await dispatcher.InvokeAsync(() => Execute(command, number), DispatcherPriority.Normal, token).Task.ConfigureAwait(false); await Write(response, "application/json", JsonSerializer.SerializeToUtf8Bytes(result), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try { context.Response.StatusCode = ex is ArgumentException or JsonException or InvalidOperationException ? 400 : 500; } catch (Exception closed) when (closed is ObjectDisposedException or HttpListenerException) { }
        }
        finally { try { context.Response.Close(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { } }
    }
    private object State() => new { title = ServerActionCatalog.Bound(vm.CurrentItem?.Title, 200), playing = vm.IsPlaying, position = Math.Max(0, vm.Engine.GetTime()), duration = Math.Max(0, vm.Engine.GetDuration()), volume = Math.Clamp(vm.Volume, 0, 100), muted = vm.IsMuted, rate = vm.PlaybackRate, queueCount = vm.Playlist.Count, queue = vm.Playlist.Take(200).Select((item, index) => new { index, title = ServerActionCatalog.Bound(item.Title, 200) }).ToArray() };
    private object Execute(string command, double number)
    {
        switch (command)
        {
            case "play": if (!vm.HasMedia && vm.Playlist.Count == 0) throw new ArgumentException("Queue media on the computer first."); if (!vm.IsPlaying) vm.PlayPauseCommand.Execute(null); break;
            case "pause": if (vm.IsPlaying) vm.PlayPauseCommand.Execute(null); break;
            case "stop": vm.StopCommand.Execute(null); break;
            case "next": vm.NextCommand.Execute(null); break;
            case "previous": vm.PreviousCommand.Execute(null); break;
            case "volume": if (number is < 0 or > 100) throw new ArgumentException("Invalid volume."); vm.Volume = Math.Min(number, vm.MaxVolume); break;
            case "mute": if (number is not (0 or 1)) throw new ArgumentException("Invalid mute state."); if ((number == 1) != vm.IsMuted) vm.ToggleMuteCommand.Execute(null); break;
            case "seek": if (number < 0 || number > Math.Max(0, vm.TotalDurationMs)) throw new ArgumentException("Seek outside the known duration."); vm.SeekToMs((long)number); break;
            case "rate": if (number is < .5 or > 2) throw new ArgumentException("Invalid playback speed."); vm.PlaybackRate = (float)number; break;
            case "queue": if (number != Math.Truncate(number) || number < 0 || number >= vm.Playlist.Count) throw new ArgumentException("Unknown queue entry."); vm.PlayAt((int)number); break;
            default: throw new ArgumentException("Unknown remote command.");
        }
        return State();
    }
    private static async Task Write(HttpListenerResponse response, string type, byte[] bytes, CancellationToken token)
    { response.ContentType = type; response.ContentLength64 = bytes.Length; await response.OutputStream.WriteAsync(bytes, token).ConfigureAwait(false); }
    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
        if (_listener == null) return;
        _life!.Cancel(); _listener.Close();
        try { if (_loop != null) await _loop.ConfigureAwait(false); }
        finally
        {
            try { await Task.WhenAll(_jobs.Values.ToArray()).ConfigureAwait(false); }
            finally { _listener = null; _life.Dispose(); _life = null; _loop = null; _secret = _authority = Address = ""; }
        }
        }
        finally { _lifecycle.Release(); }
    }
}
