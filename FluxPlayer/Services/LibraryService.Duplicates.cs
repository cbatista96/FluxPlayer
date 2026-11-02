using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;

namespace FluxPlayer.Services;

public sealed record DuplicateMergePlan(string Keeper, ImmutableArray<LibraryEntry> Before, ImmutableArray<FileFingerprint> Files, LibraryEntry Merged);
public sealed record ConsolidatedDuplicate(FileFingerprint File, string Keeper);

public sealed partial class LibraryService
{
    private readonly Dictionary<string, ConsolidatedDuplicate> _consolidated = new(StringComparer.OrdinalIgnoreCase);
    private static void ValidateConsolidated(IEnumerable<ConsolidatedDuplicate> records)
    {
        var rows = records.ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in rows)
            if (seen.Count >= 200000 || item?.File == null || !System.IO.Path.IsPathFullyQualified(item.File.Path) || !System.IO.Path.IsPathFullyQualified(item.Keeper) || item.File.Path.Equals(item.Keeper, StringComparison.OrdinalIgnoreCase) || item.File.Size < 0 || item.File.ModifiedTicks <= 0 || item.File.Sha256?.Length != 64 || !item.File.Sha256.All(Uri.IsHexDigit) || !seen.Add(item.File.Path)) throw new InvalidDataException("Invalid consolidated duplicate records.");
        if (rows.Any(item => seen.Contains(item.Keeper))) throw new InvalidDataException("Consolidated paths must refer directly to retained entries, without chains or cycles.");
    }
    public async Task<int> RestoreConsolidatedDuplicates(CancellationToken token)
    {
        await _load; await _gate.WaitAsync(token);
        try
        {
            var before = _consolidated.Values.ToArray(); bool dirty = _dirty; _consolidated.Clear();
            try { await WriteStoreLockedAsync(token); _dirty = false; return before.Length; }
            catch { foreach (var item in before) _consolidated[item.File.Path] = item; _dirty = dirty; throw; }
        }
        finally { _gate.Release(); }
    }
    public async Task<int> AutoConsolidate(DuplicateGroup[] groups, IProgress<string>? progress, CancellationToken token)
    {
        if (groups.Length > 1000) throw new ArgumentException("Consolidate up to 1,000 groups per job.");
        int count = 0;
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var entries = (await SnapshotAsync()).Where(e => group.Paths.Contains(e.Path, StringComparer.OrdinalIgnoreCase)).OrderBy(e => e.AddedUtc).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (entries.Length < 2) continue;
            var plan = await PreviewDuplicateMerge(entries[0].Path, entries.Select(e => e.Path), token);
            count += await ApplyDuplicateMerge(plan, token); progress?.Report($"Consolidated {count} index entries. Retained {entries[0].Path}");
        }
        return count;
    }
    public async Task<DuplicateMergePlan> PreviewDuplicateMerge(string keeper, IEnumerable<string> paths, CancellationToken token)
    {
        string[] selected = paths.Append(keeper).Distinct(StringComparer.OrdinalIgnoreCase).Take(101).ToArray();
        if (selected.Length is < 2 or > 100) throw new ArgumentException("Merge 2–100 byte-identical local files.");
        var snapshot = (await SnapshotAsync()).ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        if (selected.Any(p => !snapshot.ContainsKey(p) || MediaFormats.IsNetwork(p))) throw new ArgumentException("Every source must be a local library entry.");
        var files = ImmutableArray.CreateBuilder<FileFingerprint>();
        foreach (string path in selected) files.Add(await LibraryFingerprints.Hash(path, token).ConfigureAwait(false));
        if (files.Any(f => f.Size != files[0].Size || f.Sha256 != files[0].Sha256)) throw new IOException("The selected sources are not byte-identical.");
        var before = selected.Select(p => snapshot[p]).ToImmutableArray();
        return new(keeper, before, files.ToImmutable(), MergeDuplicateMetadata(snapshot[keeper], before));
    }
    private static LibraryEntry MergeDuplicateMetadata(LibraryEntry keeper, ImmutableArray<LibraryEntry> entries)
    {
        string Fill(string current, Func<LibraryEntry, string> field) => !string.IsNullOrWhiteSpace(current) ? current : entries.Select(field).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
        return keeper with
        {
            Title = Fill(keeper.Title, e => e.Title), Artist = Fill(keeper.Artist, e => e.Artist), Album = Fill(keeper.Album, e => e.Album), Genre = Fill(keeper.Genre, e => e.Genre),
            Year = keeper.Year > 0 ? keeper.Year : entries.Max(e => e.Year), TrackNumber = keeper.TrackNumber > 0 ? keeper.TrackNumber : entries.Select(e => e.TrackNumber).FirstOrDefault(n => n > 0),
            Favorite = entries.Any(e => e.Favorite), Rating = keeper.Rating > 0 ? keeper.Rating : entries.Max(e => e.Rating),
            Labels = LibraryLabels.Canonicalize(entries.SelectMany(e => e.Labels)),
            PlayCount = (int)Math.Min(int.MaxValue, entries.Sum(e => (long)e.PlayCount)),
            AddedUtc = entries.Min(e => e.AddedUtc), LastPlayedUtc = entries.Max(e => e.LastPlayedUtc),
            DurationMs = keeper.DurationMs > 0 ? keeper.DurationMs : entries.Max(e => e.DurationMs)
        };
    }
    /// <summary>Atomically consolidates the index and collection memberships; media bytes and external playlists stay intact.</summary>
    public async Task<int> ApplyDuplicateMerge(DuplicateMergePlan plan, CancellationToken token)
    {
        if (plan.Before.IsDefault || plan.Files.IsDefault || plan.Before.Length is < 2 or > 100 || plan.Files.Length != plan.Before.Length
            || plan.Before.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Before.Length
            || !plan.Before.Any(e => e.Path.Equals(plan.Keeper, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Invalid duplicate merge plan.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); token = linked.Token;
        var streams = new List<FileStream>();
        await _load.ConfigureAwait(false); await _scanGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Hold all source handles through publication so no source can be replaced after hashing.
            for (int i = 0; i < plan.Before.Length; i++)
            {
                var file = plan.Files[i];
                if (!file.Path.Equals(plan.Before[i].Path, StringComparison.OrdinalIgnoreCase) || file.Size != plan.Files[0].Size || file.Sha256 != plan.Files[0].Sha256) throw new ArgumentException("Invalid duplicate identity.");
                var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan); streams.Add(stream);
                if (stream.Length != file.Size || File.GetLastWriteTimeUtc(file.Path).Ticks != file.ModifiedTicks || Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != file.Sha256) throw new IOException("A duplicate changed. Create a new preview.");
            }
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (plan.Before.Any(e => !_entries.TryGetValue(e.Path, out var current) || current != e)) throw new IOException("Library metadata changed. Create a new preview.");
                var keeper = plan.Before.Single(e => e.Path.Equals(plan.Keeper, StringComparison.OrdinalIgnoreCase));
                var merged = MergeDuplicateMetadata(keeper, plan.Before);
                var removed = plan.Before.Where(e => !e.Path.Equals(plan.Keeper, StringComparison.OrdinalIgnoreCase)).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var collections = _collections.Values.ToArray(); bool dirty = _dirty;
                var exclusions = _consolidated.Values.ToArray();
                if (_consolidated.Count + removed.Count > 200000) throw new ArgumentException("The consolidated-path limit was reached. Restore exclusions first.");
                foreach (var item in plan.Files.Where(f => removed.Contains(f.Path))) _consolidated[item.Path] = new(item, keeper.Path);
                foreach (var item in _consolidated.Values.Where(c => removed.Contains(c.Keeper)).ToArray()) _consolidated[item.File.Path] = item with { Keeper = keeper.Path };
                foreach (string path in removed) _entries.Remove(path);
                _entries[keeper.Path] = merged;
                foreach (var node in collections.Where(n => n.Paths.Any(removed.Contains)))
                    _collections[node.Id] = node with { Paths = node.Paths.Select(p => removed.Contains(p) ? keeper.Path : p).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray() };
                try { await WriteStoreLockedAsync(token).ConfigureAwait(false); _dirty = false; }
                catch
                {
                    foreach (var entry in plan.Before) _entries[entry.Path] = entry;
                    _consolidated.Clear(); foreach (var item in exclusions) _consolidated[item.File.Path] = item;
                    _collections.Clear(); foreach (var node in collections) _collections[node.Id] = node;
                    _dirty = dirty; throw;
                }
                return removed.Count;
            }
            finally { _gate.Release(); }
        }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); _scanGate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
}
