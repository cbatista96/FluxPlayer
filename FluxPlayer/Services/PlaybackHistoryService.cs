using FluxPlayer.Models;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

/// <summary>Profile-local session history. Constructing the service performs no I/O.</summary>
public sealed class PlaybackHistoryService(string directory)
{
    private const int MaximumBytes = 32 * 1024 * 1024;
    public const int MaximumEntries = 5000;
    private readonly string _path = System.IO.Path.Combine(directory, "playback-history.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PlaybackHistoryEntry[]? _entries;
    private sealed record Store(int Version, PlaybackHistoryEntry[] Entries);
    private static void Validate(PlaybackHistoryEntry entry)
    {
        if (entry == null || !Guid.TryParseExact(entry.Id, "N", out _) || string.IsNullOrWhiteSpace(entry.Path) || entry.Path.Length > 32767 || entry.Path.Contains('\0')
            || entry.Title == null || entry.Title.Length > 4096 || entry.Artist == null || entry.Artist.Length > 4096 || entry.Kind is not ("Audio" or "Video")
            || entry.DurationMs is < 0 or > 315_360_000_000 || entry.PositionMs is < 0 or > 315_360_000_000 || entry.PlayedMs is < 0 or > 315_360_000_000
            || entry.StartedUtc > entry.UpdatedUtc || entry.State is not ("Playing" or "Paused" or "Stopped" or "Completed" or "Switched" or "Error" or "Closed"))
            throw new InvalidDataException("The playback history contains an invalid session.");
    }
    private async Task LoadAsync(CancellationToken token)
    {
        if (_entries != null) return;
        if (!File.Exists(_path)) { _entries = Array.Empty<PlaybackHistoryEntry>(); return; }
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Playback history exceeds 32 MiB.");
        Store data;
        try { data = await JsonSerializer.DeserializeAsync<Store>(stream, cancellationToken: token).ConfigureAwait(false) ?? throw new InvalidDataException("Playback history is empty."); }
        catch (JsonException e) { throw new InvalidDataException("Playback history is not valid JSON. The existing file was preserved.", e); }
        if (data.Version != 1 || data.Entries == null || data.Entries.Length > MaximumEntries) throw new InvalidDataException("Unsupported playback history version or session count.");
        foreach (var entry in data.Entries) Validate(entry);
        if (data.Entries.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Entries.Length) throw new InvalidDataException("Duplicate playback history identities.");
        _entries = data.Entries;
    }
    private static PlaybackHistoryEntry[] Retain(IEnumerable<PlaybackHistoryEntry> entries, int limit, int days, DateTimeOffset now)
    {
        if (limit is < 1 or > MaximumEntries || days is < 1 or > 3650) throw new ArgumentException("Use 1–5,000 history entries and 1–3,650 retention days.");
        var cutoff = now.AddDays(-days);
        return entries.Where(entry => entry.StartedUtc >= cutoff).OrderByDescending(entry => entry.StartedUtc).ThenBy(entry => entry.Id, StringComparer.Ordinal).Take(limit).ToArray();
    }
    public Task<PlaybackHistoryEntry[]> SnapshotAsync(int limit, int days, CancellationToken token = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await LoadAsync(token).ConfigureAwait(false); return Retain(_entries!, limit, days, DateTimeOffset.UtcNow); }
        finally { _gate.Release(); }
    }, token);
    public Task SaveAsync(PlaybackHistoryEntry entry, int limit, int days, CancellationToken token = default)
    {
        Validate(entry);
        return EditAsync(entries => Retain(entries.Where(previous => !string.Equals(previous.Id, entry.Id, StringComparison.OrdinalIgnoreCase)).Append(entry), limit, days, DateTimeOffset.UtcNow), token);
    }
    public Task ClearAsync(CancellationToken token = default) => EditAsync(_ => Array.Empty<PlaybackHistoryEntry>(), token, clear: true);
    private Task EditAsync(Func<PlaybackHistoryEntry[], PlaybackHistoryEntry[]> edit, CancellationToken token, bool clear = false) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!clear) await LoadAsync(token).ConfigureAwait(false);
            var revised = edit(_entries ?? Array.Empty<PlaybackHistoryEntry>()); token.ThrowIfCancellationRequested();
            await WriteAsync(_path, revised, overwrite: true, token).ConfigureAwait(false); _entries = revised;
        }
        finally { _gate.Release(); }
    }, token);
    public static async Task ExportCopyAsync(string destination, IEnumerable<PlaybackHistoryEntry> entries, CancellationToken token = default)
    {
        var snapshot = entries.Take(MaximumEntries + 1).ToArray();
        if (snapshot.Length > MaximumEntries) throw new ArgumentException("Export up to 5,000 history records.");
        foreach (var entry in snapshot) Validate(entry);
        await WriteAsync(System.IO.Path.GetFullPath(destination), snapshot, overwrite: false, token).ConfigureAwait(false);
    }
    private static async Task WriteAsync(string path, PlaybackHistoryEntry[] entries, bool overwrite, CancellationToken token)
    {
        if (!overwrite && (File.Exists(path) || Directory.Exists(path))) throw new IOException("Choose a new export filename.");
        string directory = System.IO.Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new Store(1, entries), cancellationToken: token).ConfigureAwait(false);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("Playback history exceeds 32 MiB.");
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
