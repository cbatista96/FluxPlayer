using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using LibVLCSharp.Shared;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FluxPlayer.Windows;

public sealed class CastingWindow : FeatureWorkbench
{
    public CastingWindow(Window owner, MainViewModel vm) : base(owner, vm, "Cast local media", "Send a selected local file through LibVLC's Chromecast renderer or to an inspected DLNA renderer. Discovery and file sharing start only on request. Device codec support and LAN/firewall access apply. Closing this panel stops its local sender/share.")
    {
        var source = Text("Local media file", vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "", 32767);
        var castDevices = new ObservableCollection<RendererItem>(); var castChoice = Field("Chromecast renderers (LibVLC discovery)", new ComboBox { ItemsSource = castDevices, DisplayMemberPath = "Name" });
        var lan = Choice("Private interface for DLNA file sharing", MobileRemote.Interfaces()); var port = Text("DLNA share port", "18767", 5); var description = Text("DLNA renderer description URL", "http://", 2048);
        RendererDiscoverer? discovery = null; MediaPlayer? player = null; var upnp = new UpnpMedia(); var share = new MediaFileShare(); UpnpDevice? device = null;
        Action("Choose file…", () => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Models.MediaFormats.FileDialogFilter }; if (dialog.ShowDialog(this) == true) source.Text = dialog.FileName; return Task.CompletedTask; });
        Action("Discover Chromecast renderers", () =>
        {
            if (discovery != null) return Task.CompletedTask;
            discovery = new RendererDiscoverer(vm.LibVLC ?? throw new InvalidOperationException("Native engine unavailable."), "microdns");
            discovery.ItemAdded += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (!IsClosed) castDevices.Add(e.RendererItem); }));
            discovery.ItemDeleted += (_, e) => { string name = e.RendererItem.Name; Dispatcher.BeginInvoke(new Action(() => { if (!IsClosed) { var found = castDevices.FirstOrDefault(d => d.Name == name); if (found != null) castDevices.Remove(found); } })); };
            if (!discovery.Start()) { discovery.Dispose(); discovery = null; throw new InvalidOperationException("LibVLC renderer discovery could not start."); }
            Status.Text = "Discovery is active. Choose a renderer and explicitly send the file."; return Task.CompletedTask;
        });
        Action("Send to selected Chromecast", () =>
        {
            if (castChoice.SelectedItem is not RendererItem renderer) throw new ArgumentException("Discover and select a renderer first.");
            string path = FfmpegJob.Source(source.Text); player?.Dispose(); player = new MediaPlayer(vm.LibVLC!);
            player.SetRenderer(renderer); player.EncounteredError += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!IsClosed) Status.Text = "Chromecast sender failed. Check the device, format and network."; }));
            using var media = new Media(vm.LibVLC!, new Uri(path)); if (!player.Play(media)) throw new InvalidOperationException("LibVLC did not accept the cast request.");
            Status.Text = "Chromecast send requested. The renderer must confirm playback on the device."; return Task.CompletedTask;
        });
        Action("Pause/resume Chromecast", () => { player?.Pause(); return Task.CompletedTask; }); Action("Stop Chromecast", () => { player?.Stop(); return Task.CompletedTask; });
        Action("Inspect DLNA renderer", async () => { device = null; var found = await upnp.Inspect(description.Text.Trim(), OperationToken); if (!found.Services.Any(s => s.Type.Contains(":AVTransport:", StringComparison.Ordinal))) throw new ArgumentException("This device has no AVTransport service."); device = found; Status.Text = found.Name + " · ready for an explicit load."; });
        Action("Share file and load DLNA renderer", async () =>
        {
            if (device == null) throw new ArgumentException("Inspect a renderer first."); await share.DisposeAsync();
            if (!int.TryParse(port.Text, out int number)) throw new ArgumentException("Enter a port number.");
            share.Start(source.Text, lan.SelectedItem?.ToString() ?? "", number);
            try
            {
                XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/", dc = "http://purl.org/dc/elements/1.1/", ns = "urn:schemas-upnp-org:metadata-1-0/upnp/";
                string metadata = new XElement(didl + "DIDL-Lite", new XElement(didl + "item", new XAttribute("id", "fluxplayer"), new XAttribute("parentID", "0"), new XAttribute("restricted", "1"), new XElement(dc + "title", System.IO.Path.GetFileName(source.Text)), new XElement(ns + "class", share.ContentType.StartsWith("audio/", StringComparison.Ordinal) ? "object.item.audioItem.musicTrack" : "object.item.videoItem"), new XElement(didl + "res", new XAttribute("protocolInfo", "http-get:*:" + share.ContentType + ":*"), share.Address))).ToString(SaveOptions.DisableFormatting);
                await upnp.LoadRenderer(device, new("fluxplayer", source.Text, false, share.Address, metadata), OperationToken); Status.Text = "Loaded on DLNA renderer. Use DLNA Play. File sharing remains active while this panel is open.";
            }
            catch { await share.DisposeAsync(); throw; }
        });
        foreach (string verb in new[] { "Play", "Pause", "Stop" }) { string action = verb; Action("DLNA " + action, () => device == null ? throw new ArgumentException("Inspect a renderer first.") : upnp.Transport(device, action, OperationToken)); }
        Action("Stop file share", async () => { await share.DisposeAsync(); Status.Text = "Local file share stopped."; });
        CancelAction(); Closed += async (_, _) => { player?.Dispose(); discovery?.Stop(); discovery?.Dispose(); upnp.Dispose(); await share.DisposeAsync(); };
    }
}
