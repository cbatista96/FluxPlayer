using FluxPlayer.Models;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

/// <summary>Lazy local index. Scans names first; expensive decoding is always an explicit operation.</summary>
public sealed class LibraryService : IAsyncDisposable
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
    public LibraryService(string? dataDirectory = null)
    {
        _path = System.IO.Path.Combine(dataDirectory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluxPlayer"), "library.json");
        _saveTimer = new Timer(_ => _ = SaveSafeAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watchTimer = new Timer(_ => _ = RefreshWatchedAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _load = Task.Run(LoadAsync);
    }

    private sealed record Store(int Version, string[] Folders, LibraryEntry[] Entries, SmartList[]? Lists = null);

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return;
            await using var file = File.OpenRead(_path);
            var store = await JsonSerializer.DeserializeAsync<Store>(file);
            if (store == null || store.Version != 1) return;
            foreach (var entry in store.Entries.Take(200_000)) if (entry.Path.Length > 0) _entries[entry.Path] = entry;
            foreach (var folder in store.Folders) if (Directory.Exists(folder)) _folders.Add(folder);
            foreach (var list in store.Lists ?? Array.Empty<SmartList>()) _lists[list.Name] = list;
            ConfigureWatchers();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { LastError = $"Unable to read the library: {e.Message}"; }
    }

    public async Task<LibraryEntry[]> SnapshotAsync()
    {
        await _load; await _gate.WaitAsync();
        try { return _entries.Values.ToArray(); } finally { _gate.Release(); }
    }

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
        if (string.IsNullOrWhiteSpace(list.Name) || list.Name.Length > 120) throw new ArgumentException("The name must contain between 1 and 120 characters.");
        await _load; await _gate.WaitAsync();
        try { _lists[list.Name.Trim()] = list with { Name = list.Name.Trim() }; MarkDirty(); } finally { _gate.Release(); }
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

    public async Task UpdateMetadataAsync(string path, MediaMetadata metadata, bool countPlayback = false)
    {
        await _load; await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _entries.TryGetValue(path, out var old);
            old ??= new LibraryEntry { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path) };
            _entries[path] = old with
            {
                Title = string.IsNullOrWhiteSpace(metadata.Title) ? old.Title : metadata.Title,
                Artist = metadata.Artist,
                Album = metadata.Album,
                Genre = metadata.Genre,
                DurationMs = Math.Max(metadata.DurationMs, old.DurationMs),
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
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            await using (var file = File.Create(temporary))
                await JsonSerializer.SerializeAsync(file, new Store(1, _folders.ToArray(), _entries.Values.ToArray(), _lists.Values.ToArray()));
            File.Move(temporary, _path, true); _dirty = false; LastError = null;
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
