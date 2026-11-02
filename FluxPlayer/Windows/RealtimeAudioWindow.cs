using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FluxPlayer.Windows;

public sealed class RealtimeAudioWindow : FeatureWorkbench
{
    public RealtimeAudioWindow(Window owner, MainViewModel vm) : base(owner, vm, "Continuous PCM and exclusive audio", "Play 1–32 queued local audio sources through a continuous FFmpeg PCM stream and WASAPI. No joined file is created. The main transport controls playback; manual Next/source changes leave this session. Exclusive and multichannel formats must be supported exactly; there is no silent fallback/downmix. Direct PCM bypasses software level/effects and needs your device's hardware volume control.")
    {
        var endpointTemplate = new DataTemplate(); var endpointText = new FrameworkElementFactory(typeof(TextBlock)); endpointText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AudioEndpointInfo.Name))); endpointTemplate.VisualTree = endpointText;
        var endpoints = Field("Windows audio endpoint", new ComboBox { ItemTemplate = endpointTemplate });
        endpoints.ItemsSource = new[] { new AudioEndpointInfo("Default", "System default", 0, 0) }.Concat(AudioEndpointFormats.Enumerate()).ToArray(); endpoints.SelectedIndex = 0;
        var exclusive = Toggle("Use exclusive WASAPI output"); var direct = Toggle("Direct integer PCM WAV (one file, unity gain, no DSP)");
        var layout = Choice("Explicit speaker layout", AudioEndpointFormats.Layouts, 1); var rate = Choice("Sample rate", ["44100", "48000", "88200", "96000", "176400", "192000"], 1);
        var fade = Text("Live crossfade seconds (0 = gapless)", "0", 16); var buffer = Text("PCM prebuffer milliseconds (500–10000)", "2000", 8);
        var effects = Text("Ordered DSP chain JSON (Gain, Limiter, Matrix, Plugin)", "[]", 65536); effects.AcceptsReturn = true; effects.Height = 90; effects.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        AudioFormatSupport[]? inspected = null;
        var formats = Table(("Layout", "Layout", 1), ("Rate", "Rate", 1), ("Mode", "Mode", 1), ("Accepted", "Supported", 1), ("Driver result", "Detail", 3)); formats.Height = 150;
        endpoints.SelectionChanged += (_, _) => { inspected = null; formats.ItemsSource = null; };
        Action("Inspect endpoint formats (no playback)", () =>
        {
            inspected = AudioEndpointFormats.Inspect((endpoints.SelectedItem as AudioEndpointInfo)?.Id ?? "Default"); formats.ItemsSource = inspected;
            Status.Text = $"{inspected.Count(r => r.Supported)}/{inspected.Length} exact formats accepted. Driver negotiation does not verify physical speaker wiring or bit-perfect capture."; return Task.CompletedTask;
        });
        Action("Save endpoint inspection JSON…", async () =>
        {
            if (inspected == null) throw new ArgumentException("Inspect the selected endpoint first.");
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON report|*.json", FileName = "FluxPlayer-audio-formats.json" };
            if (dialog.ShowDialog(this) != true) return;
            var report = new { utc = DateTimeOffset.UtcNow, endpoint = endpoints.SelectedItem as AudioEndpointInfo, physicalOutputVerified = false, formats = inspected };
            await System.IO.File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), OperationToken);
            Status.Text = "Endpoint negotiation report saved.";
        });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { if (vm.Engine.Realtime is { } session) { var p = session.Position(); Status.Text = $"{p.TrackIndex + 1}/{session.Tracks.Length} · {session.Tracks[p.TrackIndex].Title} · {p.PositionMs / 1000d:0.0}s · underrun frames {p.UnderrunFrames} · {(session.Options.DirectPcm ? "Direct exclusive PCM: software volume/mute/speed changes stop this mode" : string.Join(" → ", session.Chain?.Names ?? []))}"; } }; timer.Start();
        Action("Start continuous main-queue playback", async () =>
        {
            if (!double.TryParse(fade.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds) || !int.TryParse(buffer.Text, out int milliseconds)) throw new ArgumentException("Enter numeric fade and buffer settings.");
            var settings = JsonSerializer.Deserialize<PcmEffectSetting[]>(effects.Text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8 }) ?? [];
            if (direct.IsChecked == true && settings.Length != 0) throw new ArgumentException("Clear the effect chain for direct PCM.");
            var options = new RealtimeAudioOptions { Endpoint = (endpoints.SelectedItem as AudioEndpointInfo)?.Id ?? "Default", Exclusive = exclusive.IsChecked == true, DirectPcm = direct.IsChecked == true, Layout = layout.SelectedItem?.ToString() ?? "stereo", SampleRate = int.Parse(rate.SelectedItem!.ToString()!), CrossfadeSeconds = seconds, BufferMilliseconds = milliseconds };
            await vm.StartRealtimeAudio(options, settings.Length == 0 ? null : PcmEffectSetting.Configure(settings));
        });
        Action("Pause / resume", () => { vm.PlayPauseCommand.Execute(null); return Task.CompletedTask; }); Action("Stop", () => { vm.StopCommand.Execute(null); return Task.CompletedTask; });
        CancelAction(); Closed += (_, _) => timer.Stop(); Status.Text = "Effects execute in listed order. Managed Plugin entries run trusted local DLL code in-process; this is not a sandbox. Closing this panel leaves the main transport running.";
    }
}

public sealed class NativeDsdWindow : FeatureWorkbench
{
    public NativeDsdWindow(Window owner, MainViewModel vm) : base(owner, vm, "Native DSD · ASIO", "Stream uncompressed DSF/DSDIFF bits directly to a DSD-capable ASIO driver. No DoP, PCM conversion, software volume, rate conversion or DSP is applied. Use the device's hardware level control. Driver support is checked before playback; unsupported devices are rejected. This panel owns its DSD transport; closing it or using the main Stop/source selection stops it.")
    {
        var file = Text("Local uncompressed DSF or DSDIFF", "", 32767); var drivers = Choice("Installed ASIO driver", AsioNativeDsdDevice.Drivers()); var offset = Text("First physical ASIO output channel (zero based)", "0", 3);
        NativeDsdSession? session = null;
        async Task Stop()
        {
            var old = session; session = null;
            if (old != null) try { await old.DisposeAsync(); } catch (Exception ex) when (ex is not OutOfMemoryException) { if (!IsClosed) Status.Text = ex.Message; }
        }
        void MainStop() => _ = Stop(); vm.AuxiliaryAudioStopRequested += MainStop;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += async (_, _) => { if (session == null) return; if (session.Error != null || session.Finished) { string message = session.Error ?? "Native DSD source completed."; await Stop(); Status.Text = message; } else Status.Text = $"Native DSD · {session.PositionSeconds:0.00}/{session.DurationSeconds:0.00} seconds · underrun byte frames {session.UnderrunByteFrames}"; }; timer.Start();
        Action("Choose DSD file…", () => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Uncompressed DSD|*.dsf;*.dff" }; if (dialog.ShowDialog(this) == true) file.Text = dialog.FileName; return Task.CompletedTask; });
        Action("Start native DSD", async () =>
        {
            vm.StopCommand.Execute(null); await Stop();
            if (drivers.SelectedItem is not string driver || !int.TryParse(offset.Text, out int channel)) throw new ArgumentException("Choose an installed ASIO driver and channel offset.");
            var next = new NativeDsdSession(); session = next;
            try { await next.Prepare(file.Text, new AsioNativeDsdDevice(driver, channel), OperationToken); if (IsClosed) { await Stop(); return; } next.Play(); }
            catch { await Stop(); throw; }
        });
        Action("Pause / resume DSD", () => { if (session == null) throw new ArgumentException("Start a DSD session first."); if (session.Playing) session.Pause(); else session.Play(); return Task.CompletedTask; });
        Action("Stop DSD", Stop); CancelAction(); Closed += async (_, _) => { timer.Stop(); vm.AuxiliaryAudioStopRequested -= MainStop; await Stop(); };
        Status.Text = drivers.Items.Count == 0 ? "No ASIO driver is installed. Native DSD requires your DAC's compatible ASIO driver; no PCM fallback is performed." : "Ready for an explicit native DSD session.";
    }
}
