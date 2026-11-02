using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class UpnpWindow : RemoteSourceWorkbench
{
    public UpnpWindow(Window owner, MainViewModel vm) : base(owner, vm, "UPnP media and renderers", "Discover devices explicitly on the local network, browse a selected server and control a separately selected renderer. No discovery or renderer action runs at startup.")
    {
        var address = Text("Device description URL (or select a discovered address)", "http://", 2048); var found = Field("Discovered description addresses", new ComboBox()); var server = Field("Content server", new ComboBox()); var renderer = Field("Remote renderer", new ComboBox());
        var objectId = Text("Container ID", "0", 512); var criteria = Text("Optional UPnP search criteria (empty browses children)", "", 500); var offset = Text("Page offset", "0", 7); var seek = Text("Renderer seek seconds", "0", 8); var volume = Text("Renderer master volume (0–100)", "50", 3); CinemaUi.Compact(Form, 3);
        var table = Table(("Container", "Folder", 1), ("Title", "Title", 4), ("Object ID", "Id", 2)); var api = new UpnpMedia(); var devices = new List<UpnpDevice>();
        UpnpDevice Device(ComboBox choice) => choice.SelectedItem as UpnpDevice ?? throw new ArgumentException("Inspect and select the required device.");
        UpnpItem Item() => table.SelectedItem as UpnpItem ?? throw new ArgumentException("Select an item.");
        async Task Browse() { int start = MasteringFields.Integers(offset.Text, 1, 0, 1000000)[0]; table.ItemsSource = await api.Browse(Device(server), objectId.Text.Trim(), start, criteria.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = "Loaded up to 100 entries. Search syntax/support depends on the device."; }
        Action("Discover LAN devices", async () => { found.ItemsSource = await UpnpMedia.Discover(OperationToken); found.SelectedIndex = found.Items.Count > 0 ? 0 : -1; Status.Text = "Discovery finished. Inspect a chosen description; hostname-based descriptions can be entered manually."; });
        found.SelectionChanged += (_, _) => { if (found.SelectedItem is string url) address.Text = url; };
        Action("Inspect description", async () => { var device = await api.Inspect(address.Text.Trim(), OperationToken); if (IsClosed) return; devices.RemoveAll(d => d.Description == device.Description); devices.Add(device); foreach (var choice in new[] { server, renderer }) { choice.ItemsSource = null; choice.ItemsSource = devices.ToArray(); choice.DisplayMemberPath = "Name"; choice.SelectedIndex = devices.Count - 1; } Status.Text = device.Name + " · " + device.Kind + " · " + string.Join(", ", device.Services.Select(s => s.Type)); });
        Action("Browse / search container", Browse);
        Action("Open selected container", async () => { var item = Item(); if (!item.Folder) throw new ArgumentException("Select a container."); objectId.Text = item.Id; offset.Text = "0"; criteria.Text = ""; await Browse(); });
        Action("Play resource locally", () => { var item = Item(); if (item.Resource == "") throw new ArgumentException("Select a media resource."); vm.AddCatalogTracks([new(item.Resource, item.Title)], true); return Task.CompletedTask; });
        Action("Load selected resource on renderer", async () => { await api.LoadRenderer(Device(renderer), Item(), OperationToken); if (!IsClosed) Status.Text = "Requested renderer URI load. Use Renderer play explicitly."; });
        foreach (string verb in new[] { "Play", "Pause", "Stop" }) { string action = verb; Action("Renderer " + action.ToLowerInvariant(), () => api.Transport(Device(renderer), action, OperationToken)); }
        Action("Seek renderer", () => api.Seek(Device(renderer), (long)(MasteringFields.Numbers(seek.Text, 1, 0, 86400)[0] * 1000), OperationToken));
        Action("Set renderer volume", () => api.Level(Device(renderer), MasteringFields.Integers(volume.Text, 1, 0, 100)[0], null, OperationToken));
        Action("Mute renderer", () => api.Level(Device(renderer), 0, true, OperationToken)); Action("Unmute renderer", () => api.Level(Device(renderer), 0, false, OperationToken));
        Action("Read renderer status", async () => { var result = await api.Status(Device(renderer), OperationToken); if (!IsClosed) Status.Text = string.Join(" · ", result.Select(v => v.Property + ": " + v.Value)); });
        CinemaUi.GroupActions(Actions, ("Server", ["Browse / search container", "Open selected container", "Play resource locally"]), ("Renderer", ["Load selected resource on renderer", "Renderer play", "Renderer pause", "Renderer stop", "Seek renderer", "Set renderer volume", "Mute renderer", "Unmute renderer", "Read renderer status"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += (_, _) => api.Dispose();
    }
}
