using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluxPlayer.Windows;

public sealed class CinemaLibraryWindow : FeatureWorkbench
{
    private readonly MainViewModel _vm;
    private CinemaCatalogData _data = new([], [], [], [], [], false);
    private readonly TextBox _search;
    private readonly ComboBox _kind, _series, _season, _state, _sort;
    private readonly CheckBox _trailers;
    private readonly DataGrid _table;
    private readonly ContentControl _browser = new();
    private readonly ListBox _wall = new();
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _poster = new() { Height = 230, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Image _fanart = new() { Height = 130, Stretch = Stretch.UniformToFill, Margin = new(0, 10, 0, 10) };
    private readonly Image _seasonCover = new() { Height = 100, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Image _banner = new() { Height = 70, Stretch = Stretch.Uniform, Margin = new(0, 8, 0, 8) };
    private readonly CinemaArtwork _artwork = new();
    private CancellationTokenSource? _detailJob;
    private CinemaItem[] _rows = [];
    private CinemaItem? _selected;
    private bool _loading, _wallMode;
    private int _page, _wallGeneration;
    public CinemaLibraryWindow(Window owner, MainViewModel vm, string kind) : base(owner, vm, "Movies and series", "Local movie and episode records, artwork and viewing status. Add people profiles before assigning credits.")
    {
        _vm = vm; Width = 1180; Height = 850; MinHeight = 660;
        _search = Text("Search titles, synopsis and attributes", "", 240);
        _kind = Choice("Media type", ["All video", "Movie", "Episode"], kind == "Movie" ? 1 : kind == "Episode" ? 2 : 0);
        _series = Field("Series", new ComboBox()); _season = Field("Season", new ComboBox());
        _state = Choice("Viewing status", new[] { "Any status" }.Concat(CinemaCatalogService.WatchStates));
        _sort = Choice("Order", ["Title", "Release date", "Rating", "Episode order", "Recently updated"]);
        CinemaUi.Compact(Form, 3);
        _trailers = Toggle("Queue an available local trailer before a movie", false);
        _table = Table(("Title", "Title", 3), ("Entry", "EpisodeLabel", 1), ("Status / position", "Progress", 2), ("Release", "ReleaseDate", 1), ("Rating", "Rating", 1));
        _table.SelectionMode = DataGridSelectionMode.Single; View.Content = null; _browser.Content = _table;
        _wall.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ToolListItemStyle");
        _wall.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        _wall.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) }); layout.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); layout.Children.Add(_browser);
        var detail = new StackPanel { Margin = new(18, 0, 0, 0) }; detail.Children.Add(_poster); detail.Children.Add(_banner); detail.Children.Add(_seasonCover); detail.Children.Add(_fanart); detail.Children.Add(_details);
        var detailScroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(detailScroll, 1); layout.Children.Add(detailScroll); View.Content = layout;
        _table.SelectionChanged += (_, _) => { if (_table.SelectedItem is CinemaItem item) _ = Detail(item); };
        _wall.SelectionChanged += (_, _) => { if (_wall.SelectedItem is ListBoxItem { Tag: CinemaItem item }) _ = Detail(item); };
        CinemaUi.Debounce(this, _search, Filter); _kind.SelectionChanged += (_, _) => Filter(); _state.SelectionChanged += (_, _) => Filter(); _sort.SelectionChanged += (_, _) => Filter(); _season.SelectionChanged += (_, _) => Filter();
        _series.SelectionChanged += (_, _) => { if (_loading) return; Seasons(); Filter(); };
        Action("Add movie…", () => EditNew("Movie")); Action("Add episode…", () => EditNew("Episode"));
        Action("Edit selected…", async () => { var item = Selected(); if (new CinemaItemEditor(this, vm, _data, item).ShowDialog() == true) await Reload(); });
        Action("Series and seasons…", async () => { new CinemaSeriesWindow(this, vm).ShowDialog(); await Reload(); });
        Action("Play selected", () => { _vm.PlayCinema(Selected(), _trailers.IsChecked == true); return Task.CompletedTask; });
        Action("Mark watched", () => Mark("Watched")); Action("Add to watchlist", () => Mark("Watchlist")); Action("Mark unwatched", () => Mark("Unwatched"));
        Action("Capture current position", async () =>
        {
            var item = Selected();
            if (!_vm.HasMedia || _vm.CurrentItem == null || MediaFormats.IsNetwork(_vm.CurrentItem.FilePath) || !string.Equals(System.IO.Path.GetFullPath(_vm.CurrentItem.FilePath), System.IO.Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Play this source before capturing its current position.");
            await _vm.Cinema.Progress(item.Id, "In progress", Math.Max(0, _vm.Engine.GetTime()), OperationToken); await Reload();
        });
        Action("Continue series", () =>
        {
            string id = (_series.SelectedItem as CinemaSeries)?.Id ?? _selected?.SeriesId ?? "";
            var next = CinemaCatalogService.Continue(_data, id) ?? throw new InvalidOperationException("Choose a series with unwatched episodes.");
            _vm.PlayCinema(next, false); return Task.CompletedTask;
        });
        Action("Poster wall / table", async () => { _wallMode = !_wallMode; _page = 0; await RenderWall(); });
        Action("Previous page", async () => { _page = Math.Max(0, _page - 1); await RenderWall(); });
        Action("Next page", async () => { if ((_page + 1) * 40 < _rows.Length) _page++; await RenderWall(); });
        Action("TV view…", () => { new CinemaTheaterWindow(this, vm, _rows, _data.PlayTrailers).Show(); return Task.CompletedTask; });
        Action("Save trailer preference", async () => { await vm.Cinema.SetTrailers(_trailers.IsChecked == true, OperationToken); await Reload(); });
        Action("Refresh", Reload);
        CinemaUi.GroupActions(Actions,
            ("Add and edit", ["Add movie…", "Add episode…", "Edit selected…", "Series and seasons…"]),
            ("Viewing status", ["Mark watched", "Add to watchlist", "Mark unwatched", "Capture current position"]),
            ("Views", ["Poster wall / table", "Previous page", "Next page", "TV view…"]),
            ("Playback options", ["Continue series", "Save trailer preference"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Loaded += async (_, _) => await Run(Reload);
        Closed += (_, _) => { _detailJob?.Cancel(); _wallGeneration++; _artwork.Clear(); };
    }
    private CinemaItem Selected() => _selected ?? throw new InvalidOperationException("Select a catalog entry.");
    private async Task EditNew(string kind)
    {
        var picker = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter };
        if (picker.ShowDialog(this) != true) return;
        var item = new CinemaItem { Title = System.IO.Path.GetFileNameWithoutExtension(picker.FileName), Path = picker.FileName, Kind = kind, SeriesId = kind == "Episode" ? (_series.SelectedItem as CinemaSeries)?.Id ?? "" : "", Season = (_season.SelectedItem as CinemaSeason)?.Number ?? 1 };
        if (new CinemaItemEditor(this, _vm, _data, item).ShowDialog() == true) await Reload();
    }
    private async Task Mark(string state) { await _vm.Cinema.Progress(Selected().Id, state, 0, OperationToken); await Reload(); }
    private async Task Reload()
    {
        var data = await _vm.Cinema.Snapshot(OperationToken); if (IsClosed) return;
        string id = (_series.SelectedItem as CinemaSeries)?.Id ?? ""; _data = data; _loading = true;
        _series.ItemsSource = new object[] { "All series" }.Concat(data.Series.OrderBy(s => s.Name).Cast<object>()).ToArray();
        _series.SelectedItem = (object?)data.Series.FirstOrDefault(s => s.Id == id) ?? "All series";
        _trailers.IsChecked = data.PlayTrailers; _loading = false; Seasons(); Filter();
    }
    private void Seasons()
    {
        int? number = (_season.SelectedItem as CinemaSeason)?.Number; _loading = true;
        string id = (_series.SelectedItem as CinemaSeries)?.Id ?? "";
        var seasons = _data.Seasons.Where(s => s.SeriesId == id).OrderBy(s => s.Number).ToArray();
        _season.ItemsSource = new object[] { "All seasons" }.Concat(seasons.Cast<object>()).ToArray();
        _season.SelectedItem = (object?)seasons.FirstOrDefault(s => s.Number == number) ?? "All seasons"; _loading = false;
    }
    private void Filter()
    {
        if (_loading || IsClosed) return;
        _rows = CinemaCatalogService.Query(_data, _search.Text, _kind.SelectedItem?.ToString() ?? "All video", (_series.SelectedItem as CinemaSeries)?.Id ?? "", (_season.SelectedItem as CinemaSeason)?.Number, _state.SelectedItem?.ToString() ?? "Any status", _sort.SelectedItem?.ToString() ?? "Title");
        string? selected = _selected?.Id; _table.ItemsSource = _rows; _table.SelectedItem = _rows.FirstOrDefault(i => i.Id == selected);
        _page = 0; _ = RenderWall();
        if (_table.SelectedItem == null) { _selected = null; _detailJob?.Cancel(); _poster.Source = _fanart.Source = _banner.Source = _seasonCover.Source = null; _details.Text = "Select an entry to view its synopsis, artwork and credits."; }
        Status.Text = $"{_rows.Length:N0} of {_data.Items.Length:N0} entries. Position capture and watched flags are explicit actions.";
    }
    private async Task Detail(CinemaItem item)
    {
        _selected = item; _detailJob?.Cancel(); var job = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token); _detailJob = job;
        _poster.Source = _fanart.Source = _banner.Source = _seasonCover.Source = null;
        string cast = CinemaText.Credits(item.Credits, _data.People), attributes = CinemaText.Attributes(item.Attributes);
        _details.Text = item.Title + "\n" + item.EpisodeLabel + " · " + item.Progress + "\nRelease: " + (item.ReleaseDate?.ToString("yyyy-MM-dd") ?? "Unknown") + "\nClassification: " + item.Classification + $"\nPersonal rating: {item.Rating:0.##}/10\n\n" + item.Synopsis + "\n\nCast and production credits\n" + cast + "\n\n" + attributes + "\n\nSource: " + item.Path;
        try
        {
            var poster = await _artwork.Load(item.Poster, 480, job.Token); if (IsClosed || job.IsCancellationRequested) return; _poster.Source = poster;
            var fanart = await _artwork.Load(item.Fanart, 1000, job.Token); if (IsClosed || job.IsCancellationRequested) return; _fanart.Source = fanart;
            var show = _data.Series.FirstOrDefault(s => s.Id == item.SeriesId); var season = _data.Seasons.FirstOrDefault(s => s.SeriesId == item.SeriesId && s.Number == item.Season);
            var banner = await _artwork.Load(show?.Banner ?? "", 1000, job.Token); if (IsClosed || job.IsCancellationRequested) return; _banner.Source = banner;
            var cover = await _artwork.Load(season?.Cover ?? "", 320, job.Token); if (IsClosed || job.IsCancellationRequested) return; _seasonCover.Source = cover;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (!IsClosed && !job.IsCancellationRequested) Status.Text = "Artwork unavailable: " + ex.Message; }
        finally { if (ReferenceEquals(_detailJob, job)) _detailJob = null; job.Dispose(); }
    }
    private async Task RenderWall()
    {
        int generation = ++_wallGeneration;
        _browser.Content = _wallMode ? _wall : _table;
        if (!_wallMode) { _wall.Items.Clear(); return; }
        _wall.Items.Clear();
        foreach (var item in _rows.Skip(_page * 40).Take(40))
        {
            if (generation != _wallGeneration || IsClosed) return;
            var image = new Image { Width = 130, Height = 185, Stretch = Stretch.Uniform };
            var card = new StackPanel { Width = 145, Margin = new(5) }; card.Children.Add(image); card.Children.Add(new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Title, Margin = new(0, 6, 0, 0) }); card.Children.Add(new TextBlock { Text = item.Progress, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            _wall.Items.Add(new ListBoxItem { Content = card, Tag = item });
            try { var thumbnail = await _artwork.Load(item.Poster, 240, Lifetime.Token); if (generation != _wallGeneration || IsClosed) return; image.Source = thumbnail; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { image.ToolTip = "Artwork unavailable"; }
        }
        if (!IsClosed && generation == _wallGeneration) Status.Text = $"Poster wall · page {_page + 1} of {Math.Max(1, (_rows.Length + 39) / 40)} · 40 thumbnails per page maximum.";
    }
}

internal static class CinemaUi
{
    public static void Debounce(Window window, TextBox search, Action filter)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => { timer.Stop(); filter(); };
        search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        window.Closed += (_, _) => timer.Stop();
    }
    public static void GroupActions(WrapPanel panel, params (string Label, string[] Names)[] groups)
    {
        var buttons = panel.Children.OfType<Button>().ToDictionary(b => b.Content?.ToString() ?? "", StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var menu = new ContextMenu();
            foreach (string name in group.Names)
            {
                var original = buttons[name]; panel.Children.Remove(original);
                var item = new MenuItem { Header = name }; item.Click += (_, _) => original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); menu.Items.Add(item);
            }
            var button = new Button { Content = group.Label + "…", Padding = new(14, 8, 14, 8), Margin = new(0, 0, 8, 6), ContextMenu = menu };
            button.Click += (_, _) => { menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
            panel.Children.Add(button);
        }
    }
    public static void Compact(StackPanel form, int columns)
    {
        var children = form.Children.Cast<UIElement>().ToArray(); form.Children.Clear(); var grid = new Grid();
        for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new());
        for (int i = 0; i < (children.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (int i = 0; i < children.Length; i++) { Grid.SetRow(children[i], i / columns); Grid.SetColumn(children[i], i % columns); if (children[i] is FrameworkElement element) element.Margin = new(0, 0, i % columns == columns - 1 ? 0 : 12, 12); grid.Children.Add(children[i]); }
        form.Children.Add(grid);
    }
}
