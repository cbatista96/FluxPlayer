using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FluxPlayer.Windows;

public sealed class TelevisionWindow : FeatureWorkbench
{
    private TelevisionData _data = new([], [], [], []);
    public TelevisionWindow(Window owner, MainViewModel vm) : base(owner, vm, "Television, guide and recordings", "Import local IPTV M3U and XMLTV files, play provided streams and explicitly record or buffer them. Optional scheduled recordings require this app to remain running. Stream and codec support depend on the source.")
    {
        Width = 1180; Height = 820;
        var group = Field("Channel group", new ComboBox()); var channel = Field("Channel", new ComboBox()); var seconds = Text("Capture duration (seconds)", "60", 5); var due = Text("Recording start (ISO 8601 with explicit offset)", DateTimeOffset.Now.AddMinutes(5).ToString("o", CultureInfo.InvariantCulture), 80); CinemaUi.Compact(Form, 2);
        var guide = Table(("Start", "StartLocal", 2), ("Programme", "Title", 3), ("Description", "Description", 4)); guide.SelectionMode = DataGridSelectionMode.Single; View.Content = null;
        DataGrid Listing(params (string Label, string Property, double Width)[] columns)
        { var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = 36, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None, SelectionMode = DataGridSelectionMode.Single }; foreach (var column in columns) table.Columns.Add(new DataGridTextColumn { Header = column.Label, Binding = new Binding(column.Property), Width = new DataGridLength(column.Width, DataGridLengthUnitType.Star) }); return table; }
        var recordings = Listing(("Recording", "Title", 2), ("File", "Path", 4), ("Started (UTC)", "StartedUtc", 2), ("Seconds", "Seconds", 1));
        var tasks = Listing(("Start", "DueLocal", 2), ("State", "State", 1), ("Seconds", "Seconds", 1), ("New destination", "Destination", 4));
        var bufferRows = new ListBox(); bufferRows.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ToolListItemStyle"); var buffer = new TelevisionBuffer(vm.ProfileDirectory);
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Programme guide", Content = guide }); tabs.Items.Add(new TabItem { Header = "Recordings", Content = recordings }); tabs.Items.Add(new TabItem { Header = "Scheduled recordings", Content = tasks }); tabs.Items.Add(new TabItem { Header = "Timeshift segments", Content = bufferRows }); View.Content = tabs;
        bool loading = false;
        void Channels()
        { if (loading) return; string? id = (channel.SelectedItem as TvChannel)?.Id; var rows = _data.Channels.Where(c => group.SelectedIndex <= 0 || c.Group == group.SelectedItem?.ToString()).OrderBy(c => c.Name).ToArray(); channel.ItemsSource = rows; channel.SelectedItem = rows.FirstOrDefault(c => c.Id == id) ?? rows.FirstOrDefault(); }
        async Task Refresh()
        {
            var data = await vm.Television.Snapshot(OperationToken); if (IsClosed) return; _data = data; loading = true; string? selected = group.SelectedItem?.ToString();
            group.ItemsSource = new[] { "All groups" }.Concat(data.Channels.Select(c => c.Group).Where(g => g != "").Distinct().Order()).ToArray(); group.SelectedItem = selected; if (group.SelectedIndex < 0) group.SelectedIndex = 0; loading = false; Channels(); recordings.ItemsSource = data.Recordings.OrderByDescending(r => r.StartedUtc).ToArray(); tasks.ItemsSource = data.Tasks.OrderBy(t => t.DueUtc).ToArray(); bufferRows.ItemsSource = buffer.CompletedSegments();
            Status.Text = $"{data.Channels.Length} channels · {data.Guide.Length} programmes · {vm.TelevisionRecorder.Status} · {buffer.Status}";
        }
        group.SelectionChanged += (_, _) => Channels(); channel.SelectionChanged += (_, _) => { var chosen = channel.SelectedItem as TvChannel; guide.ItemsSource = chosen == null ? null : _data.Guide.Where(p => p.GuideId == chosen.GuideId && p.EndUtc >= DateTimeOffset.UtcNow.AddHours(-6)).OrderBy(p => p.StartUtc).Take(300).ToArray(); };
        TvChannel Selected() => channel.SelectedItem as TvChannel ?? throw new InvalidOperationException("Choose a channel.");
        string? Destination(string title) { var picker = new SaveFileDialog { Filter = "New stream recording|*.mkv", DefaultExt = ".mkv", FileName = title + " recording.mkv" }; return picker.ShowDialog(this) == true ? picker.FileName : null; }
        Action("Import IPTV M3U…", async () => { var picker = new OpenFileDialog { Filter = "IPTV playlists|*.m3u;*.m3u8" }; if (picker.ShowDialog(this) == true) { await vm.Television.ImportM3u(picker.FileName, OperationToken); await Refresh(); } });
        Action("Import XMLTV guide…", async () => { var picker = new OpenFileDialog { Filter = "XMLTV guide|*.xml;*.xmltv" }; if (picker.ShowDialog(this) == true) { await vm.Television.ImportGuide(picker.FileName, OperationToken); await Refresh(); } });
        Action("New channel…", async () => { if (new TvChannelEditor(this, vm, null).ShowDialog() == true) await Refresh(); });
        Action("Edit channel…", async () => { if (new TvChannelEditor(this, vm, Selected()).ShowDialog() == true) await Refresh(); });
        Action("Play live channel", () => { var item = Selected(); vm.AddCatalogTracks([new(item.Source, item.Name)], true); return Task.CompletedTask; });
        Action("Record stream now…", async () => { var item = Selected(); string? path = Destination("Channel"); if (path == null) return; int length = CinemaText.Integer(seconds.Text, 1, 14400); var start = DateTimeOffset.UtcNow; Status.Text = "Recording a new Matroska stream copy…"; await CaptureService.SaveStream(item.Source, path, length, OperationToken); await vm.Television.AddRecording(new(Guid.NewGuid().ToString("N"), item.Id, item.Name, path, start, length), OperationToken); await Refresh(); });
        Action("Schedule recording…", async () =>
        {
            var item = Selected(); int length = CinemaText.Integer(seconds.Text, 1, 14400); DateTimeOffset start;
            if (guide.SelectedItem is TvProgramme programme) { start = programme.StartUtc; length = programme.DurationSeconds; }
            else if (!DateTimeOffset.TryParse(due.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) throw new ArgumentException("Use an ISO date/time with an explicit offset.");
            if (start <= DateTimeOffset.UtcNow) throw new ArgumentException("Schedule a future start time."); string? path = Destination("Scheduled channel"); if (path == null) return;
            await vm.Television.Schedule(new(Guid.NewGuid().ToString("N"), item.Id, start.ToUniversalTime(), length, path), OperationToken); await Refresh(); Status.Text = "Schedule saved. Arm the recorder explicitly and keep the application open.";
        });
        Action("Arm recording scheduler", () => { vm.TelevisionRecorder.Arm(); Status.Text = "Scheduler armed for this session. One recording at a time; starts more than two minutes late are marked Missed."; return Task.CompletedTask; });
        Action("Stop recording scheduler", async () => { await vm.TelevisionRecorder.Disarm(); await Refresh(); });
        Action("Cancel selected schedule", async () => { if (tasks.SelectedItem is not TvRecordingTask task || task.State != "Armed") throw new InvalidOperationException("Choose an armed schedule. Stop the scheduler to cancel active recording."); await vm.Television.CancelArmed(task.Id, OperationToken); await Refresh(); });
        Action("Play selected recording", () => { var recording = recordings.SelectedItem as TvRecording ?? throw new InvalidOperationException("Choose a recording."); vm.AddCatalogTracks([new(recording.Path, recording.Title)], true); return Task.CompletedTask; });
        Action("Start timeshift buffer", () => { buffer.Start(Selected().Source); Status.Text = "Buffer started. Refresh after completed segments are available; select a buffered segment to play it while capture continues."; return Task.CompletedTask; });
        Action("Stop timeshift buffer", async () => { await buffer.Stop(); await Refresh(); });
        Action("Play selected buffered segment", () => { var path = bufferRows.SelectedItem as string ?? throw new InvalidOperationException("Refresh and select a completed segment."); vm.AddCatalogTracks([new(path, "Buffered television")], true); return Task.CompletedTask; });
        Action("Refresh", Refresh);
        CinemaUi.GroupActions(Actions, ("Sources and guide", ["Import IPTV M3U…", "Import XMLTV guide…", "New channel…", "Edit channel…"]), ("Recording", ["Record stream now…", "Schedule recording…", "Arm recording scheduler", "Stop recording scheduler", "Cancel selected schedule", "Play selected recording"]), ("Timeshift", ["Start timeshift buffer", "Stop timeshift buffer", "Play selected buffered segment"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Loaded += async (_, _) => await Run(Refresh); Closed += async (_, _) => await buffer.Stop();
    }
}
public sealed class TvChannelEditor : CinemaEditor
{
    public TvChannelEditor(Window owner, MainViewModel vm, TvChannel? existing) : base(owner, vm, "Television channel", "Maintain a channel name, explicit stream source, group and guide identifier.")
    {
        var item = existing ?? new TvChannel(Guid.NewGuid().ToString("N"), "", "", "", ""); var name = Text("Channel name", item.Name, 160); var source = Text("Stream URL", item.Source, 8192); var group = Text("Group", item.Group, 160); var guide = Text("XMLTV channel identifier", item.GuideId, 160);
        Action("Save channel", async () => { await vm.Television.SaveChannel(item with { Name = name.Text.Trim(), Source = source.Text.Trim(), Group = group.Text.Trim(), GuideId = guide.Text.Trim() }, OperationToken); if (!IsClosed) DialogResult = true; });
    }
}
