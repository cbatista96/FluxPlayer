using FluxPlayer.Models;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace FluxPlayer.Services;

/// <summary>
/// Honors "Allow several instances = off": the first process owns a per-profile mutex and listens on a named pipe;
/// later launches hand their file arguments to it and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaximumMessageBytes = 256 * 1024, MaximumSources = 500;
    private readonly Mutex _mutex;
    private readonly string _pipe;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    private SingleInstance(Mutex mutex, string pipe) { _mutex = mutex; _pipe = pipe; }

    private static string Key(string dataDirectory)
    {
        string normalized = Path.GetFullPath(dataDirectory).TrimEnd('\\', '/').ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }

    /// <summary>Returns the owning instance, or null when another instance already runs and received <paramref name="sources"/>.</summary>
    public static SingleInstance? TryOwn(string dataDirectory, IReadOnlyList<string> sources)
    {
        string key = Key(dataDirectory), pipe = "FluxPlayer-" + key;
        var mutex = new Mutex(true, "Local\\FluxPlayer-" + key, out bool created);
        if (created) return new SingleInstance(mutex, pipe);
        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(2000);
            byte[] payload = Encoding.UTF8.GetBytes(string.Join('\n', sources.Take(MaximumSources).Select(Absolute)));
            if (payload.Length <= MaximumMessageBytes) client.Write(payload);
            client.Flush();
            return null;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The first instance is starting or hung: do not lock the user out, run normally.
            return new SingleInstance(new Mutex(false), pipe);
        }
    }

    private static string Absolute(string source)
    {
        try { return MediaFormats.IsNetwork(source) ? source : Path.GetFullPath(source); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return source; }
    }

    public void Listen(Action<string[]> received)
    {
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192]; int read;
                    while ((read = await server.ReadAsync(chunk, _stop.Token).ConfigureAwait(false)) > 0 && buffer.Length <= MaximumMessageBytes) buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaximumMessageBytes) continue;
                    received(Parse(Encoding.UTF8.GetString(buffer.ToArray())));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException) { await Task.Delay(250).ConfigureAwait(false); }
            }
        });
    }

    internal static string[] Parse(string message) => message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.Length is > 0 and < 32768 && !line.Any(char.IsControl)).Take(MaximumSources).ToArray();

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.Wait(500); } catch (AggregateException) { }
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose(); _stop.Dispose();
    }
}
