using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class ExternalControlWindow : FeatureWorkbench
{
    public ExternalControlWindow(Window owner, MainViewModel vm) : base(owner, vm, "Local external control", "Explicitly start current-user named-pipe or loopback TCP/HTTP/WebSocket endpoints. A fresh token is required. No listener starts automatically. Untested implementation.")
    {
        var pipe = Toggle("Current-user named pipe", true); var tcp = Toggle("Loopback TCP JSON lines"); var http = Toggle("Loopback HTTP JSON-RPC and WebSocket");
        var tcpPort = Text("TCP port", "18764", 5); var httpPort = Text("HTTP port", "18765", 5);
        var pipeName = Text("Profile pipe name", vm.LocalControl.PipeName); pipeName.IsReadOnly = true;
        var key = Text("Session token (changes whenever listeners restart)", vm.LocalControl.Token, 128); key.IsReadOnly = true;
        var journal = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Padding = new(12) }; View.Content = journal;
        WindowMessageControl? messages = null;
        Action("Start selected listeners", async () => { await vm.LocalControl.Start(new(pipe.IsChecked == true, tcp.IsChecked == true, http.IsChecked == true, CinemaText.Integer(tcpPort.Text, 1024, 65535), CinemaText.Integer(httpPort.Text, 1024, 65535))); if (!IsClosed) { key.Text = vm.LocalControl.Token; Status.Text = "Listeners active. HTTP /state, /rpc and /ws require Authorization: Bearer <token>. Browser Origin requests are rejected."; } });
        Action("Stop listeners", async () => { await vm.LocalControl.Stop(); if (!IsClosed) { key.Clear(); Status.Text = "Listeners stopped."; } });
        Action("Copy session token", () => { if (vm.LocalControl.Token == "") throw new InvalidOperationException("Start listeners first."); Clipboard.SetText(vm.LocalControl.Token); return Task.CompletedTask; });
        Action("Refresh event journal", () => { journal.Text = string.Join(Environment.NewLine, vm.LocalControl.Events().Select(e => System.Text.Json.JsonSerializer.Serialize(e))); return Task.CompletedTask; });
        Action("Export new event log…", async () => { var picker = new SaveFileDialog { Filter = "Control event JSON lines|*.jsonl", FileName = "FluxPlayer-control-events.jsonl" }; if (picker.ShowDialog(this) == true) await vm.LocalControl.ExportJournal(picker.FileName, OperationToken); });
        Action("Enable / disable window messages", () => { if (messages != null) { messages.Dispose(); messages = null; Status.Text = "Window transport messages disabled."; } else { messages = new(owner, vm); Status.Text = $"Window message {messages.Message}: wParam 1 toggle, 2 stop, 3 next, 4 previous; lParam must be zero. This local desktop channel does not use the API token."; } return Task.CompletedTask; });
        Closed += (_, _) => messages?.Dispose();
        Status.Text = "Four shared connection slots; 64 KiB requests; bounded 256-event memory journal. Closing this panel leaves explicitly started API listeners running until Stop or app shutdown. Window-message hooks close with the panel.";
    }
}

public sealed class AutomaticProfilesWindow : FeatureWorkbench
{
    public AutomaticProfilesWindow(Window owner, MainViewModel vm) : base(owner, vm, "Automatic media profiles", "Apply the first matching extension rule when opening media. Order rules by priority, then activate the saved snapshot explicitly for this session.")
    {
        var name = Text("Rule name", "", 80); var extensions = Text("Extensions (space-separated, such as .flac .wav)", "", 512); var mode = Choice("Playback mode", ["Keep", "Automatic", "Audio", "Video"]); var volume = Text("Volume (−1 keeps current; 0–100 sets level)", "-1", 12); var speed = Text("Speed (0 keeps current; 0.5–2 sets rate)", "0", 12); CinemaUi.Compact(Form, 3);
        var table = Table(("Rule", "Name", 2), ("Extensions", "Formats", 3), ("Mode", "Mode", 1), ("Volume", "Volume", 1), ("Speed", "Rate", 1)); table.SelectionMode = DataGridSelectionMode.Single; AutomaticMediaRule? selected = null;
        async Task Refresh() { var data = await vm.AutomaticProfiles.Snapshot(OperationToken); if (!IsClosed) table.ItemsSource = data.Rules; }
        table.SelectionChanged += (_, _) => { selected = table.SelectedItem as AutomaticMediaRule; if (selected == null) return; name.Text = selected.Name; extensions.Text = string.Join(' ', selected.Extensions); mode.SelectedItem = selected.Mode; volume.Text = selected.Volume.ToString(CultureInfo.InvariantCulture); speed.Text = selected.Rate.ToString(CultureInfo.InvariantCulture); };
        Action("New rule", () => { selected = null; table.SelectedItem = null; name.Clear(); extensions.Clear(); return Task.CompletedTask; });
        Action("Save rule", async () => { if (!double.TryParse(volume.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.TryParse(speed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)) throw new ArgumentException("Use decimal-point numbers."); await vm.AutomaticProfiles.Save(new(selected?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), extensions.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(e => e.ToLowerInvariant()).Distinct().ToImmutableArray(), (string)mode.SelectedItem, v, rate), OperationToken); await Refresh(); Status.Text = "Saved. Activate/reload rules to use the new snapshot."; });
        foreach (var move in new[] { (Name: "Move rule up", Direction: -1), (Name: "Move rule down", Direction: 1) })
            Action(move.Name, async () => { string id = selected?.Id ?? throw new ArgumentException("Select a rule first."); await vm.AutomaticProfiles.Move(id, move.Direction, OperationToken); await Refresh(); if (!IsClosed) table.SelectedItem = table.Items.Cast<AutomaticMediaRule>().FirstOrDefault(r => r.Id == id); Status.Text = "Priority saved. Activate/reload rules to apply it."; });
        Action("Delete rule", async () => { string id = selected?.Id ?? throw new ArgumentException("Select a rule first."); await vm.AutomaticProfiles.Remove(id, OperationToken); await Refresh(); Status.Text = "Rule deleted. Activate/reload rules to apply the saved snapshot."; });
        Action("Activate / reload rules", async () => { await vm.ActivateAutomaticProfiles(true, OperationToken); if (!IsClosed) Status.Text = "Rules active for subsequent media opens in this session. Manual settings can still be changed after opening."; });
        Action("Disable rules", async () => { await vm.ActivateAutomaticProfiles(false, OperationToken); Status.Text = "Automatic rules disabled."; });
        Loaded += async (_, _) => await Run(Refresh);
    }
}

public sealed class ExperienceWindow : FeatureWorkbench
{
    public ExperienceWindow(Window owner, MainViewModel vm, Func<ExperienceSettings, Task> apply) : base(owner, vm, "Appearance and window docking", "Customize the Fluent accent, idle-video backdrop and equalizer docking. Accent text adapts to the active theme for readability.")
    {
        UseFormLayout(720); var saved = vm.CurrentSettings.Experience ?? new();
        var accent = Text("Accent (#RRGGBB; empty restores theme default)", saved.Accent, 7); var background = Text("Local idle-video background image (empty removes it)", saved.Background, 32767); var opacity = Text("Background opacity (0.05–0.5)", saved.BackgroundOpacity.ToString(CultureInfo.InvariantCulture), 12); var dock = Toggle("Keep the equalizer attached beside the player", saved.DockEqualizer); var side = Choice("Dock side", ["Right", "Left"], saved.DockSide == "Left" ? 1 : 0);
        Action("Choose background…", () => { var picker = new OpenFileDialog { Filter = "Local image|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.heic;*.heif" }; if (picker.ShowDialog(this) == true) background.Text = picker.FileName; return Task.CompletedTask; });
        Action("Save and apply", async () =>
        {
            if (!double.TryParse(opacity.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) throw new ArgumentException("Use a decimal-point opacity.");
            var settings = new ExperienceSettings(accent.Text.Trim(), background.Text.Trim(), value, dock.IsChecked == true, (string)side.SelectedItem); settings.Validate();
            if (settings.Background != "") { if (!System.IO.File.Exists(settings.Background)) throw new System.IO.FileNotFoundException("Choose an available local background image."); await new CinemaArtwork().Load(settings.Background, 1600, OperationToken); }
            OperationToken.ThrowIfCancellationRequested(); var previous = vm.CurrentSettings.Experience ?? new();
            if (!vm.SaveExperience(settings)) throw new System.IO.IOException("Unable to save appearance preferences.");
            try { await apply(settings); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!vm.SaveExperience(previous)) throw new System.IO.IOException("Unable to apply appearance or restore saved preferences.", ex);
                try { await apply(previous); } catch (Exception recovery) when (recovery is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine("Appearance restoration: " + recovery.Message); }
                throw;
            }
            if (!IsClosed) Status.Text = "Preferences saved and applied.";
        });
        Status.Text = "The background is shown only on the empty video canvas. Native selection/focus accent resources are changed; existing branded gradients remain. Docking fits the equalizer within the owner's monitor work area.";
    }
}
