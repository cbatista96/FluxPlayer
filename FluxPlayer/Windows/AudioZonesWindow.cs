using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class AudioZonesWindow : RemoteSourceWorkbench
{
    public AudioZonesWindow(Window owner, MainViewModel vm) : base(owner, vm, "Independent audio zones", "Create up to four separate local audio players with their own queues and output devices. Group commands are best-effort software calls, not sample-accurate multiroom synchronization. Closing this panel stops all zones.")
    {
        var name = Text("Zone name", "Zone", 100); var device = Field("Output device (refresh for selected zone)", new ComboBox()); device.ItemsSource = new[] { new AudioDevice("Default", "System default") }; device.DisplayMemberPath = "Name"; device.SelectedIndex = 0;
        var volume = Text("Volume (0–100)", "50", 3); var mute = Toggle("Mute zone"); var rate = Text("Playback rate (0.5–2)", "1", 6); var repeat = Toggle("Repeat zone queue"); var offset = Text("Group timeline offset (−5,000 to +5,000 ms)", "0", 6); var seek = Text("Group target seconds", "0", 8); CinemaUi.Compact(Form, 3);
        var table = Table(("Zone", "Name", 2), ("Device", "Device", 2), ("Source", "Source", 3), ("Playing", "Playing", 1), ("Position (ms)", "PositionMs", 1), ("Drift (ms)", "RelativeDriftMs", 1)); var zones = new AudioZones(vm.ProfileDirectory, Dispatcher); zones.Error += message => { if (!IsClosed) Status.Text = "Zone: " + message; };
        AudioZoneState Selected() => table.SelectedItem as AudioZoneState ?? throw new ArgumentException("Select an active zone.");
        void Refresh() { string? id = (table.SelectedItem as AudioZoneState)?.Id; table.ItemsSource = zones.Snapshot(); if (id != null) table.SelectedItem = table.Items.Cast<AudioZoneState>().FirstOrDefault(s => s.Id == id); }
        AudioZoneProfile Read(string id) => new(id, name.Text.Trim(), (device.SelectedItem as AudioDevice)?.Id ?? "Default", MasteringFields.Integers(volume.Text, 1, 0, 100)[0], mute.IsChecked == true, (float)MasteringFields.Numbers(rate.Text, 1, .5, 2)[0], repeat.IsChecked == true, MasteringFields.Integers(offset.Text, 1, -5000, 5000)[0], zones.Profiles.FirstOrDefault(z => z.Id == id)?.Queue ?? ImmutableArray<string>.Empty);
        Action("Create zone", () => { zones.Create(Read(Guid.NewGuid().ToString("N"))); Refresh(); return Task.CompletedTask; });
        Action("Read selected zone controls", () => { var profile = zones.Profiles.First(z => z.Id == Selected().Id); name.Text = profile.Name; volume.Text = profile.Volume.ToString(CultureInfo.InvariantCulture); mute.IsChecked = profile.Mute; rate.Text = profile.Rate.ToString(CultureInfo.InvariantCulture); repeat.IsChecked = profile.Repeat; offset.Text = profile.OffsetMs.ToString(CultureInfo.InvariantCulture); device.ItemsSource = zones.Devices(profile.Id); device.SelectedItem = device.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == profile.Device); return Task.CompletedTask; });
        Action("Refresh selected output devices", () => { device.ItemsSource = zones.Devices(Selected().Id); device.SelectedIndex = 0; return Task.CompletedTask; });
        Action("Apply selected zone controls", () => { zones.Configure(Read(Selected().Id)); Refresh(); return Task.CompletedTask; });
        Action("Load selected zone queue…", () => { var id = Selected().Id; var picker = new OpenFileDialog { Filter = MediaFormats.FileDialogFilter, Multiselect = true }; if (picker.ShowDialog(this) == true) { zones.LoadQueue(id, picker.FileNames); Refresh(); } return Task.CompletedTask; });
        foreach (string action in new[] { "Play", "Pause", "Stop" }) { string command = action; Action("Zone " + action.ToLowerInvariant(), () => { zones.Transport(Selected().Id, command); Refresh(); return Task.CompletedTask; }); }
        Action("Zone next track", () => { zones.Next(Selected().Id); Refresh(); return Task.CompletedTask; });
        foreach (string action in new[] { "Start queues", "Play", "Pause", "Stop", "Seek" }) { string command = action; Action("Group " + action.ToLowerInvariant(), () => { zones.Group(table.SelectedItems.Cast<AudioZoneState>().Select(s => s.Id).ToArray(), command, (long)(MasteringFields.Numbers(seek.Text, 1, 0, 86400)[0] * 1000)); Refresh(); return Task.CompletedTask; }); }
        Action("Snapshot positions and drift", () => { Refresh(); Status.Text = "Positions were sampled sequentially. Drift is relative to the first playing zone, not a calibrated output-clock measurement."; return Task.CompletedTask; });
        Action("Save zone profiles / queues", async () => { await zones.Save(OperationToken); if (!IsClosed) Status.Text = "Saved profiles. Loading them later does not start playback."; });
        Action("Restore saved zones", async () => { if (zones.Profiles.Length != 0) throw new ArgumentException("Remove active zones before restoring profiles."); var saved = await zones.Saved(OperationToken); if (!IsClosed) { foreach (var profile in saved.Zones) zones.Create(profile); Refresh(); } });
        Action("Remove selected zone", async () => { await zones.Remove(Selected().Id); if (!IsClosed) Refresh(); });
        CinemaUi.GroupActions(Actions, ("Zone", ["Read selected zone controls", "Refresh selected output devices", "Apply selected zone controls", "Load selected zone queue…", "Zone play", "Zone pause", "Zone stop", "Zone next track", "Remove selected zone"]), ("Group", ["Group start queues", "Group play", "Group pause", "Group stop", "Group seek"]), ("Profiles", ["Save zone profiles / queues", "Restore saved zones"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction()); Closed += async (_, _) => await zones.DisposeAsync();
    }
}
