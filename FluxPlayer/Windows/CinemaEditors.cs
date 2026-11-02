using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public class CinemaEditor : FeatureWorkbench
{
    public CinemaEditor(Window owner, MainViewModel vm, string title, string description) : base(owner, vm, title, description + " Local metadata only.") { UseFormLayout(740); }
    protected TextBox Memo(string label, string value, int max = 20000) => Field(label, new TextBox { Text = value, MaxLength = max, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100, MaxHeight = 220, Padding = new(10, 7, 10, 7) });
    protected TextBox FileField(string label, string value, bool image = false)
    {
        var field = Text(label, value, 32767);
        var button = new Button { Content = "Choose " + (image ? "image…" : "media…"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, -6, 0, 12), Padding = new(12, 7, 12, 7) };
        Form.Children.Add(button); button.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = image ? "Local artwork|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff" : MediaFormats.FileDialogFilter };
            if (picker.ShowDialog(this) == true) field.Text = picker.FileName;
        };
        return field;
    }
}

public sealed class CinemaItemEditor : CinemaEditor
{
    public CinemaItemEditor(Window owner, MainViewModel vm, CinemaCatalogData data, CinemaItem item) : base(owner, vm, item.Revision == 0 ? "Add video to catalog" : "Edit video details", "Posters, fanart, synopsis, release, classification, cast and production credits belong to this catalog record.")
    {
        var title = Text("Title", item.Title, 240); var path = FileField("Local source", item.Path);
        var kind = Choice("Entry type", ["Movie", "Episode"], item.Kind == "Episode" ? 1 : 0);
        var series = Field("Series (episodes only)", new ComboBox { ItemsSource = data.Series, SelectedItem = data.Series.FirstOrDefault(s => s.Id == item.SeriesId) });
        var season = Text("Season number (create the season in Series and seasons first)", item.Season.ToString(CultureInfo.InvariantCulture), 3);
        var episode = Text("Episode number", item.Episode.ToString(CultureInfo.InvariantCulture), 4);
        void KindChanged() => series.IsEnabled = season.IsEnabled = episode.IsEnabled = kind.SelectedIndex == 1;
        kind.SelectionChanged += (_, _) => KindChanged(); KindChanged();
        var synopsis = Memo("Synopsis", item.Synopsis);
        var release = Text("Release date (YYYY-MM-DD; empty if unknown)", item.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", 10);
        var classification = Text("Content classification (for example PG-13)", item.Classification, 80);
        var rating = Text("Personal rating (0–10; decimal point)", item.Rating.ToString(CultureInfo.InvariantCulture), 12);
        var state = Choice("Viewing status", CinemaCatalogService.WatchStates, Array.IndexOf(CinemaCatalogService.WatchStates, item.State));
        var poster = FileField("Poster image", item.Poster, true); var fanart = FileField("Fanart image", item.Fanart, true); var trailer = FileField("Optional local trailer", item.Trailer);
        var cast = Memo("Cast and production credits — one Person name | Job | Character or role per line. Create people profiles first.", CinemaText.Credits(item.Credits, data.People), 30000);
        var attributes = Memo("Multiple attribute values — one Field=Value | Other value per line", CinemaText.Attributes(item.Attributes), 30000);
        Action("Save catalog entry", async () =>
        {
            DateOnly? date = null;
            if (release.Text.Trim() != "") { if (!DateOnly.TryParseExact(release.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new ArgumentException("Use a release date in YYYY-MM-DD format."); date = parsed; }
            if (!double.TryParse(rating.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double score)) throw new ArgumentException("Use a rating from 0 to 10.");
            bool isEpisode = kind.SelectedIndex == 1;
            var saved = item with { Title = title.Text.Trim(), Path = path.Text.Trim(), Kind = isEpisode ? "Episode" : "Movie", SeriesId = isEpisode ? (series.SelectedItem as CinemaSeries)?.Id ?? "" : "", Season = isEpisode ? CinemaText.Integer(season.Text, 0, 999) : 1, Episode = isEpisode ? CinemaText.Integer(episode.Text, 1, 9999) : 1, Synopsis = synopsis.Text, ReleaseDate = date, Classification = classification.Text.Trim(), Rating = score, State = (string)state.SelectedItem, Poster = poster.Text.Trim(), Fanart = fanart.Text.Trim(), Trailer = trailer.Text.Trim(), Credits = CinemaText.Credits(cast.Text, data.People), Attributes = CinemaText.Attributes(attributes.Text) };
            if (saved.State != "In progress") saved = saved with { PositionMs = 0 };
            await vm.Cinema.SaveItem(saved, OperationToken); if (!IsClosed) { DialogResult = true; }
        });
        Status.Text = "Metadata is stored in the profile. Source files and their embedded tags are not modified.";
    }
}

public sealed class CinemaSeriesEditor : CinemaEditor
{
    public CinemaSeriesEditor(Window owner, MainViewModel vm, CinemaSeries? existing) : base(owner, vm, "Series details", "Create a series, its synopsis and its local banner. Add seasons after saving the series.")
    {
        var item = existing ?? new CinemaSeries(Guid.NewGuid().ToString("N"), 0, "", "", "");
        var name = Text("Series name", item.Name, 240); var synopsis = Memo("Series synopsis", item.Synopsis); var banner = FileField("Wide series banner", item.Banner, true);
        Action("Save series", async () => { await vm.Cinema.SaveSeries(item with { Name = name.Text.Trim(), Synopsis = synopsis.Text, Banner = banner.Text.Trim() }, OperationToken); if (!IsClosed) DialogResult = true; });
    }
}
public sealed class CinemaSeasonEditor : CinemaEditor
{
    public CinemaSeasonEditor(Window owner, MainViewModel vm, CinemaSeries show, CinemaSeason? existing) : base(owner, vm, "Season details", "Season cover and name for " + show.Name + ". Season 0 can hold specials.")
    {
        var number = Text("Season number", (existing?.Number ?? 1).ToString(CultureInfo.InvariantCulture), 3); number.IsReadOnly = existing != null;
        var name = Text("Season name", existing?.Name ?? "", 240); var cover = FileField("Season cover image", existing?.Cover ?? "", true);
        Action("Save season", async () => { await vm.Cinema.SaveSeason(new(show.Id, CinemaText.Integer(number.Text, 0, 999), name.Text.Trim(), cover.Text.Trim()), OperationToken); if (!IsClosed) DialogResult = true; });
    }
}
public sealed class CinemaPersonEditor : CinemaEditor
{
    public CinemaPersonEditor(Window owner, MainViewModel vm, CinemaCatalogData data, CinemaPerson? existing) : base(owner, vm, "People profile", "Maintain local artist biographies, composer information, actor profiles, relationships and discographies.")
    {
        var item = existing ?? new CinemaPerson(); var name = Text("Name (unique; do not use the | delimiter)", item.Name, 160);
        var kind = Choice("Profile type", ["Artist", "Composer", "Actor"], item.Kind == "Composer" ? 1 : item.Kind == "Actor" ? 2 : 0);
        var bio = Memo("Biography or composer/actor information", item.Biography);
        var relations = Memo("Relationships — one Existing person name | Relationship per line", CinemaText.Relations(item.Relations, data.People), 30000);
        var releases = Memo("Discography — one Year | Album title | Full track path per line. Repeat year/title for another track; path may be empty.", CinemaText.Releases(item.Releases), 200000);
        Action("Save profile", async () =>
        {
            if (name.Text.Contains('|')) throw new ArgumentException("Names cannot contain the editor delimiter |.");
            await vm.Cinema.SavePerson(item with { Name = name.Text.Trim(), Kind = (string)kind.SelectedItem, Biography = bio.Text, Relations = CinemaText.Relations(relations.Text, data.People), Releases = CinemaText.Releases(releases.Text) }, OperationToken);
            if (!IsClosed) DialogResult = true;
        });
        Status.Text = "Relationship targets must already exist. Discography paths are optional local sources, not online catalog downloads.";
    }
}
public sealed class ClassicalWorkEditor : CinemaEditor
{
    public ClassicalWorkEditor(Window owner, MainViewModel vm, CinemaCatalogData data, ClassicalWork? existing) : base(owner, vm, "Classical work", "Group ordered movements by work, composer and named classical collection.")
    {
        var item = existing ?? new ClassicalWork(); var name = Text("Work title", item.Name, 240); var collection = Text("Collection", item.Collection, 120);
        var composer = Field("Composer profile", new ComboBox { ItemsSource = data.People.Where(p => p.Kind == "Composer").OrderBy(p => p.Name).ToArray(), SelectedItem = data.People.FirstOrDefault(p => p.Id == item.ComposerId) });
        var movements = Memo("Ordered movements — one Movement title | Full local track path per line", string.Join(Environment.NewLine, item.Movements.Select(m => m.Title + " | " + m.Path)), 200000);
        var attributes = Memo("Attributes — one Field=Value | Other value per line", CinemaText.Attributes(item.Attributes), 30000);
        Action("Choose movement tracks…", () =>
        {
            var picker = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter, Multiselect = true };
            if (picker.ShowDialog(this) == true) movements.Text += (movements.Text.Length == 0 ? "" : Environment.NewLine) + string.Join(Environment.NewLine, picker.FileNames.Select(p => System.IO.Path.GetFileNameWithoutExtension(p) + " | " + p));
            return Task.CompletedTask;
        });
        Action("Save work", async () => { await vm.Cinema.SaveWork(item with { Name = name.Text.Trim(), Collection = collection.Text.Trim(), ComposerId = (composer.SelectedItem as CinemaPerson)?.Id ?? "", Movements = CinemaText.Movements(movements.Text), Attributes = CinemaText.Attributes(attributes.Text) }, OperationToken); if (!IsClosed) DialogResult = true; });
    }
}
