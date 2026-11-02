using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class InteropWindow : RemoteSourceWorkbench
{
    public InteropWindow(Window owner, MainViewModel vm, bool kodi) : base(owner, vm, kodi ? "Kodi media-center integration" : "MPD network-player integration", "Advanced session control for an existing external media server/player. Choose a purpose and operation. Requests run only on explicit action; this panel does not replace the main LibVLC playback session.")
    {
        string provider = kodi ? "Kodi" : "MPD"; var catalog = kodi ? InteropCatalog.Kodi : InteropCatalog.Mpd;
        var connection = Section("Session connection", () => (Address: Text(kodi ? "Kodi HTTPS root (HTTP on loopback only)" : "MPD host", kodi ? "http://localhost:8080/" : "localhost", 4096), Port: Text("MPD port", "6600", 5), User: Text("Kodi username", "", 256), Password: Field("Session password", new PasswordBox { MaxLength = 2048 }), Tls: Toggle("Use certificate-validating TLS for MPD", false)), true);
        ((FrameworkElement)connection.Port.Parent).Visibility = connection.Tls.Visibility = kodi ? Visibility.Collapsed : Visibility.Visible; ((FrameworkElement)connection.User.Parent).Visibility = kodi ? Visibility.Visible : Visibility.Collapsed;
        var operation = Section("Operation", () => (Group: Choice("Purpose", catalog.Select(a => a.Group).Distinct()), Action: Field("Action", new ComboBox { DisplayMemberPath = "Name" })), true);
        var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; Form.Children.Add(explanation);
        var parameters = MasteringFields.Multiline(Text(kodi ? "Named parameters (JSON object)" : "Ordered arguments (JSON array of strings)", kodi ? "{}" : "[]", 65536), 150);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12), Text = kodi ? "Optional parameters can be added using the reference shown with each action. The server enforces its installed API version and permissions." : "Each argument is separately quoted by the client. Use the displayed command signature; ranges and filter expressions are single string arguments. Credentials and control characters cannot be entered as commands." }; Form.Children.Add(info);
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(12), FontFamily = new("Cascadia Mono") }; System.Windows.Automation.AutomationProperties.SetName(output, "Operation result"); View.Content = output;
        KodiConnection? kodiClient = null; MpdConnection? mpd = null; InteropResult? report = null; string connectedTarget = ""; Button? run = null;
        InteropAction Selected() => operation.Action.SelectedItem as InteropAction ?? throw new ArgumentException("Choose an operation.");
        void Choose()
        {
            if (operation.Action.SelectedItem is not InteropAction selected) return; parameters.Text = kodi ? InteropCatalog.KodiTemplate(selected) : "[]";
            string fields = kodi && selected.Schema.TryGetProperty("params", out var declared) ? string.Join(" · ", declared.EnumerateArray().Select(p => p.GetProperty("name").GetString() + (p.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True ? " (required)" : " (optional)"))) : selected.Name;
            explanation.Text = (selected.Writes ? "Changes the external player/server. " : "Read-only request. ") + selected.Key + "\n" + fields; if (run != null) run.Content = selected.Writes ? "Review and apply…" : "Read current data";
        }
        void Filter() { operation.Action.ItemsSource = catalog.Where(a => a.Group == (string)operation.Group.SelectedItem).ToArray(); operation.Action.SelectedIndex = 0; }
        operation.Group.SelectionChanged += (_, _) => Filter(); operation.Action.SelectionChanged += (_, _) => Choose(); Filter();
        async Task Disconnect() { kodiClient?.Dispose(); kodiClient = null; if (mpd != null) { await mpd.DisposeAsync(); mpd = null; } connectedTarget = ""; }
        Action("Connect", async () =>
        {
            await Disconnect(); report = null; output.Clear(); string address = connection.Address.Text.Trim(), username = connection.User.Text, password = connection.Password.Password; bool tls = connection.Tls.IsChecked == true;
            try
            {
                if (kodi) { kodiClient = new KodiConnection(address, username, password); _ = await kodiClient.Execute("JSONRPC.Version", InteropCatalog.ParseInput("{}"), OperationToken); }
                else { int port = MasteringFields.Integers(connection.Port.Text, 1, 1, 65535)[0]; mpd = new MpdConnection(); await mpd.Connect(address, port, tls, password, OperationToken); }
                if (IsClosed) { await Disconnect(); return; } connectedTarget = address; connection.Password.Clear(); Status.Text = "Connected to " + connectedTarget + ". Requests remain explicit; no automatic polling.";
            }
            catch { await Disconnect(); throw; }
        });
        Action("Disconnect", async () => { await Disconnect(); connection.Password.Clear(); Status.Text = "Session disconnected."; });
        Action("Reset parameter template", () => { Choose(); return Task.CompletedTask; });
        run = Action("Read current data", async () =>
        {
            var selected = Selected(); var input = InteropCatalog.ParseInput(parameters.Text); if (kodi) InteropCatalog.ValidateKodi(selected, input);
            if (connectedTarget == "") throw new ArgumentException("Connect first.");
            string consequence = selected.Key.StartsWith("System.", StringComparison.Ordinal) ? "This requests a power/session action on the Kodi target." : selected.Key.Contains(".Remove", StringComparison.Ordinal) || selected.Key.Contains(".Delete", StringComparison.Ordinal) || selected.Key.EndsWith(".Clean", StringComparison.Ordinal) ? "This removes or cleans the selected remote records/data. Review the identifiers carefully." : selected.Key == "Addons.ExecuteAddon" ? "This runs the specified installed addon on the Kodi target." : "This changes the external server/player state.";
            if (selected.Writes && MessageBox.Show(this, provider + " · " + connectedTarget + "\n\n" + selected.Key + "\n" + ServerActionCatalog.Bound(input.ToString(), 2000) + "\n\n" + consequence + " Apply this request?", "Review external change", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            report = null; output.Clear();
            try
            {
                JsonElement result = kodi ? await (kodiClient ?? throw new ArgumentException("Reconnect Kodi.")).Execute(selected.Key, input, OperationToken) : await (mpd ?? throw new ArgumentException("Reconnect MPD.")).Execute(selected.Key, input, OperationToken);
                if (IsClosed) return; report = new(provider, selected.Key, DateTimeOffset.UtcNow, result); string rendered = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }); output.Text = ServerActionCatalog.Bound(rendered, 128 * 1024); Status.Text = "Request completed. Display is capped at 128 KiB; report export retains the bounded response. This is not independent verification of a remote change.";
            }
            catch (Exception ex) when (selected.Writes && ex is not OutOfMemoryException) { if (!IsClosed) Status.Text = "The remote result is uncertain; changes may have applied. Inspect the target before deliberately retrying. " + (ex is OperationCanceledException ? "Request canceled." : ex.Message); }
        });
        Choose();
        Action("Export result…", async () => { var current = report ?? throw new ArgumentException("Complete an operation first."); string? path = SavePath(this, provider + " result.json", ".json"); if (path != null) await InteropCatalog.Export(current, path, OperationToken); });
        CinemaUi.GroupActions(Actions, ("Session", ["Connect", "Disconnect"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += async (_, _) => { connection.Password.Clear(); try { await Disconnect(); } catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine("Interop cleanup: " + ex.Message); } }; Status.Text = "No external connection is opened until Connect. Parameters are advanced API data, never shell commands.";
    }
}
