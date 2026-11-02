using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class MusicMetadataWindow : RemoteSourceWorkbench
{
    public MusicMetadataWindow(Window owner, MainViewModel vm) : base(owner, vm, "MusicBrainz metadata and artwork", "Search public MusicBrainz data, browse releases/credits and save Cover Art Archive copies. Queries are sent only by explicit actions. Metadata writes require a local-file review.")
    {
        var query = Text("MusicBrainz search query", "", 500); var kind = Choice("Search entity", ["recording", "release", "artist"]);
        var fields = Section("Fields proposed for local tag review", () => (Title: Toggle("Title", true), Artist: Toggle("Artist", true), Album: Toggle("Album", true), Track: Toggle("Track number"), Year: Toggle("Release year")));
        CinemaUi.Compact(Form, 2); var table = Table(("Type", "Kind", 1), ("Title / relation", "Title", 3), ("Artist", "Artist", 2), ("Album", "Album", 2), ("Date", "Date", 1), ("Details", "Details", 3));
        var source = new MusicBrainzSource();
        MusicBrainzRow Selected(params string[] allowed) { var row = table.SelectedItem as MusicBrainzRow ?? throw new ArgumentException("Select a result."); if (allowed.Length > 0 && !allowed.Contains(row.Kind)) throw new ArgumentException("Select a " + string.Join(" or ", allowed) + " result."); return row; }
        Action("Search", async () => { table.ItemsSource = await source.Search((string)kind.SelectedItem, query.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Search loaded (up to 50 results). Identity matching is manual, not acoustic fingerprint recognition."; });
        Action("Artist discography", async () => { table.ItemsSource = await source.Discography(Selected("artist").Id, OperationToken); if (!IsClosed) Status.Text = "First 100 releases loaded; edition and country duplicates are retained."; });
        Action("Release track list", async () => { table.ItemsSource = await source.Tracks(Selected("release").Id, OperationToken); if (!IsClosed) Status.Text = "Release media and tracks loaded; disc positions remain visible in Details."; });
        Action("Recording credits", async () => { table.ItemsSource = await source.Relations("recording", Selected("recording", "track").Id, OperationToken); if (!IsClosed) Status.Text = "Recording relationships loaded."; });
        Action("Artist relationships", async () => { table.ItemsSource = await source.Relations("artist", Selected("artist").Id, OperationToken); if (!IsClosed) Status.Text = "Artist/work/URL relationships loaded; external links are shown as text."; });
        async Task Cover(bool back)
        {
            var release = Selected("release"); var cover = await source.Cover(release.Id, back, OperationToken); if (IsClosed) return;
            string? target = SavePath(this, RemoteMediaIO.SafeName(release.Title) + (back ? " back" : " front") + cover.Extension, cover.Extension);
            if (target != null) { await OfflineMediaJob.Publish(target, cover.Extension, [], staging => File.WriteAllBytesAsync(staging, cover.Bytes, OperationToken), OperationToken); if (!IsClosed) Status.Text = "Saved a new cover image. Existing artwork and embedded tags were preserved."; }
        }
        Action("Save front cover…", () => Cover(false)); Action("Save back cover…", () => Cover(true));
        Action("Review local tag changes…", () =>
        {
            var row = Selected("recording", "track"); var patch = MusicBrainzSource.Patch(row, fields.Title.IsChecked == true, fields.Artist.IsChecked == true, fields.Album.IsChecked == true, fields.Track.IsChecked == true, fields.Year.IsChecked == true);
            string? path = OpenPath(this, MediaFormats.FileDialogFilter); if (path != null) new MusicTagReviewWindow(this, vm, path, patch).ShowDialog(); return Task.CompletedTask;
        });
        CinemaUi.GroupActions(Actions, ("Browse results", ["Artist discography", "Release track list", "Recording credits", "Artist relationships"]), ("Artwork", ["Save front cover…", "Save back cover…"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += (_, _) => source.Dispose();
    }
}

public sealed record MusicTagChange(string Field, string Current, string Proposed);
public sealed class MusicTagReviewWindow : FeatureWorkbench
{
    public MusicTagReviewWindow(Window owner, MainViewModel vm, string path, MetadataPatch patch) : base(owner, vm, "Review MusicBrainz tag changes", "Read the chosen local file, inspect current and proposed values, then explicitly apply. Selected tag writes can partially succeed on unsupported formats; review each reported result.")
    {
        var filename = Text("Local target", path, 32767); filename.IsReadOnly = true; var table = Table(("Field", "Field", 1), ("Current", "Current", 3), ("Proposed", "Proposed", 3)); SourceVersion? version = null;
        Action("Read and review changes", async () =>
        {
            version = null;
            var reviewed = await Task.Run(() =>
            {
                var stamp = SourceVersion.Capture(path); using var file = TagLib.File.Create(stamp.Path); var tag = file.Tag; var rows = new List<MusicTagChange>();
                if (patch.Title != null) rows.Add(new("Title", tag.Title ?? "", patch.Title)); if (patch.Artist != null) rows.Add(new("Artist", string.Join("; ", tag.Performers), patch.Artist)); if (patch.Album != null) rows.Add(new("Album", tag.Album ?? "", patch.Album));
                foreach (var pair in patch.Extra ?? System.Collections.Immutable.ImmutableDictionary<string, string>.Empty) rows.Add(new(AdvancedTagFields.Get(pair.Key).Label, AdvancedTagFields.Get(pair.Key).Read(tag), pair.Value)); stamp.Verify(); return (Stamp: stamp, Rows: rows);
            }, OperationToken);
            if (!IsClosed) { version = reviewed.Stamp; table.ItemsSource = reviewed.Rows; Status.Text = "Review loaded. Apply writes only these fields to this one file."; }
        });
        Action("Apply reviewed changes", async () =>
        {
            var stamp = version ?? throw new ArgumentException("Read and review the proposed changes first."); stamp.Verify(); version = null;
            var result = await BatchTagService.ApplyAsync([stamp.Path], patch, vm.Engine, vm.Library, vm.CurrentItem?.FilePath, token: OperationToken);
            if (!IsClosed) Status.Text = string.Join("\n", result.Results.Select(r => r.Outcome + ": " + r.Message));
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
    }
}
