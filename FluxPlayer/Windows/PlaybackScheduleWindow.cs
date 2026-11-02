using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class PlaybackScheduleWindow : FluentWindow
{
    public PlaybackScheduleWindow(Window owner, MainViewModel vm)
    {
        Owner = owner; Title = "Playback schedule · FluxPlayer"; Width = 1060; Height = 760; MinWidth = 940; MinHeight = 680; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var body = new Grid { Margin = new(24, 18, 24, 20) }; foreach (var size in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) body.RowDefinitions.Add(new() { Height = size });
        var heading = new StackPanel { Margin = new(0, 0, 0, 16) }; heading.Children.Add(new TextBlock { Text = "Playback schedule", FontSize = 25, FontWeight = FontWeights.SemiBold }); heading.Children.Add(new TextBlock { Text = "One-time or weekly playlist, stop, volume and alarm tasks. FluxPlayer must remain open. Missed occurrences older than five minutes are skipped. Higher priorities run last when times match; this does not wake the computer.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) }); body.Children.Add(heading);
        var form = new Grid { Margin = new(0, 0, 0, 16) }; for (int i = 0; i < 4; i++) form.RowDefinitions.Add(new() { Height = GridLength.Auto }); form.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); form.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        T Field<T>(string label, T input, int row, int column) where T : Control
        { var panel = new StackPanel { Margin = new(column == 0 ? 0 : 12, 0, 0, 12) }; panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 6) }); panel.Children.Add(input); Grid.SetRow(panel, row); Grid.SetColumn(panel, column); form.Children.Add(panel); AutomationProperties.SetName(input, label); return input; }
        var name = Field("Task name", new TextBox { MaxLength = 120, Padding = new(10, 7, 10, 7) }, 0, 0); var action = Field("Action", new ComboBox { ItemsSource = new[] { "Play saved playlist", "Stop playback", "Fade volume", "Set volume", "Alarm" }, SelectedIndex = 0 }, 0, 1);
        var when = DateTime.Now.AddMinutes(5); var date = new DatePicker { SelectedDate = when.Date, SelectedDateFormat = DatePickerFormat.Short, Width = 180 }; var time = new TextBox { Text = when.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Width = 115, Margin = new(10, 0, 0, 0), Padding = new(10, 7, 10, 7) };
        AutomationProperties.SetName(date, "Scheduled local date"); AutomationProperties.SetName(time, "Scheduled local time in HH:mm:ss");
        var datePanel = new StackPanel { Orientation = Orientation.Horizontal }; datePanel.Children.Add(date); datePanel.Children.Add(time); var dateLabel = new StackPanel { Margin = new(0, 0, 0, 12) }; dateLabel.Children.Add(new TextBlock { Text = "Local date and time (HH:mm:ss)", Margin = new(0, 0, 0, 6) }); dateLabel.Children.Add(datePanel); Grid.SetRow(dateLabel, 1); form.Children.Add(dateLabel);
        var playlists = Field("Saved playlist", new ComboBox { DisplayMemberPath = "Name" }, 1, 1);
        var volume = Field("Target volume (%)", new TextBox { Text = "0", Padding = new(10, 7, 10, 7) }, 2, 0); var seconds = Field("Fade duration (1–300 seconds)", new TextBox { Text = "10", Padding = new(10, 7, 10, 7) }, 2, 1);
        var priority = Field("Priority (−10 to +10; higher runs last)", new TextBox { Text = "0", Padding = new(10, 7, 10, 7) }, 3, 0);
        var days = new StackPanel { Margin = new(12, 0, 0, 12) }; days.Children.Add(new TextBlock { Text = "Repeat on weekdays (none = once)", Margin = new(0, 0, 0, 6) }); var dayChoices = new WrapPanel(); var checks = new List<CheckBox>(); foreach (var day in Enum.GetValues<DayOfWeek>()) { var check = new CheckBox { Content = day.ToString()[..3], Tag = (int)day, Margin = new(0, 0, 12, 6) }; checks.Add(check); dayChoices.Children.Add(check); } days.Children.Add(dayChoices); Grid.SetRow(days, 3); Grid.SetColumn(days, 1); form.Children.Add(days);
        void Fields() { playlists.IsEnabled = action.SelectedIndex == 0; volume.IsEnabled = action.SelectedIndex is 2 or 3; seconds.IsEnabled = action.SelectedIndex == 2; } action.SelectionChanged += (_, _) => Fields(); Fields(); Grid.SetRow(form, 1); body.Children.Add(form);
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36, GridLinesVisibility = DataGridGridLinesVisibility.None, EnableRowVirtualization = true };
        foreach (var (title, property, width) in new[] { ("Task", "Name", 1.5d), ("Action", "ActionName", 1.2d), ("Local time", "DueLocal", 1.5d), ("Repeat", "Recurrence", 1d), ("Priority", "Priority", .5d), ("State", "State", 1.5d) }) table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width, DataGridLengthUnitType.Star) }); Grid.SetRow(table, 2); body.Children.Add(table);
        var footer = new StackPanel { Margin = new(0, 14, 0, 0) }; var buttons = new WrapPanel(); var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) }; footer.Children.Add(buttons); footer.Children.Add(status); Grid.SetRow(footer, 3); body.Children.Add(footer);
        bool busy = false, closed = false;
        async Task Run(Func<Task> work) { if (busy) return; busy = true; buttons.IsEnabled = false; try { await work(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or InvalidOperationException) { if (!closed) status.Text = ex.Message; } finally { busy = false; if (!closed) buttons.IsEnabled = true; } }
        async Task Refresh() { var tasks = await vm.PlaybackSchedule.Snapshot(); var lists = await vm.MediaCatalog.SnapshotAsync(); if (closed) return; table.ItemsSource = tasks.Tasks.OrderBy(task => task.DueUtc).ToArray(); string? selected = (playlists.SelectedItem as FluxPlayer.Models.SavedMediaPlaylist)?.Id; playlists.ItemsSource = lists.Playlists; playlists.SelectedItem = lists.Playlists.FirstOrDefault(item => item.Id == selected) ?? lists.Playlists.FirstOrDefault(); await vm.RefreshPlaybackSchedule(); status.Text = "Playlist tasks replace the current queue and start explicitly at their scheduled time. Native playback remains source-dependent."; }
        Button Button(string text) { var button = new Button { Content = text, Padding = new(13, 8, 13, 8), Margin = new(0, 0, 8, 0) }; buttons.Children.Add(button); return button; }
        var save = Button("Add scheduled task"); var cancel = Button("Cancel selected task"); var remove = Button("Remove selected task…"); var refresh = Button("Refresh");
        save.Click += async (_, _) => await Run(async () =>
        {
            if (date.SelectedDate is not { } selectedDate || !TimeSpan.TryParseExact(time.Text.Trim(), new[] { @"hh\:mm\:ss", @"hh\:mm" }, CultureInfo.InvariantCulture, out var selectedTime)) throw new ArgumentException("Choose a date and a time in HH:mm:ss.");
            var due = PlaybackSchedule.LocalTime(selectedDate, selectedTime, TimeZoneInfo.Local); if (due <= DateTimeOffset.UtcNow) throw new ArgumentException("Choose a future time.");
            double target = 0; int length = 10; if (action.SelectedIndex is 2 or 3 && (!double.TryParse(volume.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out target) || !double.IsFinite(target) || target < 0 || target > vm.MaxVolume)) throw new ArgumentException("Choose a supported volume."); if (action.SelectedIndex == 2 && (!int.TryParse(seconds.Text, out length) || length is < 1 or > 300)) throw new ArgumentException("Choose a fade duration of 1–300 seconds.");
            if (!int.TryParse(priority.Text, out int rank) || rank is < -10 or > 10) throw new ArgumentException("Use priority −10 to +10."); int weekdays = checks.Where(check => check.IsChecked == true).Aggregate(0, (mask, check) => mask | 1 << (int)check.Tag);
            string? listId = (playlists.SelectedItem as FluxPlayer.Models.SavedMediaPlaylist)?.Id; if (action.SelectedIndex == 0 && listId == null) throw new ArgumentException("Create a saved playlist in Library settings before scheduling playback.");
            await vm.PlaybackSchedule.Save(new(Guid.NewGuid().ToString("N"), name.Text, due, (ScheduledPlaybackAction)action.SelectedIndex, listId, target, length, Weekdays: weekdays, ZoneId: TimeZoneInfo.Local.Id, LocalSeconds: (int)selectedTime.TotalSeconds, Priority: rank)); name.Clear(); await Refresh();
        });
        cancel.Click += async (_, _) => await Run(async () => { if (table.SelectedItem is not PlaybackTask task) throw new InvalidOperationException("Choose a task."); await vm.PlaybackSchedule.Cancel(task.Id); await Refresh(); }); refresh.Click += async (_, _) => await Run(Refresh);
        remove.Click += async (_, _) => await Run(async () =>
        {
            if (table.SelectedItem is not PlaybackTask task) throw new InvalidOperationException("Choose a task.");
            if (MessageBox.Show(this, "Remove this scheduled task? Media files and saved playlists are preserved.", "Remove scheduled task", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await vm.PlaybackSchedule.Remove(task.Id); await Refresh();
        });
        Content = ToolWindowChrome.Wrap(this, body); SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this); Loaded += async (_, _) => await Run(Refresh); Closed += (_, _) => closed = true;
    }
}

public sealed class VolumeFadeWindow : FluentWindow
{
    public VolumeFadeWindow(Window owner, MainViewModel vm)
    {
        Owner = owner; Title = "Volume fade · FluxPlayer"; Width = 620; Height = 440; MinWidth = 580; MinHeight = 400; ExtendsContentIntoTitleBar = true; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var body = new StackPanel { Margin = new(24, 18, 24, 20) }; body.Children.Add(new TextBlock { Text = "Volume fade", FontSize = 25, FontWeight = FontWeights.SemiBold }); body.Children.Add(new TextBlock { Text = "Change playback volume gradually. Mute is preserved. Manual volume changes or Stop cancel the active fade.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 18) });
        body.Children.Add(new TextBlock { Text = "Target volume (%)", Margin = new(0, 0, 0, 6) }); var target = new Slider { Minimum = 0, Maximum = vm.MaxVolume, Value = 0 }; body.Children.Add(target); var value = new TextBlock(); value.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = target, StringFormat = "{0:0}%" }); body.Children.Add(value);
        body.Children.Add(new TextBlock { Text = "Duration (1–300 seconds)", Margin = new(0, 12, 0, 6) }); var duration = new TextBox { Text = "10", Padding = new(10, 7, 10, 7) }; body.Children.Add(duration);
        var actions = new WrapPanel { Margin = new(0, 16, 0, 0) }; var start = new Button { Content = "Start fade", Padding = new(16, 8, 16, 8), Margin = new(0, 0, 8, 0) }; var cancel = new Button { Content = "Cancel fade", Padding = new(16, 8, 16, 8) }; actions.Children.Add(start); actions.Children.Add(cancel); body.Children.Add(actions); var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) }; body.Children.Add(status);
        start.Click += async (_, _) => { try { if (!int.TryParse(duration.Text, out int seconds)) throw new ArgumentException("Enter a duration in seconds."); status.Text = "Fading…"; await vm.FadeVolume(target.Value, seconds); status.Text = "Fade finished or canceled."; } catch (ArgumentException ex) { status.Text = ex.Message; } }; cancel.Click += (_, _) => { vm.CancelVolumeFade(); status.Text = "Fade canceled."; };
        Content = ToolWindowChrome.Wrap(this, body); SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
    }
}
