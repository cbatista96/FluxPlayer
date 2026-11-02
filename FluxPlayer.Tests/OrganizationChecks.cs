using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.Immutable;
using System.IO;

internal static class OrganizationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ImmediateReport(Action<RenameResult> action) : IProgress<RenameResult> { public void Report(RenameResult result) => action(result); }
    public static async Task Run(string root, string wav)
    {
        string folder = Path.Combine(root, "organization-" + Guid.NewGuid().ToString("N")), source = Path.Combine(folder, "source"), destination = Path.Combine(folder, "destination");
        Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
        string first = Path.Combine(source, "First ñ.wav"), second = Path.Combine(source, "Second.wav"); File.Copy(wav, first); File.Copy(wav, second);
        var inputs = new[] { new RenameInput(first, "First title", "Artist ñ", "Album"), new RenameInput(second, "Second title", "Artist ñ", "Album") };
        await using var library = new LibraryService(Path.Combine(folder, "library"));
        await library.ScanFolderAsync(source, false); await library.ToggleFavoriteAsync(first);
        await library.UpdateLabelsAsync(new[] { first }, new[] { "Keep" }, LibraryLabelEdit.Add);
        await library.RecordPlaybackAsync(first, new MediaMetadata("First title", "Artist ñ", "Album", "Jazz", "", 8000, true, false, "PCM"));
        byte[] bytes = File.ReadAllBytes(first);
        var plan = await FileOrganizationService.PreviewAsync(inputs, destination, "{artist}/{album}", "{index} - {title}");
        Check(plan.Entries[0].Destination == Path.Combine(destination, "Artist ñ", "Album", "001 - First title.wav"), "Metadata hierarchy, Unicode and filename template");
        Check(!Directory.Exists(Path.Combine(destination, "Artist ñ")) && File.Exists(first), "Preview never creates folders or moves files");
        foreach (string pattern in new[] { "../escape", "{artist}//{album}", "C:/escape", "/escape", "{artist}/{unknown}" })
        {
            bool rejected = false; try { await FileOrganizationService.PreviewAsync(inputs, destination, pattern); } catch (ArgumentException) { rejected = true; }
            Check(rejected && File.Exists(first), "Reject unsafe folder pattern: " + pattern);
        }
        bool refused = false;
        try { await FileOrganizationService.PreviewAsync(inputs, destination, "All", "Fixed"); } catch (ArgumentException) { refused = true; }
        Check(refused, "Duplicate destinations rejected before mutation");
        var outside = plan with { Entries = ImmutableArray.Create(plan.Entries[0] with { Destination = Path.Combine(folder, "outside.wav") }) };
        refused = false; try { await FileOrganizationService.ApplyAsync(outside, library, _ => false); } catch (ArgumentException) { refused = true; }
        Check(refused && File.Exists(first), "Modified plan cannot escape root");
        string otherDrive = string.Equals(Path.GetPathRoot(first), "C:\\", StringComparison.OrdinalIgnoreCase) ? "D:\\" : "C:\\";
        var crossDrive = plan with { Entries = ImmutableArray.Create(plan.Entries[0] with { Source = Path.Combine(otherDrive, "Synthetic unavailable source.wav") }) };
        refused = false; try { await FileOrganizationService.ApplyAsync(crossDrive, library, _ => false); } catch (ArgumentException) { refused = true; }
        Check(refused && File.Exists(first), "Cross-drive plans rejected before accessing or moving sources");
        var stale = plan;
        File.SetLastWriteTimeUtc(second, File.GetLastWriteTimeUtc(second).AddSeconds(2));
        refused = false; try { await FileOrganizationService.ApplyAsync(stale, library, _ => false); } catch (IOException) { refused = true; }
        Check(refused && File.Exists(first) && !Directory.Exists(Path.Combine(destination, "Artist ñ")), "Stale later source blocks all moves and directory creation");
        plan = await FileOrganizationService.PreviewAsync(inputs, destination, "{artist}/{album}", "{index} - {title}");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Entries[1].Destination)!); File.WriteAllText(plan.Entries[1].Destination, "Do not replace");
        refused = false; try { await FileOrganizationService.ApplyAsync(plan, library, _ => false); } catch (IOException) { refused = true; }
        Check(refused && File.Exists(first) && File.ReadAllText(plan.Entries[1].Destination) == "Do not replace", "Later destination collision blocks all moves");
        string cleanDestination = Path.Combine(folder, "clean"); Directory.CreateDirectory(cleanDestination);
        plan = await FileOrganizationService.PreviewAsync(inputs, cleanDestination, "{artist}/{album}");
        var result = await FileOrganizationService.ApplyAsync(plan, library, path => path == first);
        Check(result.Results.Count(item => item.Renamed) == 1 && File.Exists(first) && File.Exists(plan.Entries[1].Destination), "Loaded source skipped while other files move");
        var firstPlan = await FileOrganizationService.PreviewAsync(new[] { inputs[0] }, cleanDestination, "{albumartist}/{year}/{genre}/{disc}", "{track} - {filename}");
        Check(firstPlan.Entries[0].Destination.Contains(Path.Combine("Artist ñ", "Unknown year", "Unknown genre", "00", "00 - First ñ.wav")), "Expanded token fallbacks and independent filename base");
        result = await FileOrganizationService.ApplyAsync(firstPlan, library, _ => false);
        string moved = firstPlan.Entries.Single().Destination;
        Check(result.Results.Single().Renamed && !File.Exists(first) && File.ReadAllBytes(moved).SequenceEqual(bytes), "Source moved with identical media bytes");
        var entry = (await library.SnapshotAsync()).Single(item => item.Path == moved);
        Check(entry.Favorite && entry.PlayCount == 1 && entry.Labels.Contains("Keep") && entry.Title == "First title", "Index record retains favorites, labels, metadata and counts");
        await using (var reopened = new LibraryService(Path.Combine(folder, "library"))) Check((await reopened.SnapshotAsync()).Any(item => item.Path == moved && item.Favorite), "Moved index paths persist");
        var cancelSources = new[] { Path.Combine(source, "Cancel A.wav"), Path.Combine(source, "Cancel B.wav") };
        foreach (string path in cancelSources) File.Copy(wav, path);
        var cancelPlan = await FileOrganizationService.PreviewAsync(cancelSources.Select(path => new RenameInput(path)), cleanDestination, "Canceled");
        using var cancellation = new CancellationTokenSource();
        result = await FileOrganizationService.ApplyAsync(cancelPlan, library, _ => false, new ImmediateReport(_ => cancellation.Cancel()), cancellation.Token);
        Check(result.Canceled && result.Results.Length == 1 && File.Exists(cancelPlan.Entries[0].Destination) && File.Exists(cancelSources[1]), "Cancellation retains completed moves and untouched remaining files");
        var reserved = await FileOrganizationService.PreviewAsync(new[] { new RenameInput(cancelSources[1], Artist: "CON.foo", Album: "A/B") }, cleanDestination, "{artist}/{album}");
        Check(reserved.Entries[0].Destination.Contains(Path.Combine("_CON.foo", "A_B")), "Reserved and slash-containing metadata becomes safe folder names");
        var braces = await FileOrganizationService.PreviewAsync(new[] { new RenameInput(cancelSources[1], Artist: "Artist {Live}") }, cleanDestination, "{artist}");
        Check(Path.GetFileName(Path.GetDirectoryName(braces.Entries[0].Destination)) == "Artist {Live}", "Valid metadata braces are preserved rather than interpreted as template tokens");
        using var beforeStart = new CancellationTokenSource(); beforeStart.Cancel();
        refused = false; try { await FileOrganizationService.ApplyAsync(reserved, library, _ => false, token: beforeStart.Token); } catch (OperationCanceledException) { refused = true; }
        Check(refused && File.Exists(cancelSources[1]) && !Directory.Exists(Path.Combine(cleanDestination, "_CON.foo")), "Pre-canceled operation creates nothing");
        string callbackSource = Path.Combine(source, "Callback.wav"); File.Copy(wav, callbackSource); await library.ScanFolderAsync(source, false);
        var callbackPlan = await FileOrganizationService.PreviewAsync(new[] { new RenameInput(callbackSource) }, cleanDestination, "Callback");
        refused = false;
        try { await FileOrganizationService.ApplyAsync(callbackPlan, library, _ => false, new ImmediateReport(_ => throw new InvalidOperationException("Closed UI callback"))); }
        catch (InvalidOperationException) { refused = true; }
        await using var callbackReopened = new LibraryService(Path.Combine(folder, "library"));
        Check(refused && (await callbackReopened.SnapshotAsync()).Any(item => item.Path == callbackPlan.Entries[0].Destination), "Completed move index persists even if a progress subscriber fails");
    }
}
