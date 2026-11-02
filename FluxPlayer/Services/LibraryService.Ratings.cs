using System.IO;

namespace FluxPlayer.Services;

public sealed partial class LibraryService
{
    public Task<int> SetRatings(IEnumerable<string> paths, int rating, CancellationToken token = default)
    {
        if (rating is < 0 or > 5) throw new ArgumentException("Use a rating from 0 (unrated) to 5.");
        var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(10_001).ToArray(); if (selected.Length > 10_000) throw new ArgumentException("Rate up to 10,000 files at a time.");
        return Task.Run(async () =>
        {
            await _load.ConfigureAwait(false); await _gate.WaitAsync(token).ConfigureAwait(false); int count;
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this); var before = selected.Where(_entries.ContainsKey).Select(path => _entries[path]).Where(entry => entry.Rating != rating).ToArray();
                if (before.Length == 0) return 0; bool dirty = _dirty;
                foreach (var entry in before) _entries[entry.Path] = entry with { Rating = rating };
                try { await WriteStoreLockedAsync(token).ConfigureAwait(false); _dirty = false; count = before.Length; }
                catch { foreach (var entry in before) _entries[entry.Path] = entry; _dirty = dirty; throw; }
            }
            finally { _gate.Release(); }
            Changed?.Invoke(this, EventArgs.Empty); return count;
        }, token);
    }
}
