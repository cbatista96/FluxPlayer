using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed class MpdConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private Stream? _stream;
    private readonly byte[] _readBuffer = new byte[16384];
    private int _readOffset, _readCount;
    private bool _disposed;
    public string Version { get; private set; } = "";
    public async Task Connect(string host, int port, bool tls, string password, CancellationToken token)
    {
        if (host.Length is < 1 or > 253 || host.Any(char.IsControl) || port is < 1 or > 65535 || password.Length > 2048 || password.Any(char.IsControl)) throw new ArgumentException("Check host, port and bounded session password.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MpdConnection)); Disconnect(); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            _tcp = new TcpClient(); await _tcp.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false); _tcp.NoDelay = true;
            if (!tls && (_tcp.Client.RemoteEndPoint is not IPEndPoint endpoint || !IPAddress.IsLoopback(endpoint.Address))) throw new ArgumentException("Plain MPD is allowed only on loopback. Use a certificate-validating TLS proxy for remote connections.");
            _stream = _tcp.GetStream(); if (tls) { var ssl = new SslStream(_stream, false); _stream = ssl; await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, deadline.Token).ConfigureAwait(false); }
            string greeting = await Line(deadline.Token).ConfigureAwait(false); if (!greeting.StartsWith("OK MPD ", StringComparison.Ordinal)) throw new IOException("The endpoint is not an MPD server."); Version = greeting[7..];
            if (password != "") { await Send("password " + Quote(password), deadline.Token).ConfigureAwait(false); try { _ = await Response(deadline.Token).ConfigureAwait(false); } catch (IOException) { throw new IOException("MPD session authentication failed."); } }
        }
        catch { Disconnect(); throw; }
        finally { _gate.Release(); }
    }
    public async Task<JsonElement> Execute(string key, JsonElement input, CancellationToken token)
    {
        var action = InteropCatalog.Mpd.FirstOrDefault(a => a.Key == key) ?? throw new ArgumentException("Unknown MPD command.");
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() > 64 || input.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || v.GetString()!.Length > 4096 || v.GetString()!.Any(char.IsControl))) throw new ArgumentException("Use a JSON array of up to 64 bounded string arguments, without control characters.");
        JsonElement prefix = action.Schema.GetProperty("Prefix");
        IEnumerable<string?> commandParts = prefix.ValueKind switch
        {
            JsonValueKind.String => new[] { prefix.GetString() },
            JsonValueKind.Array => prefix.EnumerateArray().Select(value => value.GetString()),
            _ => throw new InvalidDataException("The MPD command catalog has an invalid prefix.")
        };
        string command = string.Join(' ', commandParts.Concat(input.EnumerateArray().Select(v => Quote(v.GetString()!))));
        if (Encoding.UTF8.GetByteCount(command) > 65536) throw new ArgumentException("The command exceeds 64 KiB.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed || _stream == null) throw new IOException("Connect an MPD session first."); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await Send(command, timeout.Token).ConfigureAwait(false); var lines = await Response(timeout.Token).ConfigureAwait(false); return JsonSerializer.SerializeToElement(new { ProtocolVersion = Version, Lines = lines });
        }
        catch { Disconnect(); throw; }
        finally { _gate.Release(); }
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private async Task Send(string command, CancellationToken token)
    { byte[] bytes = Encoding.UTF8.GetBytes(command + "\n"); await _stream!.WriteAsync(bytes, token).ConfigureAwait(false); await _stream.FlushAsync(token).ConfigureAwait(false); }
    private async Task<string> Line(CancellationToken token)
    {
        using var output = new MemoryStream();
        while (true)
        {
            token.ThrowIfCancellationRequested(); if (_readOffset == _readCount) { _readCount = await _stream!.ReadAsync(_readBuffer, token).ConfigureAwait(false); _readOffset = 0; if (_readCount == 0) throw new EndOfStreamException("The MPD server disconnected."); }
            int end = Array.IndexOf(_readBuffer, (byte)10, _readOffset, _readCount - _readOffset); int count = (end < 0 ? _readCount : end) - _readOffset;
            if (output.Length + count > 65536) throw new IOException("An MPD response line exceeded 64 KiB."); output.Write(_readBuffer, _readOffset, count); _readOffset += count;
            if (end >= 0) { _readOffset++; break; }
        }
        return new UTF8Encoding(false, true).GetString(output.ToArray()).TrimEnd('\r');
    }
    private async Task<string[]> Response(CancellationToken token)
    {
        var lines = new List<string>(); int bytes = 0;
        while (true)
        {
            string line = await Line(token).ConfigureAwait(false); if (line == "OK") return lines.ToArray(); if (line.StartsWith("ACK ", StringComparison.Ordinal)) throw new IOException("MPD rejected the command: " + ServerActionCatalog.Bound(line, 1000));
            if (line.StartsWith("binary: ", StringComparison.Ordinal)) throw new IOException("Binary MPD responses are not supported by this text session.");
            bytes += Encoding.UTF8.GetByteCount(line); if (bytes > 4 * 1024 * 1024 || lines.Count >= 20000) throw new IOException("The MPD result exceeded 4 MiB / 20,000 lines; narrow the requested range."); lines.Add(line);
        }
    }
    private void Disconnect() { _stream?.Dispose(); _stream = null; _tcp?.Dispose(); _tcp = null; Version = ""; _readOffset = _readCount = 0; }
    public async ValueTask DisposeAsync() { await _gate.WaitAsync().ConfigureAwait(false); try { _disposed = true; Disconnect(); } finally { _gate.Release(); } }
}
