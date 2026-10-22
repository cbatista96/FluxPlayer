using FluxPlayer.Models;
using System.IO;
using System.Security.Cryptography;
using System.Diagnostics;

namespace FluxPlayer.Services;

public sealed record DuplicateGroup(string Hash, long Size, string[] Paths);
public sealed record DuplicateScanProgress(int Completed, int Candidates, string Path);
public sealed record DuplicateScanResult(DuplicateGroup[] Groups, int HashedFiles, int SkippedFiles);

/// <summary>Explicit, cancellable duplicate analysis. Never changes or deletes media.</summary>
public static class DuplicateFinder
{
    private sealed record Candidate(string Path, long Size, DateTime ModifiedUtc);

    public static Task<DuplicateScanResult> FindAsync(IEnumerable<LibraryEntry> entries,
        IProgress<DuplicateScanProgress>? progress = null, CancellationToken token = default)
    {
        var paths = entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.Run(async () =>
        {
            int skipped = 0;
            var files = new List<Candidate>();
            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (MediaFormats.IsNetwork(path)) continue;
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists) files.Add(new(path, file.Length, file.LastWriteTimeUtc));
                    else skipped++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                { skipped++; }
            }

            // Hash only files with equal actual sizes. Stream one candidate at a time to bound I/O and memory.
            var candidates = files.GroupBy(f => f.Size).Where(g => g.Count() > 1).SelectMany(g => g).ToArray();
            var hashes = new Dictionary<(long Size, string Hash), List<string>>();
            int completed = 0, hashed = 0;
            var reporting = Stopwatch.StartNew();
            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
                    var current = new FileInfo(candidate.Path);
                    if (!current.Exists || current.Length != candidate.Size || current.LastWriteTimeUtc != candidate.ModifiedUtc)
                        skipped++;
                    else
                    {
                        var key = (candidate.Size, hash);
                        if (!hashes.TryGetValue(key, out var group)) hashes[key] = group = new();
                        group.Add(candidate.Path); hashed++;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { skipped++; }
                completed++;
                if (completed == 1 || completed == candidates.Length || reporting.ElapsedMilliseconds >= 100)
                {
                    progress?.Report(new(completed, candidates.Length, candidate.Path));
                    reporting.Restart();
                }
            }

            var result = hashes.Where(pair => pair.Value.Count > 1)
                .Select(pair => new DuplicateGroup(pair.Key.Hash, pair.Key.Size,
                    pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray()))
                .OrderByDescending(g => g.Size).ThenBy(g => g.Paths[0], StringComparer.OrdinalIgnoreCase).ToArray();
            return new DuplicateScanResult(result, hashed, skipped);
        }, token);
    }
}
