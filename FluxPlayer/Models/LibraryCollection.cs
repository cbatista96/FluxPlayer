using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record LibraryCollection(string Id, string Name, string? ParentId, ImmutableArray<string> Paths);
public sealed record LibraryCollectionChoice(string? Id, string Display);

public static class LibraryCollections
{
    public const int MaximumCollections = 1000, MaximumDepth = 32, MaximumMembers = 100_000;
    public static bool ValidId(string? id) => id != null && Guid.TryParseExact(id, "N", out _);
    public static void Validate(IReadOnlyCollection<LibraryCollection> collections)
    {
        if (collections.Count > MaximumCollections) throw new ArgumentException("Use up to 1,000 collections.");
        var nodes = new Dictionary<string, LibraryCollection>(StringComparer.OrdinalIgnoreCase);
        var siblings = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long members = 0;
        foreach (var node in collections)
        {
            if (node == null || !ValidId(node.Id) || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 120 || node.Name != node.Name.Trim()
                || node.Name.Any(char.IsControl) || node.ParentId != null && !ValidId(node.ParentId) || node.Paths.IsDefault
                || !nodes.TryAdd(node.Id, node) || !siblings.Add((node.ParentId ?? "") + "/" + node.Name))
                throw new ArgumentException("Collections need unique IDs, unique sibling names and 1–120 printable characters per name.");
            if (node.Paths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Contains('\0'))
                || node.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.Paths.Length || (members += node.Paths.Length) > MaximumMembers)
                throw new ArgumentException("Use unique member paths and up to 100,000 memberships across collections.");
        }
        foreach (var node in collections)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var current = node;
            for (int depth = 1; ; depth++)
            {
                if (depth > MaximumDepth || !visited.Add(current.Id)) throw new ArgumentException("Collection trees must be acyclic and at most 32 levels deep.");
                if (current.ParentId == null) break;
                if (!nodes.TryGetValue(current.ParentId, out current!)) throw new ArgumentException("A parent collection is missing.");
            }
        }
    }
    public static LibraryCollectionChoice[] Choices(IReadOnlyCollection<LibraryCollection> collections, string rootName = "All collections")
    {
        Validate(collections);
        var result = new List<LibraryCollectionChoice> { new(null, FluxPlayer.Services.Localizer.T(rootName)) };
        var children = collections.ToLookup(node => node.ParentId ?? "", StringComparer.OrdinalIgnoreCase);
        void Add(string parent, int depth)
        {
            foreach (var node in children[parent].OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
            { result.Add(new(node.Id, new string(' ', depth * 3) + node.Name + $" ({node.Paths.Length:N0})")); Add(node.Id, depth + 1); }
        }
        Add("", 0); return result.ToArray();
    }
    public static HashSet<string> Members(IReadOnlyCollection<LibraryCollection> collections, string id, bool includeChildren)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        if (includeChildren)
        {
            var children = collections.ToLookup(node => node.ParentId ?? "", StringComparer.OrdinalIgnoreCase); var queue = new Queue<string>(); queue.Enqueue(id);
            while (queue.TryDequeue(out var parent)) foreach (var child in children[parent]) if (selected.Add(child.Id)) queue.Enqueue(child.Id);
        }
        return collections.Where(node => selected.Contains(node.Id)).SelectMany(node => node.Paths).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
