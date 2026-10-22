using FluxPlayer.Models;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed partial class LibraryService
{
    /// <summary>Changes local index labels in one persisted transaction; media files are never opened for writing.</summary>
    public Task<int> UpdateLabelsAsync(IEnumerable<string> paths, IEnumerable<string> labels, LibraryLabelEdit edit,
        CancellationToken token = default)
    {
        var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(10_001).ToArray();
        if (selected.Length > 10_000) throw new ArgumentException("Select up to 10,000 files per label edit.");
        var values = LibraryLabels.Canonicalize(labels);
        if (!Enum.IsDefined(edit)) throw new ArgumentException("Choose Add or Remove labels.");
        return Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _load.ConfigureAwait(false);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            int changed = 0;
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var updates = new List<(string Path, LibraryEntry Old, LibraryEntry New)>();
                foreach (string path in selected)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (!_entries.TryGetValue(path, out var entry)) continue;
                    var revised = LibraryLabels.Canonicalize(edit == LibraryLabelEdit.Add ? entry.Labels.Concat(values)
                        : entry.Labels.Where(label => !values.Contains(label, StringComparer.OrdinalIgnoreCase)));
                    if (!entry.Labels.SequenceEqual(revised)) updates.Add((path, entry, entry with { Labels = revised }));
                }
                if (updates.Count == 0) return 0;
                bool wasDirty = _dirty;
                foreach (var update in updates) _entries[update.Path] = update.New;
                try
                {
                    await WriteStoreLockedAsync(linked.Token).ConfigureAwait(false);
                    _dirty = false; LastError = null; changed = updates.Count;
                }
                catch
                {
                    foreach (var update in updates) _entries[update.Path] = update.Old;
                    _dirty = wasDirty; throw;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; throw; }
            finally { _gate.Release(); }
            Changed?.Invoke(this, EventArgs.Empty); return changed;
        }, token);
    }

    // The caller holds _gate, including while serializing, so the file represents one coherent index.
    private async Task WriteStoreLockedAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await JsonSerializer.SerializeAsync(file, new Store(1, _folders.ToArray(), _entries.Values.ToArray(), _lists.Values.ToArray()), cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
