using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class SpokenMediaWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly bool _books;
    private SpokenMediaData? _data;
    private CancellationTokenSource? _job;
    private bool _closed, _binding;
    public SpokenMediaWindow(Window owner, MainViewModel vm, bool books)
    {
        InitializeComponent(); Owner = owner; _vm = vm; _books = books;
        Title = (books ? "Audiobooks" : "Podcasts") + " · FluxPlayer"; Heading.Text = books ? "Audiobooks" : "Podcasts";
        Description.Text = books ? "A local shelf with listening progress, individual speed and named bookmarks. Progress is saved on pause, stop, switching and close."
            : "RSS and Atom subscriptions. Refresh and download only when requested; offline copies are stored in your active profile.";
        Books.Visibility = BookActions.Visibility = books ? Visibility.Visible : Visibility.Collapsed;
        Episodes.Visibility = EpisodeActions.Visibility = PodcastControls.Visibility = EpisodeFilter.Visibility = books ? Visibility.Collapsed : Visibility.Visible;
        if (books) Grid.SetColumnSpan(Search, 2);
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => await Run(Refresh);
        Closed += (_, _) => { _closed = true; _job?.Cancel(); };
    }
    private async Task Refresh(CancellationToken token)
    {
        await _vm.SaveSpokenProgress(); var data = await _vm.SpokenMedia.Snapshot(token); if (_closed) return;
        string? selected = (Feeds.SelectedItem as PodcastFeed)?.Id;
        _binding = true; _data = data; Feeds.ItemsSource = data.Feeds.OrderBy(item => item.Title).ToArray();
        Feeds.SelectedItem = Feeds.Items.OfType<PodcastFeed>().FirstOrDefault(item => item.Id == selected) ?? Feeds.Items.OfType<PodcastFeed>().FirstOrDefault();
        _binding = false; Filter();
    }
    private void Filter()
    {
        if (_data == null || _binding) return; string query = Search.Text.Trim();
        string? bookId = (Books.SelectedItem as AudioBook)?.Id, episodeId = (Episodes.SelectedItem as PodcastEpisode)?.Id;
        var books = _data.Books.Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Title).ToArray();
        string? feed = (Feeds.SelectedItem as PodcastFeed)?.Id;
        var episodes = _data.Episodes.Where(item => item.FeedId == feed && item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) && (EpisodeFilter.SelectedIndex != 1 || !item.Listened) && (EpisodeFilter.SelectedIndex != 2 || item.LocalPath != null && File.Exists(item.LocalPath)))
            .OrderByDescending(item => item.PublishedUtc).ThenBy(item => item.Title).ToArray();
        Books.ItemsSource = books; Episodes.ItemsSource = episodes;
        Books.SelectedItem = books.FirstOrDefault(item => item.Id == bookId); Episodes.SelectedItem = episodes.FirstOrDefault(item => item.Id == episodeId);
        Status.Text = _books ? $"Audiobooks: {books.Length:N0} · Source files are kept in their original locations." : $"Subscriptions: {_data.Feeds.Length:N0} · Episodes in this view: {episodes.Length:N0} · No background polling.";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();
    private void Feed_Changed(object sender, SelectionChangedEventArgs e) => Filter();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(Refresh);
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_job != null) { Status.Text = "Wait for the current task or cancel it."; return; }
        using var job = new CancellationTokenSource(); _job = job; CancelJob.Visibility = Visibility.Visible;
        try { await action(job.Token); }
        catch (OperationCanceledException) { if (!_closed) Status.Text = "Task canceled. Completed copies and saved records are retained."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Net.Http.HttpRequestException or System.Xml.XmlException)
        { if (!_closed) Status.Text = ex.Message; }
        finally { _job = null; if (!_closed) CancelJob.Visibility = Visibility.Collapsed; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _job?.Cancel();
    private AudioBook Book() => Books.SelectedItem as AudioBook ?? throw new InvalidOperationException("Choose an audiobook.");
    private PodcastEpisode Episode() => Episodes.SelectedItem as PodcastEpisode ?? throw new InvalidOperationException("Choose an episode.");
    private PodcastFeed Feed() => Feeds.SelectedItem as PodcastFeed ?? throw new InvalidOperationException("Choose a subscription.");
    private async void AddBooks_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add audiobooks", Multiselect = true, Filter = "Audio files|*.mp3;*.m4a;*.m4b;*.aac;*.flac;*.ogg;*.opus;*.wav;*.wma;*.aiff;*.ape;*.wv|All files|*.*" };
        if (dialog.ShowDialog(this) == true) await Run(async token => { await _vm.SpokenMedia.AddBooks(dialog.FileNames, token); await Refresh(token); });
    }
    private async void Play_Click(object sender, RoutedEventArgs e) => await Run(_ => { if (_books) _vm.PlayBook(Book()); else _vm.PlayEpisode(Episode()); return Task.CompletedTask; });
    private async void Restart_Click(object sender, RoutedEventArgs e) => await Run(_ => { _vm.PlayBook(Book(), 0); return Task.CompletedTask; });
    private async void Subscribe_Click(object sender, RoutedEventArgs e)
    {
        string address = FeedAddress.Text.Trim();
        await Run(async token => { var uri = PodcastDocuments.Address(address); await _vm.SpokenMedia.AddFeeds(new[] { uri.AbsoluteUri }, token); await Refresh(token); Feeds.SelectedItem = Feeds.Items.OfType<PodcastFeed>().FirstOrDefault(item => item.Url == uri.AbsoluteUri); FeedAddress.Clear(); Status.Text = "Subscription saved. Choose Refresh feed to retrieve episodes."; });
    }
    private async void FeedRefresh_Click(object sender, RoutedEventArgs e) => await Run(async token => { var feed = Feed(); Status.Text = "Retrieving " + feed.Title + "…"; await _vm.SpokenMedia.RefreshFeed(feed.Id, token); await Refresh(token); });
    private async void Download_Click(object sender, RoutedEventArgs e) => await Run(async token =>
    {
        var episode = Episode(); Status.Text = "Downloading " + episode.Title + "…";
        await _vm.SpokenMedia.Download(episode.Id, new Progress<long>(bytes => { if (!_closed) Status.Text = $"Downloading {episode.Title} · {bytes / 1048576d:0.0} MiB"; }), token);
        await Refresh(token); Status.Text = "Episode saved for offline playback.";
    });
    private void Menu(object sender, params (string Label, Func<Task> Action)[] choices)
    {
        var menu = new ContextMenu(); foreach (var choice in choices) { var item = new MenuItem { Header = choice.Label }; item.Click += async (_, _) => await choice.Action(); menu.Items.Add(item); }
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void Opml_Click(object sender, RoutedEventArgs e) => Menu(sender, ("Import subscriptions…", async () =>
    {
        var dialog = new OpenFileDialog { Filter = "OPML subscriptions|*.opml;*.xml", Title = "Import podcast subscriptions" }; if (dialog.ShowDialog(this) != true) return;
        await Run(async token => { if (new FileInfo(dialog.FileName).Length > PodcastDocuments.MaximumBytes) throw new InvalidDataException("OPML documents are limited to 4 MiB."); var bytes = await File.ReadAllBytesAsync(dialog.FileName, token); var urls = await Task.Run(() => PodcastDocuments.ReadOpml(bytes), token); await _vm.SpokenMedia.AddFeeds(urls, token); await Refresh(token); });
    }), ("Export subscriptions to a new copy…", async () =>
    {
        var dialog = new SaveFileDialog { Filter = "OPML subscriptions|*.opml", FileName = "FluxPlayer-subscriptions.opml", Title = "Export subscription copy" }; if (dialog.ShowDialog(this) != true) return;
        await Run(async token => { var data = await _vm.SpokenMedia.Snapshot(token); await PodcastDocuments.WriteOpmlCopy(dialog.FileName, data.Feeds, token); Status.Text = "Subscription copy exported."; });
    }));
    private void Completion_Click(object sender, RoutedEventArgs e) => Menu(sender, ("Mark completed / listened", () => SetCompletion(true)), ("Mark in progress / not listened", () => SetCompletion(false)));
    private Task SetCompletion(bool value) => Run(async token =>
    {
        if (_books) { var book = Book(); if (_vm.IsActiveBook(book.Id)) { _vm.StopCommand.Execute(null); await _vm.SaveSpokenProgress(); book = (await _vm.SpokenMedia.Snapshot(token)).Books.First(item => item.Id == book.Id); } await _vm.SpokenMedia.UpdateBook(book.Id, book.PositionMs, book.DurationMs, book.Rate, value, token); }
        else { var episode = Episode(); if (_vm.IsActiveEpisode(episode.Id)) { _vm.StopCommand.Execute(null); await _vm.SaveSpokenProgress(); episode = (await _vm.SpokenMedia.Snapshot(token)).Episodes.First(item => item.Id == episode.Id); } await _vm.SpokenMedia.UpdateEpisode(episode.Id, episode.PositionMs, episode.DurationMs, value, token); }
        await Refresh(token);
    });
    private void Speed_Click(object sender, RoutedEventArgs e) => Menu(sender, new[] { .5f, .75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f }.Select(rate => ($"{rate:0.##}×", (Func<Task>)(() => Run(async token =>
    { var book = Book(); if (_vm.IsActiveBook(book.Id)) { _vm.PlaybackRate = rate; await _vm.SaveSpokenProgress(); } else await _vm.SpokenMedia.UpdateBook(book.Id, book.PositionMs, book.DurationMs, rate, book.Completed, token); await Refresh(token); })))).ToArray());
    private async void Bookmarks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.SaveSpokenProgress(); var book = Book(); book = (await _vm.SpokenMedia.Snapshot()).Books.First(item => item.Id == book.Id);
            var choices = new List<(string, Func<Task>)> { ("Add named bookmark…", () => Run(async token =>
            { string? name = Prompt("Bookmark name", "Bookmark"); if (name == null) return; long position = _vm.IsActiveBook(book.Id) ? _vm.Engine.GetTime() : book.PositionMs; await _vm.SpokenMedia.Bookmark(book.Id, name, position, token); await Refresh(token); })) };
            for (int i = 0; i < book.Bookmarks.Length; i++) { int index = i; var mark = book.Bookmarks[i]; choices.Add(($"Go to {mark.Name} · {PlaybackHistoryEntry.FormatTime(mark.PositionMs)}", () => Run(_ => { _vm.PlayBook(book, mark.PositionMs); return Task.CompletedTask; }))); choices.Add(($"Remove bookmark: {mark.Name}", () => Run(async token => { await _vm.SpokenMedia.RemoveBookmark(book.Id, index, token); await Refresh(token); }))); }
            Menu(sender, choices.ToArray());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { Status.Text = ex.Message; }
    }
    private string? Prompt(string title, string value)
    {
        return MediaToolsWindows.RequestText(this, _vm, title, "Name");
    }
    private bool Confirm(string message) => MessageBox.Show(this, message, "Spoken media", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private async void RemoveBook_Click(object sender, RoutedEventArgs e) => await Run(async token => { var book = Book(); if (!Confirm("Remove this audiobook's shelf entry and bookmarks? The audio file is retained.")) return; if (_vm.IsActiveBook(book.Id)) { _vm.StopCommand.Execute(null); await _vm.SaveSpokenProgress(); } await _vm.SpokenMedia.RemoveBook(book.Id, token); await Refresh(token); });
    private async void Unsubscribe_Click(object sender, RoutedEventArgs e) => await Run(async token => { var feed = Feed(); if (!Confirm("Remove this subscription and its episode history? Remove downloaded copies first.")) return; await _vm.SpokenMedia.RemoveFeed(feed.Id, token); await Refresh(token); });
    private async void RemoveDownload_Click(object sender, RoutedEventArgs e) => await Run(async token => { var episode = Episode(); if (episode.LocalPath == null) return; if (_vm.IsActiveEpisode(episode.Id)) throw new InvalidOperationException("Stop this episode before removing its offline copy."); if (!Confirm("Remove this downloaded episode copy? Its online address and listening state are retained.")) return; await _vm.SpokenMedia.RemoveDownload(episode.Id, token); await Refresh(token); });
}
