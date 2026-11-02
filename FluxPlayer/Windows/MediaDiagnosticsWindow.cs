using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class MediaDiagnosticsWindow : RemoteSourceWorkbench
{
    public MediaDiagnosticsWindow(Window owner, MainViewModel vm, bool audio) : base(owner, vm, audio ? "Audio analysis and visualizations" : "Video quality diagnostics", "Inspect a chosen local excerpt. Reports and visualizations run only on request; results describe that excerpt, not the complete file.")
    {
        var input = Section("Source and excerpt", () => (Source: Text("Local source", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767), Start: Text("Start (seconds)", "0", 20), Seconds: Text("Duration (0.1–60 seconds; animated visualizations up to 30)", "20", 20)), true);
        var kind = Field("Analysis", new ComboBox { ItemsSource = MediaDiagnostics.Kinds.Where(k => k.Audio == audio).ToArray(), DisplayMemberPath = "Name", SelectedIndex = 0 });
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; Form.Children.Add(detail);
        void Detail() { if (kind.SelectedItem is DiagnosticKind k) detail.Text = k.Description; }
        kind.SelectionChanged += (_, _) => Detail(); Detail(); CinemaUi.Compact(Form, 2);
        var table = Table(("Line", "Number", 1), ("Diagnostic / event", "Detail", 12)); DiagnosticReport? report = null;
        Action("Choose source…", () => { string? path = OpenPath(this, FluxPlayer.Models.MediaFormats.FileDialogFilter); if (path != null) input.Source.Text = path; return Task.CompletedTask; });
        Action("Analyze excerpt", async () =>
        {
            var selected = kind.SelectedItem as DiagnosticKind ?? throw new ArgumentException("Choose an analysis."); if (selected.Extension != ".json") throw new ArgumentException("This view produces a file. Choose Export visual / manifest.");
            string source = input.Source.Text.Trim(); double start = MasteringFields.Numbers(input.Start.Text, 1, 0, 86400)[0], seconds = MasteringFields.Numbers(input.Seconds.Text, 1, .1, 60)[0];
            Status.Text = "Analyzing selected excerpt…"; var value = await MediaDiagnostics.Analyze(selected.Id, source, start, seconds, OperationToken); if (!IsClosed) { report = value; table.ItemsSource = value.Lines; Status.Text = value.Scope; }
        });
        Action("Export current report…", async () => { var current = report ?? throw new ArgumentException("Analyze an excerpt first."); string? path = SavePath(this, "Excerpt analysis.json", ".json"); if (path != null) { await MediaDiagnostics.ExportReport(current, path, OperationToken); if (!IsClosed) Status.Text = "Saved the displayed analysis, including its source, interval and scope."; } });
        Action("Export visual / manifest…", async () =>
        {
            var selected = kind.SelectedItem as DiagnosticKind ?? throw new ArgumentException("Choose a visualization."); if (selected.Extension == ".json") throw new ArgumentException("Choose Analyze excerpt for a report.");
            string source = input.Source.Text.Trim(); double start = MasteringFields.Numbers(input.Start.Text, 1, 0, 86400)[0], seconds = MasteringFields.Numbers(input.Seconds.Text, 1, .1, selected.Extension == ".mp4" ? 30 : 60)[0];
            string? path = SavePath(this, "Excerpt visualization" + selected.Extension, selected.Extension); if (path == null) return; Status.Text = "Exporting selected visualization…"; await MediaDiagnostics.ExportVisual(selected.Id, source, path, start, seconds, OperationToken); if (!IsClosed) Status.Text = "Saved a new " + selected.Name + " file.";
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Status.Text = "Select an analysis. Reports retain a bounded tail; scene, noise and quality indicators are heuristic.";
    }
}
