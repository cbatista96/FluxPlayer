using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class LibraryAutomationWindow : FeatureWorkbench
{
    public LibraryAutomationWindow(Window owner, MainViewModel vm, int mode) : base(owner, vm, new[] { "Artist mix", "Album mix", "Computed ratings", "Repair moved library files" }[mode], mode < 2 ? "Preview an interleaved queue from available indexed local audio. Saved playlists and the library are preserved." : mode == 2 ? "Derive personal ratings from at least three retained listening sessions. Only unrated library files are changed; media tags and existing ratings are preserved." : "Capture SHA-256 fingerprints while sources exist, then locate uniquely matching moved files. Repair updates library paths and collections without moving media.")
    {
        if (mode < 2)
        {
            var count = Text("Maximum tracks (1–1000)", "100"); var table = Table(("Title", "Title", 2), ("Artist", "Artist", 1), ("Album", "Album", 1), ("Source", "Path", 2)); LibraryEntry[] preview = [];
            count.TextChanged += (_, _) => { preview = []; table.ItemsSource = null; };
            Action("Preview mix", async () => { if (!int.TryParse(count.Text, out int limit)) throw new ArgumentException("Enter a track count."); preview = await vm.PreviewMix(mode == 1, limit, OperationToken); table.ItemsSource = preview; Status.Text = $"{preview.Length} available tracks. Groups are interleaved, retaining each group's internal order."; });
            Action("Add mix to queue", () => { if (preview.Length == 0) throw new ArgumentException("Preview a mix first."); int added = vm.AddCatalogTracks(preview.Select(item => new SavedMediaTrack(item.Path, item.Title)), false); Status.Text = $"Added {added} tracks without starting playback."; return Task.CompletedTask; });
            Action("Play mix", () => { if (preview.Length == 0) throw new ArgumentException("Preview a mix first."); vm.AddCatalogTracks(preview.Select(item => new SavedMediaTrack(item.Path, item.Title)), true); return Task.CompletedTask; });
        }
        else if (mode == 2)
        {
            var table = Table(("Source", "Path", 3), ("Proposed stars", "Rating", 1)); Dictionary<string, int> preview = [];
            Action("Preview computed ratings", async () => { preview = await vm.PreviewComputedRatings(OperationToken); var entries = await vm.Library.SnapshotAsync(); var unrated = entries.Where(entry => entry.Rating == 0).Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase); preview = preview.Where(pair => unrated.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value); table.ItemsSource = preview.Select(pair => new RatingRow(pair.Key, pair.Value)).ToArray(); Status.Text = $"{preview.Count} unrated sources meet the retained-session threshold. History is opt-in and duration excludes paused time."; });
            Action("Apply reviewed ratings", async () => { if (preview.Count == 0) throw new ArgumentException("Preview eligible ratings first."); var selected = table.SelectedItems.Cast<RatingRow>().ToArray(); int count = await vm.Library.ApplyComputedRatings(selected.Length == 0 ? preview : selected.ToDictionary(row => row.Path, row => row.Rating), OperationToken); table.ItemsSource = null; preview.Clear(); Status.Text = $"Applied {count} personal ratings. Existing ratings were preserved."; });
        }
        else
        {
            var folder = Text("Folder to search for moved sources"); var table = Table(("Missing library source", "Missing", 2), ("Unique matching file", "Found", 2)); RelinkCandidate[] preview = []; var fingerprints = new LibraryFingerprints(vm.ProfileDirectory);
            Action("Capture fingerprints", async () => { var entries = await vm.Library.SnapshotAsync(); var progress = new Progress<string>(name => Status.Text = "Fingerprinting " + name); int count = await Task.Run(() => fingerprints.Capture(entries, progress, OperationToken)); Status.Text = $"Saved {count} fingerprints for existing files. Capture before files are moved externally."; });
            Action("Choose search folder…", () => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) { folder.Text = dialog.FolderName; preview = []; table.ItemsSource = null; } return Task.CompletedTask; });
            Action("Find unique matches", async () => { var entries = await vm.Library.SnapshotAsync(); string root = folder.Text; preview = await Task.Run(() => fingerprints.Find(entries, root, null, OperationToken)); table.ItemsSource = preview; Status.Text = $"{preview.Length} unique byte-identical matches. Ambiguous, changed and unfingerprinted files are omitted."; });
            Action("Apply reviewed repairs", async () => { var selected = table.SelectedItems.Cast<RelinkCandidate>().ToArray(); var plan = selected.Length > 0 ? selected : preview; if (plan.Any(item => vm.Engine.IsSourceInUse(item.Missing) || vm.Engine.IsSourceInUse(item.Found))) throw new IOException("Close playback of affected sources before repairing their paths."); await vm.Library.Relink(plan, OperationToken); foreach (var item in plan) vm.RelocateQueuedSource(item.Missing, item.Found); vm.SaveSettings(); preview = []; table.ItemsSource = null; Status.Text = $"Repaired {plan.Length} library paths and current queue references. Separate saved sessions/playlists are not rewritten."; });
            var cancel = CancelAction(); ((StackPanel)Actions.Parent).Children.Add(cancel);
        }
    }
    public sealed record RatingRow(string Path, int Rating);
}
