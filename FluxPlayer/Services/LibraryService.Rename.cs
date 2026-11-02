using System.IO;
using System.Collections.Immutable;

namespace FluxPlayer.Services;

public sealed partial class LibraryService
{
    internal async Task<RenameResult> RenameIndexedFileAsync(RenameEntry entry, Func<string, bool> isLoaded, string? organizationRoot = null)
    {
        await _load.ConfigureAwait(false);
        await _scanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                FileRenameService.Check(entry);
                if (isLoaded(entry.Source)) return new(entry, false, "The file is loaded in the player. Open another source before renaming it.");
                if (_entries.ContainsKey(entry.Destination)) return new(entry, false, "The destination already has a library record.");
                if (organizationRoot != null)
                {
                    FileOrganizationService.Check(entry, organizationRoot);
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Destination)!);
                    FileOrganizationService.Check(entry, organizationRoot);
                }
                File.Move(entry.Source, entry.Destination, false);
                if (_entries.Remove(entry.Source, out var record)) _entries[entry.Destination] = record with { Path = entry.Destination };
                foreach (var node in _collections.Values.Where(node => node.Paths.Contains(entry.Source, StringComparer.OrdinalIgnoreCase)).ToArray())
                    _collections[node.Id] = node with { Paths = node.Paths.Select(path => string.Equals(path, entry.Source, StringComparison.OrdinalIgnoreCase) ? entry.Destination : path).ToImmutableArray() };
                _dirty = true;
                return new(entry, true, (organizationRoot == null ? "Renamed" : "Moved") + "; media bytes and indexed metadata preserved.");
            }
            finally { _gate.Release(); }
        }
        finally { _scanGate.Release(); }
    }

    internal async Task PersistRenamesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await WriteStoreLockedAsync().ConfigureAwait(false); _dirty = false; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
}
