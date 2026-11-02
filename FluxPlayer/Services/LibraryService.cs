using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

/// <summary>Lazy local index. Scans names first; expensive decoding is always an explicit operation.</summary>
public sealed partial class LibraryService : IAsyncDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, LibraryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SmartList> _lists = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Task _load;
    private readonly Timer _saveTimer;
    private readonly Timer _watchTimer;
    private bool _dirty;
    private bool _disposed;
    public string? LastError { get; private set; }
    public event EventHandler? Changed;
    internal void NotifyMetadataChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public LibraryService(string? dataDirectory = null)
    {
        _path = System.IO.Path.Combine(dataDirectory ?? AppProfile.Current.DataDirectory, "library.json");
        _saveTimer = new Timer(_ => _ = SaveSafeAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watchTimer = new Timer(_ => _ = RefreshWatchedAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _load = Task.Run(LoadAsync);
    }

    private sealed record Store(int Version, string[] Folders, LibraryEntry[] Entries, SmartList[]? Lists = null, LibraryCollection[]? Collections = null, ConsolidatedDuplicate[]? Consolidated = null);

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return;
            await using var file = File.OpenRead(_path);
            var store = await JsonSerializer.DeserializeAsync<Store>(file);
            if (store == null || store.Version != 1) return;
            foreach (var entry in store.Entries.Take(200_000))
                if (!string.IsNullOrWhiteSpace(entry.Path)) _entries[entry.Path] = entry with { Kind = NormalizeKind(entry.Kind), Labels = LibraryLabels.Sanitize(entry.Labels), Year = Math.Clamp(entry.Year, 0, 9999), Rating = Math.Clamp(entry.Rating, 0, 5) };
            foreach (var folder in store.Folders) if (Directory.Exists(folder)) _folders.Add(folder);
            foreach (var list in (store.Lists ?? Array.Empty<SmartList>()).Take(5000))
                if (list?.IsValid == true) _lists[list.Name] = list;
            var collections = store.Collections ?? Array.Empty<LibraryCollection>();
            LibraryCollections.Validate(collections);
            foreach (var node in collections) _collections[node.Id] = node with { Paths = node.Paths.Where(_entries.ContainsKey).ToImmutableArray() };
            ConfigureWatchers();
            var consolidated = store.Consolidated ?? [];
            ValidateConsolidated(consolidated);
            foreach (var item in consolidated) if (_entries.ContainsKey(item.Keeper)) _consolidated[item.File.Path] = item;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastError = $"Unable to read the library: {e.Message}"; }
    }

    public async Task<LibraryEntry[]> SnapshotAsync()
    {
        await _load; await _gate.WaitAsync();
        try { return _entries.Values.ToArray(); } finally { _gate.Release(); }
    }

    private static string NormalizeKind(string? kind) =>
        string.Equals(kind, "Video", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "V\u00eddeo", StringComparison.OrdinalIgnoreCase) ? "Video" : "Audio";

    public async Task<string[]> FoldersAsync()
    {
        await _load; await _gate.WaitAsync();
        try { return _folders.ToArray(); } finally { _gate.Release(); }
    }

    public async Task<SmartList[]> SmartListsAsync()
    {
        await _load; await _gate.WaitAsync();
        try { return _lists.Values.OrderBy(x => x.Name).ToArray(); } finally { _gate.Release(); }
    }
    public async Task SaveSmartListAsync(SmartList list)
    {
        if (!list.IsValid) throw new ArgumentException("Use a name of 1–120 characters, rules and artist/album names up to 4096 characters, personal labels up to 64 printable characters, and valid media/collection filters.");
        await _load; await _gate.WaitAsync();
        try
        {
            if (list.CollectionId != null && !_collections.ContainsKey(list.CollectionId)) throw new ArgumentException("The selected collection no longer exists.");
            _lists[list.Name.Trim()] = list with { Name = list.Name.Trim() }; MarkDirty();
        }
        finally { _gate.Release(); }
    }
    public async Task RemoveSmartListAsync(string name)
    {
        await _load; await _gate.WaitAsync();
        try { if (_lists.Remove(name)) MarkDirty(); } finally { _gate.Release(); }
    }

    public async Task<int> ScanFolderAsync(string root, bool watch, IProgress<int>? progress = null, CancellationToken token = default)
    {
        await _load;
        root = System.IO.Path.GetFullPath(root);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _scanGate.WaitAsync(linked.Token);
        try
        {
            int count = 0;
            // Enumeration, file attributes, and batch construction never run on the dispatcher.
            await Task.Run(async () =>
            {
                var batch = new List<LibraryEntry>(256);
                foreach (string path in MediaFormats.EnumerateFolder(root))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        batch.Add(new LibraryEntry
                        {
                            Path = path,
                            Title = System.IO.Path.GetFileNameWithoutExtension(path),
                            Kind = MediaFormats.IsAudio(path) ? "Audio" : "Video",
                            Size = info.Length,
                            ModifiedUtc = info.LastWriteTimeUtc
                        });
                    }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    if (batch.Count < 256) continue;
                    count += await MergeAsync(batch, linked.Token); batch.Clear(); progress?.Report(count);
                }
                count += await MergeAsync(batch, linked.Token); progress?.Report(count);
            }, linked.Token);
            if (watch)
            {
                await _gate.WaitAsync(linked.Token);
                try { _folders.Add(root); ConfigureWatchers(); MarkDirty(); } finally { _gate.Release(); }
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return count;
        }
        finally { _scanGate.Release(); }
    }

    private async Task<int> MergeAsync(List<LibraryEntry> batch, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var item in batch)
            {
                if (_consolidated.TryGetValue(item.Path, out var duplicate))
                {
                    if (_entries.ContainsKey(duplicate.Keeper) && item.Size == duplicate.File.Size && item.ModifiedUtc.Ticks == duplicate.File.ModifiedTicks) continue;
                    _consolidated.Remove(item.Path);
                }
                if (_entries.TryGetValue(item.Path, out var old))
                    _entries[item.Path] = old with
                    {
                        Size = item.Size,
                        ModifiedUtc = item.ModifiedUtc,
                        MetadataRead = old.MetadataRead && old.ModifiedUtc == item.ModifiedUtc
                    };
                else if (_entries.Count < 200_000) _entries.Add(item.Path, item);
            }
            if (batch.Count > 0) MarkDirty();
            return batch.Count;
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateMetadataAsync(string path, MediaMetadata metadata, bool countPlayback = false, bool refreshFileStamp = false)
    {
        (long Size, DateTime ModifiedUtc)? stamp = null;
        if (refreshFileStamp && !MediaFormats.IsNetwork(path))
            stamp = await Task.Run<(long, DateTime)?>(() =>
            {
                try { var file = new FileInfo(path); return (file.Length, file.LastWriteTimeUtc); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
            });
        await _load; await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            if (_consolidated.TryGetValue(path, out var duplicate) && _entries.TryGetValue(duplicate.Keeper, out var retained))
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length == duplicate.File.Size && info.LastWriteTimeUtc.Ticks == duplicate.File.ModifiedTicks)
                {
                    if (countPlayback) { _entries[retained.Path] = retained with { PlayCount = retained.PlayCount == int.MaxValue ? int.MaxValue : retained.PlayCount + 1, LastPlayedUtc = DateTime.UtcNow }; MarkDirty(); }
                    return;
                }
                _consolidated.Remove(path);
            }
            _entries.TryGetValue(path, out var old);
            old ??= new LibraryEntry { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path) };
            _entries[path] = old with
            {
                Title = string.IsNullOrWhiteSpace(metadata.Title) ? System.IO.Path.GetFileNameWithoutExtension(path) : metadata.Title,
                Artist = metadata.Artist,
                Album = metadata.Album,
                Genre = metadata.Genre,
                Year = Math.Clamp(metadata.Year, 0, 9999),
                TrackNumber = Math.Clamp(metadata.TrackNumber, 0, 65535),
                DurationMs = metadata.DurationMs > 0 ? metadata.DurationMs : old.DurationMs,
                Size = stamp?.Size ?? old.Size,
                ModifiedUtc = stamp?.ModifiedUtc ?? old.ModifiedUtc,
                Kind = metadata.HasVideo ? "Video" : "Audio",
                MetadataRead = true,
                PlayCount = old.PlayCount + (countPlayback ? 1 : 0),
                LastPlayedUtc = countPlayback ? DateTime.UtcNow : old.LastPlayedUtc
            };
            MarkDirty();
        }
        finally { _gate.Release(); }
    }

    public Task RecordPlaybackAsync(string path, MediaMetadata metadata) => UpdateMetadataAsync(path, metadata, true);

    public async Task<bool> IsFavoriteAsync(string path)
    {
        await _load; await _gate.WaitAsync();
        try { return _entries.TryGetValue(path, out var entry) && entry.Favorite; } finally { _gate.Release(); }
    }

    public async Task<bool> ToggleFavoriteAsync(string path)
    {
        await _load; await _gate.WaitAsync();
        try
        {
            _entries.TryGetValue(path, out var entry);
            entry ??= new LibraryEntry { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path), Kind = MediaFormats.IsAudio(path) ? "Audio" : "Video" };
            _entries[path] = entry with { Favorite = !entry.Favorite }; MarkDirty();
            return !entry.Favorite;
        }
        finally { _gate.Release(); }
    }

    public async Task StopWatchingAsync(string root)
    {
        await _load; await _gate.WaitAsync();
        try { _folders.Remove(root); ConfigureWatchers(); MarkDirty(); } finally { _gate.Release(); }
    }

    private void ConfigureWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        foreach (string folder in _folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };
                FileSystemEventHandler changed = (_, _) => { if (!_disposed) _watchTimer.Change(1500, Timeout.Infinite); };
                watcher.Created += changed; watcher.Changed += changed; watcher.Deleted += changed;
                watcher.Renamed += (_, _) => { if (!_disposed) _watchTimer.Change(1500, Timeout.Infinite); };
                watcher.Error += (_, _) => { if (!_disposed) _watchTimer.Change(2000, Timeout.Infinite); };
                watcher.EnableRaisingEvents = true; _watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { LastError = e.Message; }
        }
    }

    private async Task RefreshWatchedAsync()
    {
        try
        {
            if (_disposed) return;
            foreach (string root in await FoldersAsync())
                if (Directory.Exists(root)) await ScanFolderAsync(root, false, token: _lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; }
    }

    private void MarkDirty()
    { _dirty = true; if (!_disposed) _saveTimer.Change(3000, Timeout.Infinite); }

    private async Task SaveSafeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_dirty) return;
            await WriteStoreLockedAsync(); _dirty = false; LastError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; }
        finally { _gate.Release(); }
    }

    public async Task ExportBackupAsync(string destination)
    {
        await _load;
        await SaveSafeAsync();
        if (LastError != null) throw new IOException(LastError);
        if (!File.Exists(_path))
        {
            await _gate.WaitAsync(); try { _dirty = true; } finally { _gate.Release(); }
            await SaveSafeAsync();
        }
        File.Copy(_path, destination, true);
    }

    public sealed record ImportResult(int AddedFiles, int MergedFiles, int SmartLists, int WatchedFolders);

    /// <summary>Merges a validated backup without replacing current favorites or reducing playback counts.</summary>
    public Task<ImportResult> ImportBackupAsync(string source, bool restoreWatchedFolders = false,
        CancellationToken token = default) => Task.Run(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _load.ConfigureAwait(false);
        Store store;
        await using (var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (file.Length > 256L * 1024 * 1024) throw new InvalidDataException("The backup exceeds 256 MiB.");
            try
            {
                store = await JsonSerializer.DeserializeAsync<Store>(file,
                    new JsonSerializerOptions { MaxDepth = 16 }, linked.Token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The backup is empty.");
            }
            catch (JsonException e) { throw new InvalidDataException("The backup is not valid library JSON.", e); }
        }
        if (store.Version != 1 || store.Entries == null || store.Folders == null || store.Entries.Length > 200_000)
            throw new InvalidDataException("The backup version or entry count is not supported.");

        // Validate every record before mutating the current index. A bad record cannot cause a partial merge.
        var imported = new Dictionary<string, LibraryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in store.Entries)
        {
            linked.Token.ThrowIfCancellationRequested();
            if (entry == null || string.IsNullOrWhiteSpace(entry.Path) || entry.Path.Length > 32767
                || (!MediaFormats.IsNetwork(entry.Path) && !System.IO.Path.IsPathFullyQualified(entry.Path))
                || entry.DurationMs < 0 || entry.Size < 0 || entry.PlayCount < 0 || entry.Year is < 0 or > 9999 || entry.Rating is < 0 or > 5
                || new[] { entry.Title, entry.Artist, entry.Album, entry.Genre }.Any(s => s == null || s.Length > 4096))
                throw new InvalidDataException("The backup contains an invalid media entry.");
            string path;
            try { path = MediaFormats.IsNetwork(entry.Path) ? entry.Path : System.IO.Path.GetFullPath(entry.Path); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException)
            { throw new InvalidDataException("The backup contains an invalid media path.", e); }
            System.Collections.Immutable.ImmutableArray<string> labels;
            try { labels = LibraryLabels.Canonicalize(entry.Labels.IsDefault ? Array.Empty<string>() : entry.Labels); }
            catch (ArgumentException e) { throw new InvalidDataException("The backup contains invalid personal labels.", e); }
            if (!imported.TryAdd(path, entry with { Path = path, Kind = NormalizeKind(entry.Kind), Labels = labels }))
                throw new InvalidDataException("The backup contains duplicate media paths.");
        }
        var lists = store.Lists ?? Array.Empty<SmartList>();
        ValidateConsolidated(store.Consolidated ?? []);
        if (lists.Length > 5000 || lists.Any(l => l?.IsValid != true))
            throw new InvalidDataException("The backup contains an invalid smart playlist.");
        var collections = store.Collections ?? Array.Empty<LibraryCollection>();
        try { LibraryCollections.Validate(collections); }
        catch (ArgumentException e) { throw new InvalidDataException("The backup contains invalid collection trees.", e); }
        var collectionIds = collections.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (collections.Any(node => node.Paths.Any(path => !imported.ContainsKey(path))) || lists.Any(list => list.CollectionId != null && !collectionIds.Contains(list.CollectionId)))
            throw new InvalidDataException("The backup contains missing collection/media references.");
        if (store.Folders.Length > 64 || store.Folders.Any(f => string.IsNullOrWhiteSpace(f)
            || f.Length > 32767 || !System.IO.Path.IsPathFullyQualified(f)))
            throw new InvalidDataException("The backup contains invalid watched folders.");
        var folders = restoreWatchedFolders ? store.Folders.Select(System.IO.Path.GetFullPath)
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();

        int added = 0, merged = 0, addedLists = 0;
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            (LibraryCollection[] Nodes, Dictionary<string, string> Ids) collectionMerge;
            try { collectionMerge = MergeCollections(collections); }
            catch (ArgumentException e) { throw new InvalidDataException("The merged collection tree exceeds its limits.", e); }
            if (_entries.Count + imported.Keys.Count(path => !_entries.ContainsKey(path)) > 200_000
                || _folders.Union(folders, StringComparer.OrdinalIgnoreCase).Count() > 64
                || _lists.Keys.Union(lists.Select(l => l.Name), StringComparer.OrdinalIgnoreCase).Count() > 5000)
                throw new InvalidDataException("The merged library would exceed its entry or watched-folder limit.");
            // Preflight label unions before publishing any imported record.
            var mergedLabels = new Dictionary<string, System.Collections.Immutable.ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in imported)
                if (_entries.TryGetValue(pair.Key, out var existing))
                    try { mergedLabels[pair.Key] = LibraryLabels.Canonicalize(existing.Labels.Concat(pair.Value.Labels)); }
                    catch (ArgumentException e) { throw new InvalidDataException("Merged personal labels would exceed the per-file limit.", e); }
            foreach (var pair in imported)
            {
                if (_entries.TryGetValue(pair.Key, out var current))
                {
                    var metadata = current.MetadataRead ? current : pair.Value;
                    _entries[pair.Key] = metadata with
                    {
                        Favorite = current.Favorite || pair.Value.Favorite,
                        Rating = current.Rating > 0 ? current.Rating : pair.Value.Rating,
                        Labels = mergedLabels[pair.Key],
                        PlayCount = Math.Max(current.PlayCount, pair.Value.PlayCount),
                        AddedUtc = current.AddedUtc < pair.Value.AddedUtc ? current.AddedUtc : pair.Value.AddedUtc,
                        LastPlayedUtc = Nullable.Compare(current.LastPlayedUtc, pair.Value.LastPlayedUtc) >= 0
                            ? current.LastPlayedUtc : pair.Value.LastPlayedUtc
                    };
                    merged++;
                }
                else { _entries.Add(pair.Key, pair.Value); added++; }
            }
            _collections.Clear(); foreach (var node in collectionMerge.Nodes) _collections.Add(node.Id, node);
            foreach (var list in lists)
            {
                var restoredList = list.CollectionId == null ? list : list with { CollectionId = collectionMerge.Ids[list.CollectionId] };
                if (_lists.TryAdd(restoredList.Name, restoredList)) addedLists++;
            }
            foreach (string folder in folders) _folders.Add(folder);
            foreach (var item in store.Consolidated ?? []) if (_entries.ContainsKey(item.Keeper) && _consolidated.Count < 200000) _consolidated.TryAdd(item.File.Path, item);
            foreach (var item in _consolidated.Values.Where(c => _entries.ContainsKey(c.Keeper)).ToArray())
            {
                _entries.Remove(item.File.Path);
                foreach (var node in _collections.Values.Where(n => n.Paths.Contains(item.File.Path, StringComparer.OrdinalIgnoreCase)).ToArray())
                    _collections[node.Id] = node with { Paths = node.Paths.Select(p => p.Equals(item.File.Path, StringComparison.OrdinalIgnoreCase) ? item.Keeper : p).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray() };
            }
            if (folders.Length > 0) ConfigureWatchers();
            MarkDirty();
        }
        finally { _gate.Release(); }
        await SaveSafeAsync().ConfigureAwait(false);
        if (LastError != null) throw new IOException(LastError);
        Changed?.Invoke(this, EventArgs.Empty);
        return new ImportResult(added, merged, addedLists, folders.Length);
    }, token);

    /// <summary>Removes index records only. Media files on disk are never modified.</summary>
    public async Task<int> RemoveEntriesAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await _load.ConfigureAwait(false);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        int removed = 0;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (string path in selected) if (_entries.Remove(path)) removed++;
            if (removed > 0)
                foreach (var node in _collections.Values.ToArray()) _collections[node.Id] = node with { Paths = node.Paths.Where(_entries.ContainsKey).ToImmutableArray() };
            if (removed > 0) MarkDirty();
        }
        finally { _gate.Release(); }
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        await _load;
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        await _saveTimer.DisposeAsync(); await _watchTimer.DisposeAsync();
        await _scanGate.WaitAsync(); _scanGate.Release();
        await SaveSafeAsync();
        _lifetime.Dispose();
    }
}
