using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class SubtitleWorkshopWindow : RemoteSourceWorkbench
{
    private sealed record CueRow(int Number, SubtitleCue Cue)
    { public string Start => SubtitleEditingService.FormatTime(Cue.StartMs); public string End => SubtitleEditingService.FormatTime(Cue.EndMs); public string Text => Cue.Text; }
    public SubtitleWorkshopWindow(Window owner, MainViewModel vm) : base(owner, vm, "Subtitle quality and cleanup", "Review SRT/WebVTT text cues, audit readability and apply document edits with Undo. Export creates a new copy; WebVTT styling, regions and cue settings are not preserved.")
    {
        ImmutableArray<SubtitleCue> cues = []; ImmutableArray<SubtitleFinding> findings = []; var undo = new List<ImmutableArray<SubtitleCue>>(); var redo = new List<ImmutableArray<SubtitleCue>>();
        var source = Text("Loaded document", "", 32767); source.IsReadOnly = true;
        var selection = Section("Action", () => (Type: Choice("Category", ["Quality review", "Text cleanup", "Timing and structure"]), Action: Field("Operation", new ComboBox { DisplayMemberPath = "Name" })), true);
        string Category(SubtitleWorkshopAction action) => action.Audit ? "Quality review" : Array.IndexOf(SubtitleWorkshop.Actions.ToArray(), action) < 20 ? "Text cleanup" : "Timing and structure";
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; Form.Children.Add(detail);
        void Filter() { selection.Action.ItemsSource = SubtitleWorkshop.Actions.Where(a => Category(a) == (string)selection.Type.SelectedItem).ToArray(); selection.Action.SelectedIndex = 0; }
        void Detail() { if (selection.Action.SelectedItem is SubtitleWorkshopAction action) detail.Text = action.Description; }
        selection.Type.SelectionChanged += (_, _) => Filter(); selection.Action.SelectionChanged += (_, _) => Detail(); Filter();
        var timing = Section("Readability and timing limits", () => (Minimum: Text("Minimum exposure (ms)", "800", 10), Maximum: Text("Maximum exposure (ms)", "7000", 10), Speed: Text("Characters per second", "20", 12), Columns: Text("Columns per line", "42", 10), Gap: Text("Gap threshold (ms)", "100", 10)));
        var interval = Section("Interval extraction and frame grid", () => (Start: Text("Interval start (ms)", "0", 16), End: Text("Interval end (ms)", "60000", 16), Fps: Text("Frames per second (for example 23.976)", "25", 16)));
        var edit = Section("Manual correction of the selected cue", () => (Start: Text("Cue start (ms)", "0", 16), End: Text("Cue end (ms)", "1000", 16), Text: MasteringFields.Multiline(Text("Cue text (line breaks preserved)", "", 8192), 100)));
        CinemaUi.Compact(Form, 2);
        var table = Table(("#", "Number", 1), ("Start", "Start", 2), ("End", "End", 2), ("Caption", "Text", 8));
        var findingTable = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 36, GridLinesVisibility = DataGridGridLinesVisibility.None };
        foreach (var (title, path, width) in new[] { ("Cue", "Cue", 1d), ("Issue", "Issue", 3d), ("Caption", "Text", 8d) }) findingTable.Columns.Add(new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding(path), Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
        View.Content = null; var tabs = new TabControl(); var cueTab = new TabItem { Header = "Captions", Content = table }; tabs.Items.Add(cueTab); tabs.Items.Add(new TabItem { Header = "Findings", Content = findingTable }); View.Content = tabs;
        void Show() { table.ItemsSource = cues.Select((c, i) => new CueRow(i + 1, c)).ToArray(); findings = []; findingTable.ItemsSource = findings; }
        long Memory(ImmutableArray<SubtitleCue> document) => document.Sum(c => 128L + c.Text.Length * 2L);
        void BoundHistory() { while (undo.Count + redo.Count > 8 || undo.Sum(Memory) + redo.Sum(Memory) > 32L * 1024 * 1024) { if (undo.Count > 0) undo.RemoveAt(0); else if (redo.Count > 0) redo.RemoveAt(0); else break; } }
        void Commit(ImmutableArray<SubtitleCue> updated) { SubtitleWorkshop.ValidateDocument(updated); undo.Add(cues); redo.Clear(); cues = updated; BoundHistory(); Show(); tabs.SelectedItem = cueTab; Status.Text = $"{cues.Length:N0} cues in the edited document. Export to save a new copy."; }
        double Number(TextBox box, double min, double max) => MasteringFields.Numbers(box.Text, 1, min, max)[0];
        int Integer(TextBox box, int min, int max) => MasteringFields.Integers(box.Text, 1, min, max)[0];
        long Milliseconds(TextBox box) { double value = Number(box, 0, FluxPlayer.Models.SubtitleTiming.MaximumTimeMs); if (value != Math.Truncate(value)) throw new ArgumentException("Use whole millisecond timestamps."); return (long)value; }
        SubtitleWorkshopOptions Options() => new(Integer(timing.Minimum, 1, 30000), Integer(timing.Maximum, 1, 60000), Number(timing.Speed, 1, 100), Integer(timing.Columns, 10, 120), Integer(timing.Gap, 0, 10000), Milliseconds(interval.Start), Milliseconds(interval.End), Number(interval.Fps, 1, 120));
        table.SelectionChanged += (_, _) => { if (table.SelectedItem is CueRow row) { edit.Start.Text = row.Cue.StartMs.ToString(System.Globalization.CultureInfo.InvariantCulture); edit.End.Text = row.Cue.EndMs.ToString(System.Globalization.CultureInfo.InvariantCulture); edit.Text.Text = row.Cue.Text; } };
        Action("Open subtitles…", async () =>
        {
            if (undo.Count > 0 && MessageBox.Show(this, "Discard the current in-memory edits and open another document?", "Open subtitles", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            string? path = OpenPath(this, "Text subtitles|*.srt;*.vtt"); if (path == null) return; var loaded = await SubtitleWorkshop.Read(path, OperationToken); if (IsClosed) return; cues = loaded; undo.Clear(); redo.Clear(); source.Text = path; Show(); Status.Text = $"Loaded {cues.Length:N0} cues. Original subtitle file remains unchanged.";
        });
        Action("Run quality review", async () =>
        {
            var action = selection.Action.SelectedItem as SubtitleWorkshopAction ?? throw new ArgumentException("Choose an operation."); if (!action.Audit) throw new ArgumentException("Choose a Quality review operation."); var current = cues; var options = Options();
            var value = await Task.Run(() => SubtitleWorkshop.Audit(action.Id, current, options, OperationToken), OperationToken); if (IsClosed) return; findings = value; findingTable.ItemsSource = findings; tabs.SelectedIndex = 1; Status.Text = $"{findings.Length:N0} findings from {action.Name}. No captions changed.";
        });
        Action("Apply document edit", async () =>
        {
            var action = selection.Action.SelectedItem as SubtitleWorkshopAction ?? throw new ArgumentException("Choose an operation."); if (action.Audit) throw new ArgumentException("Choose a Text cleanup or Timing and structure operation."); var current = cues; var options = Options(); ImmutableArray<SubtitleCue> second = [];
            if (action.Id == "append") { string? path = OpenPath(this, "Second text subtitle|*.srt;*.vtt"); if (path == null) return; second = await SubtitleWorkshop.Read(path, OperationToken); }
            var extra = second; var value = await Task.Run(() => SubtitleWorkshop.Transform(action.Id, current, options, extra, OperationToken), OperationToken); if (!IsClosed) Commit(value);
        });
        Action("Go to finding", () => { var finding = findingTable.SelectedItem as SubtitleFinding ?? throw new ArgumentException("Select a finding."); tabs.SelectedItem = cueTab; table.SelectedIndex = finding.Cue - 1; table.ScrollIntoView(table.SelectedItem); return Task.CompletedTask; });
        Action("Save cue correction", () => { if (table.SelectedItem is not CueRow row) throw new ArgumentException("Select a cue."); var changed = cues.SetItem(row.Number - 1, new(Milliseconds(edit.Start), Milliseconds(edit.End), edit.Text.Text)).OrderBy(c => c.StartMs).ToImmutableArray(); Commit(changed); return Task.CompletedTask; });
        Action("Remove selected cue", () => { if (table.SelectedItem is not CueRow row) throw new ArgumentException("Select a cue."); Commit(cues.RemoveAt(row.Number - 1)); return Task.CompletedTask; });
        Action("Undo", () => { if (undo.Count > 0) { redo.Add(cues); cues = undo[^1]; undo.RemoveAt(undo.Count - 1); Show(); Status.Text = "Last edit undone."; } return Task.CompletedTask; });
        Action("Redo", () => { if (redo.Count > 0) { undo.Add(cues); cues = redo[^1]; redo.RemoveAt(redo.Count - 1); Show(); Status.Text = "Edit restored."; } return Task.CompletedTask; });
        Action("Export subtitle copy…", async () => { var current = cues; string? path = SavePath(this, "Edited captions.srt", ".srt"); if (path == null) return; await SubtitleWorkshop.Export(current, path, OperationToken); if (!IsClosed) Status.Text = "Saved a new subtitle copy. In-memory Undo history remains available."; });
        Action("Export WebVTT copy…", async () => { var current = cues; string? path = SavePath(this, "Edited captions.vtt", ".vtt"); if (path == null) return; await SubtitleWorkshop.Export(current, path, OperationToken); if (!IsClosed) Status.Text = "Saved a new plain-text WebVTT copy."; });
        Action("Export findings…", async () =>
        {
            var current = findings; string sourcePath = source.Text; string? path = SavePath(this, "Subtitle findings.json", ".json"); if (path == null) return;
            string json = JsonSerializer.Serialize(new { Source = sourcePath, CreatedUtc = DateTimeOffset.UtcNow, Findings = current }); if (Encoding.UTF8.GetByteCount(json) > 12 * 1024 * 1024) throw new ArgumentException("The findings report exceeds its bound.");
            await OfflineMediaJob.Publish(path, ".json", [], staging => System.IO.File.WriteAllTextAsync(staging, json, OperationToken), OperationToken); if (!IsClosed) Status.Text = "Saved a new findings report.";
        });
        CinemaUi.GroupActions(Actions, ("Corrections", ["Go to finding", "Save cue correction", "Remove selected cue", "Undo", "Redo"]), ("Export", ["Export subtitle copy…", "Export WebVTT copy…", "Export findings…"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Status.Text = "Open SRT/WebVTT subtitles. Quality reviews flag candidates; document edits remain in memory until exported.";
    }
}
