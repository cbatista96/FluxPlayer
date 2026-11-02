using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;

namespace FluxPlayer.Windows;

public sealed class DuplicateMergeWindow : FeatureWorkbench
{
    private sealed record Change(string Field, string Before, string After);
    public DuplicateMergeWindow(Window owner, MainViewModel vm, string keeper, string[] paths) : base(owner, vm, "Consolidate exact duplicates", "The selected row is retained. Other index entries merge into it; collection memberships follow it. Files, embedded tags, playback history and external playlists stay intact. Unchanged copies remain excluded from scans; use Restore consolidated paths to include them again.")
    {
        DuplicateMergePlan? plan = null;
        var table = Table(("Field", "Field", 1), ("Retained entry", "Before", 2), ("Merged value", "After", 2));
        Field("Retained file", new System.Windows.Controls.TextBox { Text = keeper, IsReadOnly = true });
        Field("Files in this consolidation", new System.Windows.Controls.TextBox { Text = string.Join(Environment.NewLine, paths), IsReadOnly = true, AcceptsReturn = true, MaxHeight = 100, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto });
        Action("Preview metadata merge", async () =>
        {
            plan = null; table.ItemsSource = null;
            var next = await vm.Library.PreviewDuplicateMerge(keeper, paths, OperationToken);
            var old = next.Before.Single(e => e.Path.Equals(keeper, StringComparison.OrdinalIgnoreCase)); var merged = next.Merged;
            table.ItemsSource = new[] { new Change("Title", old.Title, merged.Title), new Change("Artist", old.Artist, merged.Artist), new Change("Album", old.Album, merged.Album), new Change("Genre", old.Genre, merged.Genre), new Change("Year", old.Year.ToString(), merged.Year.ToString()), new Change("Track", old.TrackNumber.ToString(), merged.TrackNumber.ToString()), new Change("Favorite", old.Favorite.ToString(), merged.Favorite.ToString()), new Change("Rating", old.Rating.ToString(), merged.Rating.ToString()), new Change("Labels", old.LabelText, merged.LabelText), new Change("Play count", old.PlayCount.ToString(), merged.PlayCount.ToString()), new Change("Added UTC", old.AddedUtc.ToString("O"), merged.AddedUtc.ToString("O")), new Change("Last played UTC", old.LastPlayedUtc?.ToString("O") ?? "", merged.LastPlayedUtc?.ToString("O") ?? ""), new Change("Duration (ms)", old.DurationMs.ToString(), merged.DurationMs.ToString()) };
            plan = next; Status.Text = $"Verified {next.Before.Length} identical files. Existing nonempty metadata and manual rating on the retained entry win. Labels and favorites combine; play counts add. Review, then apply.";
        });
        Action("Apply reviewed merge", async () =>
        {
            if (plan == null) throw new InvalidOperationException("Create a preview first.");
            var reviewed = plan; plan = null;
            int removed = await vm.Library.ApplyDuplicateMerge(reviewed, OperationToken);
            Status.Text = $"Consolidated {removed} duplicate index entries. Media files remain in their original locations.";
        });
        CancelAction();
    }
}
