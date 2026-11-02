using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

/// <summary>Provider-specific categorized operations; no connection or write happens in the constructor.</summary>
public sealed class ServerActionsWindow : RemoteSourceWorkbench
{
    private SubsonicSource? _subsonic;
    private PersonalMediaServer? _personal;
    private UpnpMedia? _upnp;
    private UpnpDevice? _device;
    private bool _connected;
    private string _target = "";
    public ServerActionsWindow(Window owner, MainViewModel vm, string provider) : base(owner, vm, provider == "UPnP" ? "UPnP capabilities and queue settings" : provider + " library actions", "Connect explicitly, choose a category, and read or change selected server data. Credentials remain in this session. Remote changes affect the selected account/device and require their own action.")
    {
        var definitions = ServerActionCatalog.For(provider);
        var server = Section(provider == "UPnP" ? "Selected device" : "Session connection", () => Text(provider == "UPnP" ? "Device description URL" : "Server root", provider == "UPnP" ? "http://" : "https://", 4096), true);
        TextBox? user = null; PasswordBox? secret = null;
        if (provider != "UPnP")
        {
            if (provider != "Plex") user = Text(provider == "Subsonic" ? "Username" : "Optional user ID (empty uses token owner)", "", 256);
            secret = Field(provider == "Subsonic" ? "Session password" : "Session token", new PasswordBox { MaxLength = provider == "Subsonic" ? 1024 : 2048 });
        }
        var target = new TextBlock { Text = "No active connection.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; Form.Children.Add(target);
        var selected = Section("Operation", () => (Group: Choice("Category", definitions.Select(d => d.Group).Distinct()), Operation: Field("Action", new ComboBox { DisplayMemberPath = "Name" })), true);
        var parameterHost = new StackPanel { Margin = new(12, 10, 12, 4) }; Form.Children.Add(new Expander { Header = "Selected action parameters", IsExpanded = true, Content = parameterHost, Margin = new(0, 0, 0, 12) });
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; Form.Children.Add(details);
        var controls = new Dictionary<string, Control>(StringComparer.Ordinal); var draft = new Dictionary<string, string>(StringComparer.Ordinal); Button? readAction = null, changeAction = null;
        string Value(Control control) => control is ComboBox combo ? combo.SelectedItem as string ?? "" : ((TextBox)control).Text;
        void Rebuild()
        {
            foreach (var pair in controls) draft[pair.Key] = Value(pair.Value);
            parameterHost.Children.Clear(); controls.Clear(); if (selected.Operation.SelectedItem is not ServerActionDefinition definition) return;
            details.Text = (definition.Writes ? "Changes server data: " : "Read-only: ") + definition.Scope;
            if (readAction != null) readAction.IsEnabled = !definition.Writes;
            if (changeAction != null) changeAction.IsEnabled = definition.Writes;
            foreach (var field in definition.Fields)
            {
                var panel = new StackPanel { Margin = new(0, 0, 0, 12) }; panel.Children.Add(new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 5) });
                string value = draft.GetValueOrDefault(field.Key, field.Default); Control control;
                if (field.Kind == "choice") control = new ComboBox { ItemsSource = field.Choices, SelectedItem = field.Choices.Contains(value) ? value : field.Default };
                else { var box = new TextBox { Text = value, MaxLength = field.Kind is "ids" or "indices" ? 32768 : field.Kind == "url" ? 4096 : 512, Padding = new(10, 7, 10, 7) }; if (field.Kind is "ids" or "indices") MasteringFields.Multiline(box, 90); control = box; }
                AutomationProperties.SetName(control, field.Label); if (field.Kind == "integer") control.ToolTip = $"Whole number from {field.Minimum} to {field.Maximum}.";
                panel.Children.Add(control); parameterHost.Children.Add(panel); controls.Add(field.Key, control);
            }
            if (definition.Fields.IsEmpty) parameterHost.Children.Add(new TextBlock { Text = "No additional parameters." });
        }
        void Filter() { selected.Operation.ItemsSource = definitions.Where(d => d.Group == (string)selected.Group.SelectedItem).ToArray(); selected.Operation.SelectedIndex = 0; }
        selected.Group.SelectionChanged += (_, _) => Filter(); selected.Operation.SelectionChanged += (_, _) => Rebuild(); Filter(); CinemaUi.Compact(Form, 2);
        var table = Table(("Title / property", "Title", 3), ("ID / key", "Id", 2), ("Occurrence", "Occurrence", 1), ("Type", "Kind", 1), ("Details", "Detail", 5)); ServerActionReport? report = null;
        View.Content = null; var resultLayout = new Grid(); resultLayout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); resultLayout.RowDefinitions.Add(new() { Height = GridLength.Auto }); resultLayout.Children.Add(table);
        var selectedDetail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 10, 0, 0), Padding = new(10) }; AutomationProperties.SetName(selectedDetail, "Selected result details"); Grid.SetRow(selectedDetail, 1); resultLayout.Children.Add(selectedDetail); View.Content = resultLayout;
        table.SelectionChanged += (_, _) => selectedDetail.Text = table.SelectedItem is ServerActionRow row ? row.Title + "\nID: " + row.Id + (row.Occurrence == "" ? "" : " · Occurrence: " + row.Occurrence) + "\n" + row.Detail : "";
        ServerActionDefinition Definition() => selected.Operation.SelectedItem as ServerActionDefinition ?? throw new ArgumentException("Choose an operation.");
        Dictionary<string, string> Inputs() => controls.ToDictionary(p => p.Key, p => Value(p.Value), StringComparer.Ordinal);
        Action("Connect / inspect", async () =>
        {
            Disconnect(); report = null; table.ItemsSource = null; target.Text = "Connecting…";
            string address = server.Text.Trim(), username = user?.Text ?? "", credential = secret?.Password ?? "";
            try
            {
                if (provider == "UPnP") { _upnp = new UpnpMedia(); _device = await _upnp.Inspect(address, OperationToken); _target = _device.Name; }
                else if (provider == "Subsonic") { _subsonic = new SubsonicSource(address, username, credential); await _subsonic.Ping(OperationToken); _target = _subsonic.Server; }
                else { var kind = Enum.Parse<PersonalServerKind>(provider); _personal = new PersonalMediaServer(kind, address, username, credential); await _personal.Connect(OperationToken); _target = RemoteMediaIO.Server(address).ToString(); }
                if (IsClosed) { Disconnect(); return; } _connected = true; secret?.Clear(); target.Text = provider + " session: " + _target; Status.Text = "Session ready. Choose a read operation; each server capability and permission is checked when requested.";
            }
            catch { Disconnect(); if (!IsClosed) target.Text = "No active connection."; throw; }
        });
        Action("Disconnect", () => { Disconnect(); secret?.Clear(); report = null; table.ItemsSource = null; target.Text = "No active connection."; Status.Text = "Session disconnected."; return Task.CompletedTask; });
        async Task<ServerActionRow[]> Execute(ServerActionDefinition definition, Dictionary<string, string> values)
        {
            if (!_connected) throw new ArgumentException("Connect to the selected server/device first.");
            if (_subsonic != null) return await _subsonic.ExecuteAction(definition.Key, values, OperationToken);
            if (_personal != null) return await _personal.ExecuteAction(definition.Key, values, OperationToken);
            if (_upnp != null && _device != null) return await _upnp.ExecuteAction(_device, definition.Key, values, OperationToken);
            throw new ArgumentException("Reconnect to the server/device.");
        }
        void Display(ServerActionDefinition definition, ServerActionRow[] rows)
        {
            if (IsClosed) return; report = new(provider, definition.Key, definition.Scope + " Up to 100 displayed entries; server support/permissions apply.", DateTimeOffset.UtcNow, rows.Take(100).ToImmutableArray()); table.ItemsSource = report.Rows;
        }
        readAction = Action("Read server data", async () =>
        {
            var definition = Definition(); if (definition.Writes) throw new ArgumentException("This operation changes remote data. Use Apply remote change after reviewing its parameters."); var values = ServerActionCatalog.Validate(definition, Inputs());
            var rows = await Execute(definition, values); if (IsClosed) return; Display(definition, rows); Status.Text = $"Loaded {rows.Length} rows (display limit 100). No remote data was changed.";
        });
        changeAction = Action("Apply remote change…", async () =>
        {
            var definition = Definition(); if (!definition.Writes) throw new ArgumentException("This is a read-only operation. Use Read server data."); if (!_connected) throw new ArgumentException("Connect first."); var values = ServerActionCatalog.Validate(definition, Inputs());
            string summary = string.Join("\n", definition.Fields.Select(f => f.Label + ": " + ServerActionCatalog.Bound(values[f.Key], 400)));
            if (MessageBox.Show(this, provider + " · " + _target + "\n\n" + definition.Name + "\n" + definition.Scope + "\n\n" + summary + "\n\nApply this remote change?", "Review remote change", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            report = null; table.ItemsSource = null;
            try { var rows = await Execute(definition, values); if (IsClosed) return; Display(definition, rows); Status.Text = "Server accepted the request. Read its current state to review the result; local library state was not synchronized."; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { if (!IsClosed) Status.Text = "Remote change did not return a confirmed result. It may already have applied. Read the server state before deliberately retrying. " + (ex is OperationCanceledException ? "Request canceled." : ex.Message); }
        });
        ServerActionRow Row() => table.SelectedItem as ServerActionRow ?? throw new ArgumentException("Select a result row.");
        Action("Use selected ID", () => { if (!controls.TryGetValue("id", out var field) || field is not TextBox box) throw new ArgumentException("Choose an operation with an item/playlist ID field first."); box.Text = Row().Id; return Task.CompletedTask; });
        Action("Use selected occurrence", () => { var row = Row(); if (row.Occurrence == "") throw new ArgumentException("Load playlist occurrences first."); string key = controls.ContainsKey("entry") ? "entry" : "indices"; if (!controls.TryGetValue(key, out var control) || control is not TextBox box) throw new ArgumentException("Choose playlist occurrence removal/reordering first."); box.Text = row.Occurrence; return Task.CompletedTask; });
        Action("Add selected media IDs", () =>
        {
            string key = controls.ContainsKey("songs") ? "songs" : controls.ContainsKey("items") ? "items" : "item"; if (!controls.TryGetValue(key, out var control) || control is not TextBox box) throw new ArgumentException("Choose a playlist creation/append operation first.");
            var chosen = table.SelectedItems.Cast<ServerActionRow>().ToHashSet(); var ids = table.Items.Cast<ServerActionRow>().Where(chosen.Contains).Select(r => r.Id).Where(id => id != "").Take(51).ToArray(); if (ids.Length is < 1 or > 50 || key == "item" && ids.Length != 1) throw new ArgumentException("Choose the required 1–50 media entries (Plex creation uses one initial item).");
            if (key == "item") box.Text = ids[0]; else { string text = box.Text.Trim(); box.Text = (text == "" ? "" : text + "\n") + string.Join('\n', ids); } return Task.CompletedTask;
        });
        Action("Copy selected ID", () => { string id = Row().Id; if (id == "") throw new ArgumentException("The selected property has no item ID."); Clipboard.SetText(id); return Task.CompletedTask; });
        Action("Copy occurrence ID", () => { string occurrence = Row().Occurrence; if (occurrence == "") throw new ArgumentException("Load playlist occurrences first."); Clipboard.SetText(occurrence); return Task.CompletedTask; });
        Action("Export displayed report…", async () => { var current = report ?? throw new ArgumentException("Read a view or complete a change first."); string? path = SavePath(this, provider + " action report.json", ".json"); if (path != null) { await ServerActionCatalog.Export(current, path, OperationToken); if (!IsClosed) Status.Text = "Saved the displayed bounded report. Connection credentials are not included."; } });
        readAction.IsEnabled = !Definition().Writes; changeAction.IsEnabled = Definition().Writes;
        CinemaUi.GroupActions(Actions, ("Session", ["Connect / inspect", "Disconnect"]), ("Selection", ["Use selected ID", "Use selected occurrence", "Add selected media IDs", "Copy selected ID", "Copy occurrence ID"]));
        ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += (_, _) => { secret?.Clear(); Disconnect(); }; Status.Text = "No network request runs until Connect / inspect. Read and remote-change actions remain separate.";
    }
    private void Disconnect()
    { _connected = false; _subsonic?.Dispose(); _subsonic = null; _personal?.Dispose(); _personal = null; _upnp?.Dispose(); _upnp = null; _device = null; _target = ""; }
}
