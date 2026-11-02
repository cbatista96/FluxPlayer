using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

/// <summary>Lazy, serialized profile document. Publish disk before replacing the immutable snapshot.</summary>
internal sealed class ProfileDocument<T>(string path, Func<T> empty, Action<T> validate, int maximumBytes = 32 * 1024 * 1024) where T : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private T? _data;
    private sealed record Envelope(int Version, T Data);
    private async Task Load(CancellationToken token)
    {
        if (_data != null) return;
        if (!File.Exists(path)) { _data = empty(); return; }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > maximumBytes) throw new InvalidDataException("The profile document exceeds its size limit.");
        Envelope? value;
        try { value = await JsonSerializer.DeserializeAsync<Envelope>(stream, cancellationToken: token).ConfigureAwait(false); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid profile JSON. The original file was preserved.", ex); }
        if (value?.Version != 1 || value.Data == null) throw new InvalidDataException("Unsupported profile document.");
        validate(value.Data); _data = value.Data;
    }
    public Task<T> Read(CancellationToken token = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await Load(token).ConfigureAwait(false); return _data!; } finally { _gate.Release(); }
    }, token);
    public Task Edit(Func<T, T> change, CancellationToken token = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Load(token).ConfigureAwait(false); var revised = change(_data!); validate(revised);
            await Write(path, revised, overwrite: true, maximumBytes, token).ConfigureAwait(false); _data = revised;
        }
        finally { _gate.Release(); }
    }, token);
    public static async Task Write(string destination, T data, bool overwrite, int maximumBytes, CancellationToken token)
    {
        destination = Path.GetFullPath(destination); token.ThrowIfCancellationRequested();
        if (!overwrite && (File.Exists(destination) || Directory.Exists(destination))) throw new IOException("Choose a new filename.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new Envelope(1, data), cancellationToken: token).ConfigureAwait(false);
                if (stream.Length > maximumBytes) throw new InvalidDataException("The profile document exceeds its size limit.");
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
