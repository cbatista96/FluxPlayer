using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

/// <summary>Large-type catalog navigation using standard keyboard/directional input.</summary>
public sealed class CinemaTheaterWindow : FluentWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CinemaArtwork _artwork = new();
    private CancellationTokenSource? _imageJob;
    private readonly ToolWindowFullscreen _fullscreen;
    private bool _closed;
    public CinemaTheaterWindow(Window owner, MainViewModel vm, IReadOnlyCollection<CinemaItem> rows, bool trailers)
    {
        Owner = owner; Title = "TV library · FluxPlayer"; Width = 1250; Height = 850; MinWidth = 900; MinHeight = 650; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = Brushes.Transparent;
        var outer = new Grid(); var body = new Grid { Margin = new(32) }; outer.Children.Add(body);
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.Children.Add(new TextBlock { Text = "Movies and series", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 24) });
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); Grid.SetRow(content, 1); body.Children.Add(content);
        var list = new ListBox { ItemsSource = rows.ToArray(), DisplayMemberPath = "Title", FontSize = 26, Margin = new(0, 0, 24, 0) };
        list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ToolListItemStyle");
        list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true); list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        list.SetValue(ScrollViewer.CanContentScrollProperty, true);
        System.Windows.Automation.AutomationProperties.SetName(list, "TV catalog titles"); content.Children.Add(list);
        var details = new StackPanel(); var poster = new Image { Height = 340, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }; details.Children.Add(poster);
        var title = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 12) }; details.Children.Add(title);
        var synopsis = new TextBlock { FontSize = 20, TextWrapping = TextWrapping.Wrap }; details.Children.Add(synopsis);
        var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); content.Children.Add(scroll);
        var footer = new StackPanel { Margin = new(0, 24, 0, 0) }; var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var play = new Button { Content = "Play selected", FontSize = 22, Padding = new(22, 14, 22, 14), Margin = new(0, 0, 16, 10) }; buttons.Children.Add(play);
        var full = new Button { Content = "Fullscreen (F)", FontSize = 22, Padding = new(22, 14, 22, 14), Margin = new(0, 0, 0, 10) }; buttons.Children.Add(full);
        var status = new TextBlock { Text = "↑/↓ Browse · Enter Play · → Play button · ← Titles · F Fullscreen · Esc Return/close.", FontSize = 16, TextWrapping = TextWrapping.Wrap }; footer.Children.Add(status); Grid.SetRow(footer, 2); body.Children.Add(footer);
        var chrome = (Grid)ToolWindowChrome.Wrap(this, outer); Content = chrome; _fullscreen = new(this, outer, chrome.Children[0]);
        list.SelectionChanged += async (_, _) =>
        {
            _imageJob?.Cancel(); var job = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _imageJob = job;
            poster.Source = null;
            try
            {
                if (list.SelectedItem is not CinemaItem item) { title.Text = "No titles in this view"; synopsis.Text = "Add local entries in Movies and series, or change the library filters."; return; }
                title.Text = item.Title + "\n" + item.EpisodeLabel + " · " + item.Progress; synopsis.Text = item.Synopsis;
                var image = await _artwork.Load(item.Poster, 600, job.Token); if (!_closed && !job.IsCancellationRequested) poster.Source = image;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is not OutOfMemoryException) { if (!_closed && !job.IsCancellationRequested) status.Text = "Artwork unavailable: " + ex.Message; }
            finally { if (ReferenceEquals(_imageJob, job)) _imageJob = null; job.Dispose(); }
        };
        void Play()
        {
            if (list.SelectedItem is not CinemaItem item) { status.Text = "Choose a title first."; return; }
            if (vm.PlayCinema(item, trailers)) { if (_fullscreen.IsActive) _fullscreen.Toggle(); Window playback = owner; while (playback.Owner != null) playback = playback.Owner; playback.Activate(); status.Text = "Playback started in the player: " + item.Title; }
        }
        play.Click += (_, _) => Play(); full.Click += (_, _) => _fullscreen.Toggle();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F) _fullscreen.Toggle();
            else if (e.Key == Key.Escape) { if (_fullscreen.IsActive) _fullscreen.Toggle(); else Close(); }
            else if (e.Key == Key.Right) play.Focus();
            else if (e.Key == Key.Left) list.Focus();
            else if (e.Key == Key.Enter && list.IsKeyboardFocusWithin) Play();
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) => { list.SelectedIndex = rows.Count == 0 ? -1 : 0; if (rows.Count == 0) { title.Text = "No titles in this view"; synopsis.Text = "Add titles in the movie/series library."; } list.Focus(); };
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _imageJob?.Cancel(); _artwork.Clear(); poster.Source = null; };
    }
}
