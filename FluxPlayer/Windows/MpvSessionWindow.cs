using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class MpvSessionWindow : RemoteSourceWorkbench
{
    public MpvSessionWindow(Window owner, MainViewModel vm) : base(owner, vm, "Alternate mpv playback session", "Control an explicitly launched, separately installed mpv player. Its video uses its own native window; this does not replace FluxPlayer's main LibVLC session. Driver, renderer, HDR and output compatibility remain conditional.")
    {
        var executable = Text("Trusted installed mpv.exe", "", 32767);
        var select = Section("Session controls", () => (Group: Choice("Category", MpvActionCatalog.Actions.Select(a => a.Group).Distinct()), Action: Field("Control", new ComboBox { DisplayMemberPath = "Name" })), true);
        var value = Text("Value", "", 32768); var flag = Toggle("Enabled"); var choices = Field("Choice", new ComboBox());
        var scope = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; Form.Children.Add(scope); CinemaUi.Compact(Form, 2);
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(12), FontFamily = new("Cascadia Mono") }; System.Windows.Automation.AutomationProperties.SetName(output, "mpv session response"); View.Content = output;
        var session = new MpvSession(); InteropResult? report = null; Button? stage = null, apply = null, read = null;
        MpvAction Selected() => select.Action.SelectedItem as MpvAction ?? throw new ArgumentException("Choose a session control.");
        void Choose()
        {
            if (select.Action.SelectedItem is not MpvAction action) return; value.Text = action.Default; flag.IsChecked = action.Default == "true"; choices.ItemsSource = action.Choices; choices.SelectedItem = action.Default;
            System.Windows.Automation.AutomationProperties.SetName(value, action.Name + " value"); System.Windows.Automation.AutomationProperties.SetName(flag, action.Name); System.Windows.Automation.AutomationProperties.SetName(choices, action.Name + " choice");
            string unit = action.Key is "seek-absolute" or "seek-relative" or "audio-delay" or "sub-delay" or "secondary-sub-delay" ? " (seconds)" : action.Key == "volume" ? " (percent)" : action.Kind == "color" ? " (#RRGGBB or #RRGGBBAA)" : "";
            if (value.Parent is StackPanel field && field.Children[0] is TextBlock label) label.Text = "Value" + unit;
            if (stage != null) stage.IsEnabled = action.Mode == "startup"; if (apply != null) apply.IsEnabled = action.Mode != "startup"; if (read != null) read.IsEnabled = action.Mode == "property";
            ((FrameworkElement)value.Parent).Visibility = action.Kind is "none" or "bool" or "choice" ? Visibility.Collapsed : Visibility.Visible; flag.Visibility = action.Kind == "bool" ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)choices.Parent).Visibility = action.Kind == "choice" ? Visibility.Visible : Visibility.Collapsed;
            scope.Text = action.Mode == "startup" ? "Startup option: stage it, close the current mpv session and start a new session to apply. No running output/device is changed by staging." : action.Mode == "property" ? "Property change applies only to this owned mpv session. Read current value to inspect the backend response." : "Explicit command for this owned mpv session. Queue occurrences are zero-based; track and edition IDs come from the session snapshot.";
            if (action.Kind is "number" or "integer" or "pair") scope.Text += $" Allowed range: {action.Minimum}–{action.Maximum}.";
        }
        void Filter() { select.Action.ItemsSource = MpvActionCatalog.Actions.Where(a => a.Group == (string)select.Group.SelectedItem).ToArray(); select.Action.SelectedIndex = 0; }
        select.Group.SelectionChanged += (_, _) => Filter(); select.Action.SelectionChanged += (_, _) => Choose(); Filter();
        string Input() => Selected().Kind == "bool" ? (flag.IsChecked == true ? "true" : "false") : Selected().Kind == "choice" ? choices.SelectedItem as string ?? "" : value.Text;
        void Display(string key, JsonElement data) { if (IsClosed) return; report = new("mpv", key, DateTimeOffset.UtcNow, data); output.Text = ServerActionCatalog.Bound(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), 128 * 1024); }
        Action("Choose mpv.exe…", () => { string? path = OpenPath(this, "Installed mpv|mpv.exe"); if (path != null) executable.Text = path; return Task.CompletedTask; });
        Action("Start alternate session", async () => { await session.Start(executable.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Owned mpv session started idle and paused. Choose Open local media or Open URL, then Pause/resume. Main playback remains independent."; });
        Action("Choose local input…", () => { if (Selected().Kind != "path") throw new ArgumentException("Choose a file-loading or external-track action first."); string? path = OpenPath(this, "Media and subtitle files|*.*"); if (path != null) value.Text = path; return Task.CompletedTask; });
        stage = Action("Stage startup option", () => { session.Stage(Selected(), Input()); Status.Text = "Option staged for the next mpv launch in this panel. Close and restart the session to apply it."; return Task.CompletedTask; });
        apply = Action("Apply session control", async () => { var action = Selected(); var data = await session.Execute(action.Key, Input(), false, OperationToken); Display(action.Key, data); if (!IsClosed) Status.Text = "mpv accepted the command/property. Inspect current values or the output; acceptance is not hardware or image-quality verification."; });
        read = Action("Read current value", async () => { var action = Selected(); Display(action.Key, await session.Execute(action.Key, "", true, OperationToken)); if (!IsClosed) Status.Text = "Read the selected mpv property."; });
        Action("Read tracks, queue and devices", async () => { Display("snapshot", await session.Snapshot(OperationToken)); if (!IsClosed) Status.Text = "Snapshot loaded. Unavailable properties report errors independently; no polling timer is active."; });
        Action("Close owned session", async () => { await session.Stop(); report = null; output.Clear(); Status.Text = "Owned session closed. Staged startup choices are retained in this panel for the next explicit launch."; });
        Action("Export response…", async () => { var current = report ?? throw new ArgumentException("Read or apply a session control first."); string? path = SavePath(this, "mpv response.json", ".json"); if (path != null) await InteropCatalog.Export(current, path, OperationToken); });
        Choose(); CinemaUi.GroupActions(Actions, ("Sources", ["Choose mpv.exe…", "Choose local input…"]), ("Inspect", ["Read tracks, queue and devices", "Export response…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += async (_, _) => { try { await session.DisposeAsync(); } catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine("mpv cleanup: " + ex.Message); } }; Status.Text = "No process or IPC listener is created until Start alternate session. Install mpv separately and choose its trusted executable.";
    }
}
