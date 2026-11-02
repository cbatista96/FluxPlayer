using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json.Nodes;

internal static class CollectionChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action, string message)
    { bool refused = false; try { await action(); } catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException) { refused = true; } Check(refused, message); }
    public static async Task Run(string root, string wav)
    {
        string folder = Path.Combine(root, "collections-" + Guid.NewGuid().ToString("N")), media = Path.Combine(folder, "media"), profile = Path.Combine(folder, "profile");
        Directory.CreateDirectory(media); string first = Path.Combine(media, "A ñ.wav"), second = Path.Combine(media, "B.wav"); File.Copy(wav, first); File.Copy(wav, second);
        byte[] original = File.ReadAllBytes(first);
        await using var library = new LibraryService(profile); await library.ScanFolderAsync(media, false);
        var music = await library.SaveCollectionAsync("Music ñ"); var jazz = await library.SaveCollectionAsync("Jazz", music.Id); var classical = await library.SaveCollectionAsync("Classical", music.Id);
        var separate = await library.SaveCollectionAsync("Separate"); await library.SaveCollectionAsync("Jazz", separate.Id);
        await Reject(async () => { await library.SaveCollectionAsync("jazz", music.Id); }, "Duplicate sibling names rejected case-insensitively");
        await Reject(async () => { await library.SaveCollectionAsync("Cycle", jazz.Id, music.Id); }, "Moving a root under its child cannot create a cycle");
        await Reject(async () => { await library.SaveCollectionAsync("Missing parent", Guid.NewGuid().ToString("N")); }, "Dangling parents rejected");
        await Task.WhenAll(library.UpdateCollectionMembersAsync(jazz.Id, new[] { first, first }, true), library.UpdateCollectionMembersAsync(classical.Id, new[] { second }, true));
        var nodes = await library.CollectionsAsync();
        Check(LibraryCollections.Members(nodes, music.Id, true).SetEquals(new[] { first, second }) && LibraryCollections.Members(nodes, music.Id, false).Count == 0, "Child union and direct-only browsing");
        Check(await library.UpdateCollectionMembersAsync(music.Id, new[] { first, Path.Combine(media, "missing.wav") }, true) == 1, "Only indexed paths become members");
        Check(LibraryCollections.Members(await library.CollectionsAsync(), music.Id, true).Count == 2, "Duplicate membership across parent and child counted once");
        jazz = await library.SaveCollectionAsync("Jazz renamed", music.Id, jazz.Id);
        await library.SaveSmartListAsync(new SmartList("Jazz view", "format:wav", 1, 0, CollectionId: jazz.Id, IncludeChildCollections: false));
        string backup = Path.Combine(folder, "backup.json"); await library.ExportBackupAsync(backup);
        await using (var reopened = new LibraryService(profile))
        {
            Check((await reopened.CollectionsAsync()).Single(node => node.Id == jazz.Id).Paths.Single() == first, "Nested names and membership survive reopening");
            Check((await reopened.SmartListsAsync()).Single().CollectionId == jazz.Id && !(await reopened.SmartListsAsync()).Single().IncludeChildCollections, "Scoped smart playlist persists");
        }
        using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); bool refused = false; try { await library.SaveCollectionAsync("Canceled", token: canceled.Token); } catch (OperationCanceledException) { refused = true; } Check(refused && (await library.CollectionsAsync()).All(node => node.Name != "Canceled"), "Canceled collection edit leaves no record"); }
        using (var locked = new FileStream(Path.Combine(profile, "library.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Reject(async () => { await library.SaveCollectionAsync("Blocked"); }, "Blocked publication reported");
        Check((await library.CollectionsAsync()).All(node => node.Name != "Blocked") && !Directory.EnumerateFiles(profile, "*.tmp").Any(), "Failed save rolls back memory and removes temporary output");
        await using (var target = new LibraryService(Path.Combine(folder, "target")))
        {
            var targetRoot = await target.SaveCollectionAsync("Music ñ"); var targetChild = await target.SaveCollectionAsync("Jazz renamed", targetRoot.Id);
            await target.ImportBackupAsync(backup);
            var restored = await target.CollectionsAsync();
            Check(restored.Count(node => node.ParentId == null && node.Name == "Music ñ") == 1 && restored.Single(node => node.Id == targetChild.Id).Paths.Contains(first), "Backup merges matching sibling trees without duplicating names");
            Check((await target.SmartListsAsync()).Single().CollectionId == targetChild.Id, "Imported smart playlist remaps merged collection identity");
            var before = await target.SnapshotAsync(); var malformed = JsonNode.Parse(File.ReadAllText(backup))!.AsObject();
            var badNodes = malformed["Collections"]!.AsArray(); badNodes[0]!["ParentId"] = badNodes[0]!["Id"]!.GetValue<string>();
            string invalid = Path.Combine(folder, "invalid.json"); File.WriteAllText(invalid, malformed.ToJsonString());
            await Reject(async () => { await target.ImportBackupAsync(invalid); }, "Cyclic backup rejected before index mutation");
            Check((await target.SnapshotAsync()).SequenceEqual(before), "Rejected collection backup preserves index");
        }
        var rename = await FileRenameService.PreviewAsync(new[] { new RenameInput(first) }, "Renamed {filename}");
        Check((await FileRenameService.ApplyAsync(rename, library, _ => false)).Results.Single().Renamed, "Collection member rename succeeds");
        string moved = rename[0].Destination; nodes = await library.CollectionsAsync();
        Check(nodes.Single(node => node.Id == jazz.Id).Paths.Contains(moved) && !nodes.Any(node => node.Paths.Contains(first)), "File relocation updates every collection membership");
        Check(File.ReadAllBytes(moved).SequenceEqual(original), "Collection operations and rename preserve media bytes");
        await library.RemoveEntriesAsync(new[] { second }); Check(!(await library.CollectionsAsync()).Any(node => node.Paths.Contains(second)), "Removing an indexed entry prunes membership references");
        await Reject(async () => { await library.RemoveCollectionAsync(music.Id); }, "Scoped smart playlists protect referenced collection trees");
        await library.RemoveSmartListAsync("Jazz view");
        Check(await library.RemoveCollectionAsync(music.Id) == 3 && File.Exists(moved) && (await library.SnapshotAsync()).Length == 1, "Removing a subtree retains media and indexed files");
        var deep = new List<LibraryCollection>();
        for (int index = 0; index < 33; index++) deep.Add(new(Guid.NewGuid().ToString("N"), "Level " + index, index == 0 ? null : deep[^1].Id, ImmutableArray<string>.Empty));
        bool depthRefused = false; try { LibraryCollections.Validate(deep); } catch (ArgumentException) { depthRefused = true; }
        Check(depthRefused, "Depth limit bounded without recursion overflow");
        var tooMany = Enumerable.Range(0, LibraryCollections.MaximumCollections + 1).Select(index => new LibraryCollection(Guid.NewGuid().ToString("N"), "Group " + index, null, ImmutableArray<string>.Empty)).ToArray();
        bool limitRefused = false; try { LibraryCollections.Validate(tooMany); } catch (ArgumentException) { limitRefused = true; }
        Check(limitRefused, "Collection count limit enforced");
        var excessive = new LibraryCollection(Guid.NewGuid().ToString("N"), "Excess", null, Enumerable.Range(0, LibraryCollections.MaximumMembers + 1).Select(index => "Indexed path " + index).ToImmutableArray());
        limitRefused = false; try { LibraryCollections.Validate(new[] { excessive }); } catch (ArgumentException) { limitRefused = true; }
        Check(limitRefused, "Global membership count bounded");
    }
}
