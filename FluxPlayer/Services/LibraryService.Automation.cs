using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;

namespace FluxPlayer.Services;

public sealed record FileFingerprint(string Path, long Size, long ModifiedTicks, string Sha256);
public sealed record RelinkCandidate(string Missing, string Found, string Sha256, long Size, long ModifiedTicks);
public sealed class LibraryFingerprints
{
    private sealed record Data(ImmutableArray<FileFingerprint> Files);
    private readonly ProfileDocument<Data> _store;
    public LibraryFingerprints(string folder) => _store = new(Path.Combine(folder, "library-fingerprints.json"), () => new([]), data => { if (data.Files.IsDefault || data.Files.Length > 200_000 || data.Files.Any(item => item == null || string.IsNullOrWhiteSpace(item.Path) || item.Size < 0 || item.Sha256?.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid library fingerprints."); }, 64 * 1024 * 1024);
    public static async Task<FileFingerprint> Hash(string path, CancellationToken token)
    {
        var info = new FileInfo(path); long size = info.Length, stamp = info.LastWriteTimeUtc.Ticks; await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan); string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)); info.Refresh(); if (size != info.Length || stamp != info.LastWriteTimeUtc.Ticks) throw new IOException("A source changed while being fingerprinted."); return new(info.FullName, size, stamp, hash);
    }
    public async Task<int> Capture(IEnumerable<LibraryEntry> entries, IProgress<string>? progress, CancellationToken token)
    {
        var result = new List<FileFingerprint>(); foreach (var entry in entries.Take(200_001)) { token.ThrowIfCancellationRequested(); if (result.Count == 200_000) throw new ArgumentException("Fingerprint up to 200,000 files."); if (File.Exists(entry.Path)) { result.Add(await Hash(entry.Path, token)); progress?.Report(entry.Title); } }
        await _store.Edit(data => new(data.Files.Concat(result).GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToImmutableArray()), token); return result.Count;
    }
    public async Task<RelinkCandidate[]> Find(IEnumerable<LibraryEntry> entries, string folder, IProgress<string>? progress, CancellationToken token)
    {
        var paths = entries.Where(item => !File.Exists(item.Path)).Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase); var missing = (await _store.Read(token)).Files.Where(item => paths.Contains(item.Path)).ToArray(); if (missing.Length == 0) return [];
        var sizes = missing.Select(item => item.Size).ToHashSet(); var found = new List<FileFingerprint>(); int visited = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        { token.ThrowIfCancellationRequested(); if (++visited > 200_000) throw new IOException("Search is limited to 200,000 files. Choose a smaller folder."); if (sizes.Contains(new FileInfo(path).Length)) { found.Add(await Hash(path, token)); progress?.Report(Path.GetFileName(path)); } }
        var unique = found.GroupBy(item => (item.Size, item.Sha256)).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
        return missing.Where(item => unique.ContainsKey((item.Size, item.Sha256))).Select(item =>
        {
            var match = unique[(item.Size, item.Sha256)];
            return new RelinkCandidate(item.Path, match.Path, item.Sha256, item.Size, match.ModifiedTicks);
        }).GroupBy(item => item.Found, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
    }
}
public sealed partial class LibraryService
{
    public Task<int> ApplyComputedRatings(IReadOnlyDictionary<string, int> ratings, CancellationToken token) => Task.Run(async () =>
    {
        if (ratings.Count > 10_000 || ratings.Values.Any(value => value is < 1 or > 5)) throw new ArgumentException("Use at most 10,000 valid ratings."); await _load; await _gate.WaitAsync(token);
        try { var before = _entries.Where(pair => ratings.ContainsKey(pair.Key) && pair.Value.Rating == 0).ToArray(); bool dirty = _dirty; foreach (var pair in before) _entries[pair.Key] = pair.Value with { Rating = ratings[pair.Key] }; try { await WriteStoreLockedAsync(token); _dirty = false; } catch { foreach (var pair in before) _entries[pair.Key] = pair.Value; _dirty = dirty; throw; } return before.Length; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }, token);
    public async Task Relink(IReadOnlyList<RelinkCandidate> plan, CancellationToken token)
    {
        if (plan.Count is < 1 or > 1000 || plan.Select(item => item.Missing).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Count || plan.Select(item => item.Found).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Count) throw new ArgumentException("Repair 1–1,000 unique missing paths.");
        foreach (var item in plan) { token.ThrowIfCancellationRequested(); if (File.Exists(item.Missing)) throw new IOException("An original source reappeared. Refresh the preview."); var hash = await LibraryFingerprints.Hash(item.Found, token); if (hash.Size != item.Size || hash.ModifiedTicks != item.ModifiedTicks || hash.Sha256 != item.Sha256) throw new IOException("A replacement changed. Refresh the preview."); }
        await _load; await _scanGate.WaitAsync(token); try { await _gate.WaitAsync(token); try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); if (plan.Any(item => !_entries.ContainsKey(item.Missing) || _entries.ContainsKey(item.Found) || File.Exists(item.Missing) || !File.Exists(item.Found) || new FileInfo(item.Found).Length != item.Size || new FileInfo(item.Found).LastWriteTimeUtc.Ticks != item.ModifiedTicks)) throw new IOException("The index or a source changed. Rebuild the repair preview.");
            var before = _entries.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase); var collections = _collections.Values.ToArray(); bool dirty = _dirty;
            foreach (var item in plan) { var old = _entries[item.Missing]; _entries.Remove(item.Missing); _entries[item.Found] = old with { Path = item.Found, ModifiedUtc = new DateTime(item.ModifiedTicks, DateTimeKind.Utc) }; foreach (var node in _collections.Values.Where(node => node.Paths.Contains(item.Missing, StringComparer.OrdinalIgnoreCase)).ToArray()) _collections[node.Id] = node with { Paths = node.Paths.Select(path => path.Equals(item.Missing, StringComparison.OrdinalIgnoreCase) ? item.Found : path).ToImmutableArray() }; }
            try { await WriteStoreLockedAsync(token); _dirty = false; } catch { _entries.Clear(); foreach (var pair in before) _entries[pair.Key] = pair.Value; _collections.Clear(); foreach (var node in collections) _collections[node.Id] = node; _dirty = dirty; throw; }
        } finally { _gate.Release(); } } finally { _scanGate.Release(); } Changed?.Invoke(this, EventArgs.Empty);
    }
}
