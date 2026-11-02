using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;

namespace FluxPlayer.Services;

public sealed partial class LibraryService
{
    private readonly Dictionary<string, LibraryCollection> _collections = new(StringComparer.OrdinalIgnoreCase);

    public async Task<LibraryCollection[]> CollectionsAsync()
    {
        await _load.ConfigureAwait(false); await _gate.WaitAsync().ConfigureAwait(false);
        try { return _collections.Values.ToArray(); } finally { _gate.Release(); }
    }
    public Task<LibraryCollection> SaveCollectionAsync(string name, string? parentId = null, string? id = null, CancellationToken token = default) =>
        EditCollectionsAsync(nodes =>
        {
            name = name.Trim(); id ??= Guid.NewGuid().ToString("N");
            if (nodes.TryGetValue(id, out var old)) nodes[id] = old with { Name = name, ParentId = parentId };
            else nodes.Add(id, new(id, name, parentId, ImmutableArray<string>.Empty));
            return nodes[id];
        }, token);

    public Task<int> UpdateCollectionMembersAsync(string id, IEnumerable<string> paths, bool add, CancellationToken token = default)
    {
        var selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(10_001).ToArray();
        if (selected.Length > 10_000) throw new ArgumentException("Select up to 10,000 files per collection edit.");
        return EditCollectionsAsync(nodes =>
        {
            if (!nodes.TryGetValue(id, out var node)) throw new ArgumentException("The collection no longer exists.");
            var members = node.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase); int changed = 0;
            foreach (string path in selected)
                if (add ? _entries.ContainsKey(path) && members.Add(path) : members.Remove(path)) changed++;
            nodes[id] = node with { Paths = members.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToImmutableArray() }; return changed;
        }, token);
    }
    public Task<int> RemoveCollectionAsync(string id, CancellationToken token = default) => EditCollectionsAsync(nodes =>
    {
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id }; bool changed;
        do { changed = false; foreach (var node in nodes.Values) if (node.ParentId != null && removed.Contains(node.ParentId)) changed |= removed.Add(node.Id); } while (changed);
        if (_lists.Values.Any(list => list.CollectionId != null && removed.Contains(list.CollectionId)))
            throw new ArgumentException("Remove smart playlists scoped to this collection tree before removing the collections.");
        int count = 0; foreach (string key in removed) if (nodes.Remove(key)) count++; return count;
    }, token);

    private Task<T> EditCollectionsAsync<T>(Func<Dictionary<string, LibraryCollection>, T> edit, CancellationToken token) => Task.Run(async () =>
    {
        await _load.ConfigureAwait(false); await _gate.WaitAsync(token).ConfigureAwait(false);
        T result;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
            var draft = new Dictionary<string, LibraryCollection>(_collections, StringComparer.OrdinalIgnoreCase); result = edit(draft);
            LibraryCollections.Validate(draft.Values.ToArray()); token.ThrowIfCancellationRequested();
            var previous = _collections.Values.ToArray(); bool wasDirty = _dirty;
            _collections.Clear(); foreach (var node in draft.Values) _collections.Add(node.Id, node);
            try { await WriteStoreLockedAsync(token).ConfigureAwait(false); _dirty = false; LastError = null; }
            catch { _collections.Clear(); foreach (var node in previous) _collections.Add(node.Id, node); _dirty = wasDirty; throw; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; throw; }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty); return result;
    }, token);

    private (LibraryCollection[] Nodes, Dictionary<string, string> Ids) MergeCollections(LibraryCollection[] imported)
    {
        var merged = new Dictionary<string, LibraryCollection>(_collections, StringComparer.OrdinalIgnoreCase);
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byId = imported.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var choice in LibraryCollections.Choices(imported).Skip(1))
        {
            var node = byId[choice.Id!]; string? parent = node.ParentId == null ? null : mapping[node.ParentId];
            if (!merged.TryGetValue(node.Id, out var match)) match = merged.Values.FirstOrDefault(item => string.Equals(item.ParentId, parent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, node.Name, StringComparison.OrdinalIgnoreCase));
            if (match == null) { match = node with { ParentId = parent }; merged.Add(match.Id, match); }
            else merged[match.Id] = match with { Paths = match.Paths.Union(node.Paths, StringComparer.OrdinalIgnoreCase).ToImmutableArray() };
            mapping[node.Id] = match.Id;
        }
        var result = merged.Values.ToArray(); LibraryCollections.Validate(result); return (result, mapping);
    }
}
