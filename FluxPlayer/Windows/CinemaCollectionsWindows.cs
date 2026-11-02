using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluxPlayer.Windows;

public sealed class CinemaSeriesWindow : FeatureWorkbench
{
    private CinemaCatalogData _data = new([], [], [], [], [], false);
    public CinemaSeriesWindow(Window owner, MainViewModel vm) : base(owner, vm, "Series and seasons", "Maintain series records, synopses, banners and named seasons with covers. Episode membership is edited in Movies and series.")
    {
        var shows = Field("Series", new ComboBox());
        var synopsis = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 100, Margin = new(0, 0, 0, 12) }; Form.Children.Add(synopsis);
        var banner = new Image { Height = 85, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }; Form.Children.Add(banner);
        var table = Table(("Season", "Number", 1), ("Name", "Name", 3), ("Cover path", "Cover", 3)); table.SelectionMode = DataGridSelectionMode.Single;
        var artwork = new CinemaArtwork(); int generation = 0;
        async Task Refresh()
        {
            string? id = (shows.SelectedItem as CinemaSeries)?.Id;
            var data = await vm.Cinema.Snapshot(OperationToken); if (IsClosed) return; _data = data;
            shows.ItemsSource = data.Series.OrderBy(s => s.Name).ToArray(); shows.SelectedItem = data.Series.FirstOrDefault(s => s.Id == id) ?? data.Series.FirstOrDefault();
            Status.Text = $"{data.Series.Length} series / {data.Seasons.Length} seasons. Select a series before adding a season.";
        }
        shows.SelectionChanged += async (_, _) =>
        {
            int request = ++generation; banner.Source = null;
            if (shows.SelectedItem is not CinemaSeries show) { synopsis.Text = "Create a series to begin."; table.ItemsSource = null; return; }
            synopsis.Text = show.Synopsis; table.ItemsSource = _data.Seasons.Where(s => s.SeriesId == show.Id).OrderBy(s => s.Number).ToArray();
            try { var image = await artwork.Load(show.Banner, 1000, Lifetime.Token); if (!IsClosed && generation == request) banner.Source = image; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { if (!IsClosed && generation == request) Status.Text = "Banner unavailable: " + ex.Message; }
        };
        Action("New series…", async () => { if (new CinemaSeriesEditor(this, vm, null).ShowDialog() == true) await Refresh(); });
        Action("Edit series…", async () => { var show = shows.SelectedItem as CinemaSeries ?? throw new InvalidOperationException("Choose a series."); if (new CinemaSeriesEditor(this, vm, show).ShowDialog() == true) await Refresh(); });
        Action("New season…", async () => { var show = shows.SelectedItem as CinemaSeries ?? throw new InvalidOperationException("Choose a series."); if (new CinemaSeasonEditor(this, vm, show, null).ShowDialog() == true) await Refresh(); });
        Action("Edit season…", async () => { var show = shows.SelectedItem as CinemaSeries ?? throw new InvalidOperationException("Choose a series."); var season = table.SelectedItem as CinemaSeason ?? throw new InvalidOperationException("Choose a season."); if (new CinemaSeasonEditor(this, vm, show, season).ShowDialog() == true) await Refresh(); });
        Action("Refresh", Refresh); Loaded += async (_, _) => await Run(Refresh); Closed += (_, _) => { generation++; artwork.Clear(); };
    }
}

public sealed class CinemaPeopleWindow : FeatureWorkbench
{
    private CinemaCatalogData _data = new([], [], [], [], [], false);
    public CinemaPeopleWindow(Window owner, MainViewModel vm) : base(owner, vm, "Artists, composers and actors", "Local biographies, composer/actor profiles, relationships, discographies and linked production credits. Metadata is manually curated.")
    {
        Width = 1150;
        var search = Text("Search people and biographies", "", 240); var kind = Choice("Profile type", ["All people", "Artist", "Composer", "Actor"]); CinemaUi.Compact(Form, 2);
        var table = Table(("Name", "Name", 3), ("Type", "Kind", 1)); table.SelectionMode = DataGridSelectionMode.Single; View.Content = null;
        var details = new StackPanel { Margin = new(18, 0, 0, 0) };
        var bio = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; details.Children.Add(bio);
        details.Children.Add(new TextBlock { Text = "Related people", FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 6) });
        var relationships = new ComboBox(); System.Windows.Automation.AutomationProperties.SetName(relationships, "Related people"); details.Children.Add(relationships);
        var follow = new Button { Content = "Open related profile", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 7, 12, 7), Margin = new(0, 8, 0, 16) }; details.Children.Add(follow);
        details.Children.Add(new TextBlock { Text = "Discography", FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 6) });
        var releases = new ComboBox { DisplayMemberPath = "Display" }; System.Windows.Automation.AutomationProperties.SetName(releases, "Discography release"); details.Children.Add(releases);
        var tracks = new ListBox { MaxHeight = 150, Margin = new(0, 8, 0, 12) }; tracks.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ToolListItemStyle"); details.Children.Add(tracks);
        details.Children.Add(new TextBlock { Text = "Cast and production credits", FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 6) });
        var credits = new TextBlock { TextWrapping = TextWrapping.Wrap }; details.Children.Add(credits);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); grid.Children.Add(table); var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); View.Content = grid;
        void Filter()
        {
            var selected = (table.SelectedItem as CinemaPerson)?.Id;
            var rows = _data.People.Where(p => (kind.SelectedIndex == 0 || p.Kind == kind.SelectedItem?.ToString()) && (p.Name + " " + p.Biography).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            table.ItemsSource = rows; table.SelectedItem = rows.FirstOrDefault(p => p.Id == selected); Status.Text = $"{rows.Length} people. Local profile data; no online enrichment or scraping.";
        }
        async Task Refresh() { var data = await vm.Cinema.Snapshot(OperationToken); if (IsClosed) return; _data = data; Filter(); }
        table.SelectionChanged += (_, _) =>
        {
            if (table.SelectedItem is not CinemaPerson person) { bio.Text = "Select a profile."; relationships.ItemsSource = null; releases.ItemsSource = null; tracks.ItemsSource = null; credits.Text = ""; return; }
            bio.Text = person.Name + " · " + person.Kind + "\n\n" + person.Biography;
            relationships.ItemsSource = person.Relations.Select(r => new PersonLink(r.PersonId, (_data.People.FirstOrDefault(p => p.Id == r.PersonId)?.Name ?? "Unknown") + " · " + r.Relationship)).ToArray();
            releases.ItemsSource = person.Releases.OrderBy(r => r.Year).ThenBy(r => r.Title).ToArray(); releases.SelectedIndex = person.Releases.Length == 0 ? -1 : 0;
            credits.Text = string.Join(Environment.NewLine, _data.Items.SelectMany(i => i.Credits.Where(c => c.PersonId == person.Id).Select(c => i.Title + " · " + c.Job + (c.Role == "" ? "" : " · " + c.Role))));
        };
        releases.SelectionChanged += (_, _) => tracks.ItemsSource = (releases.SelectedItem as ArtistRelease)?.Paths.ToArray();
        follow.Click += (_, _) =>
        {
            if (relationships.SelectedItem is not PersonLink link) return;
            kind.SelectedIndex = 0; search.Clear(); Filter(); table.SelectedItem = _data.People.FirstOrDefault(p => p.Id == link.Id); if (table.SelectedItem != null) table.ScrollIntoView(table.SelectedItem);
        };
        CinemaUi.Debounce(this, search, Filter); kind.SelectionChanged += (_, _) => Filter();
        Action("New profile…", async () => { if (new CinemaPersonEditor(this, vm, _data, null).ShowDialog() == true) await Refresh(); });
        Action("Edit selected…", async () => { var person = table.SelectedItem as CinemaPerson ?? throw new InvalidOperationException("Choose a person."); if (new CinemaPersonEditor(this, vm, _data, person).ShowDialog() == true) await Refresh(); });
        Action("Play selected release", () =>
        {
            var release = releases.SelectedItem as ArtistRelease ?? throw new InvalidOperationException("Choose a discography release.");
            int added = vm.AddCatalogTracks(release.Paths.Select(p => new SavedMediaTrack(p, System.IO.Path.GetFileNameWithoutExtension(p))), true); Status.Text = $"Queued {added} available local tracks."; return Task.CompletedTask;
        });
        Action("Refresh", Refresh); Loaded += async (_, _) => await Run(Refresh);
    }
    private sealed record PersonLink(string Id, string Label) { public override string ToString() => Label; }
}

public sealed class ClassicalLibraryWindow : FeatureWorkbench
{
    private CinemaCatalogData _data = new([], [], [], [], [], false);
    public ClassicalLibraryWindow(Window owner, MainViewModel vm) : base(owner, vm, "Classical collections", "Browse named classical collections by composer and work, preserving movement order. Multiple attribute values support performers, instruments, periods and catalog numbers.")
    {
        var search = Text("Search work and attributes", "", 240); var collection = Field("Collection", new ComboBox()); var composer = Field("Composer", new ComboBox()); CinemaUi.Compact(Form, 3);
        var table = Table(("Work", "Name", 3), ("Collection", "Collection", 2)); table.SelectionMode = DataGridSelectionMode.Single; View.Content = null;
        var list = new ListBox { DisplayMemberPath = "Title", Margin = new(16, 0, 0, 0) }; list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ToolListItemStyle");
        var attributes = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(16, 12, 0, 0) };
        var right = new Grid(); right.RowDefinitions.Add(new()); right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.Children.Add(list); Grid.SetRow(attributes, 1); right.Children.Add(attributes);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); grid.Children.Add(table); Grid.SetColumn(right, 1); grid.Children.Add(right); View.Content = grid;
        bool loading = false;
        void Filter()
        {
            if (loading) return;
            string? id = (table.SelectedItem as ClassicalWork)?.Id;
            var rows = _data.Works.Where(w => (collection.SelectedIndex <= 0 || w.Collection == collection.SelectedItem?.ToString()) && (composer.SelectedIndex <= 0 || w.ComposerId == (composer.SelectedItem as CinemaPerson)?.Id) && (w.Name + " " + CinemaText.Attributes(w.Attributes)).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            table.ItemsSource = rows; table.SelectedItem = rows.FirstOrDefault(w => w.Id == id); Status.Text = $"{rows.Length} works. Movement order follows the saved work; unavailable sources are skipped when queued.";
        }
        async Task Refresh()
        {
            var data = await vm.Cinema.Snapshot(OperationToken); if (IsClosed) return; _data = data; loading = true;
            collection.ItemsSource = new[] { "All collections" }.Concat(data.Works.Select(w => w.Collection).Distinct(StringComparer.OrdinalIgnoreCase).Order()).ToArray(); collection.SelectedIndex = 0;
            composer.ItemsSource = new object[] { "All composers" }.Concat(data.People.Where(p => p.Kind == "Composer").OrderBy(p => p.Name).Cast<object>()).ToArray(); composer.SelectedIndex = 0; loading = false; Filter();
        }
        table.SelectionChanged += (_, _) => { var work = table.SelectedItem as ClassicalWork; list.ItemsSource = work?.Movements.ToArray(); attributes.Text = work == null ? "Select a work to browse its movements." : "Composer: " + (_data.People.FirstOrDefault(p => p.Id == work.ComposerId)?.Name ?? "Unknown") + "\n\n" + CinemaText.Attributes(work.Attributes); };
        CinemaUi.Debounce(this, search, Filter); collection.SelectionChanged += (_, _) => Filter(); composer.SelectionChanged += (_, _) => Filter();
        Action("New work…", async () => { if (new ClassicalWorkEditor(this, vm, _data, null).ShowDialog() == true) await Refresh(); });
        Action("Edit work…", async () => { var work = table.SelectedItem as ClassicalWork ?? throw new InvalidOperationException("Choose a work."); if (new ClassicalWorkEditor(this, vm, _data, work).ShowDialog() == true) await Refresh(); });
        Action("Play work in order", () =>
        {
            var work = table.SelectedItem as ClassicalWork ?? throw new InvalidOperationException("Choose a work."); int added = vm.AddCatalogTracks(work.Movements.Select(m => new SavedMediaTrack(m.Path, m.Title)), true); Status.Text = $"Queued {added} available movements in saved order."; return Task.CompletedTask;
        });
        Action("Play movement", () => { var movement = list.SelectedItem as ClassicalMovement ?? throw new InvalidOperationException("Choose a movement."); vm.AddCatalogTracks([new(movement.Path, movement.Title)], true); return Task.CompletedTask; });
        Action("Refresh", Refresh); Loaded += async (_, _) => await Run(Refresh);
    }
}
