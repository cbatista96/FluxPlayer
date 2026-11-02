using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluxPlayer.Windows;

public sealed class CaptureWindow : ProcessingEditor
{
    public CaptureWindow(Window owner, MainViewModel vm) : base(owner, vm, "Capture and recorded-audio synchronization", "Explicit microphone, DirectShow video and desktop recording to new files. Device enumeration, preview and five-second input measurements access hardware only when requested.")
    {
        var devices = Group("Capture devices and duration", () => (Mic: Field("Microphone (optional for video)", new ComboBox()), Camera: Field("Camera / DirectShow video input", new ComboBox()), Seconds: Text("Recording duration in seconds (1–14,400)", "60", 5)), true);
        var sync = Group("Synchronize an existing recording with video", () => (Video: FileInput("Original video"), Audio: FileInput("Recorded audio"), Offset: Text("Recorded-audio offset (−30,000 to +30,000 ms)", "0", 7)));
        int Seconds() => Integer(devices.Seconds, 1, 14400);
        string Mic() => (devices.Mic.SelectedItem as CaptureDevice)?.Name ?? "";
        string Camera() => (devices.Camera.SelectedItem as CaptureDevice)?.Name ?? throw new InvalidOperationException("Enumerate and choose a video device.");
        string? Destination(string extension, string name) { var picker = new SaveFileDialog { Filter = "New capture|*" + extension, DefaultExt = extension, FileName = name + extension }; return picker.ShowDialog(this) == true ? picker.FileName : null; }
        Action("Enumerate devices", async () => { var items = await CaptureService.Devices(OperationToken); if (IsClosed) return; devices.Mic.ItemsSource = items.Where(d => d.Kind == "audio").ToArray(); devices.Camera.ItemsSource = items.Where(d => d.Kind == "video").ToArray(); Status.Text = $"Found {items.Length} devices. Select sources explicitly; enumeration does not certify driver compatibility."; });
        Action("Measure microphone input", async () => { var level = await CaptureService.MeasureMicrophone(Mic(), OperationToken); if (!IsClosed) Status.Text = $"Measured {level.Seconds:0.0} seconds · RMS {level.RmsDb:0.0} dBFS · Sample peak {level.PeakDb:0.0} dBFS. No audible monitoring or calibration claim."; });
        Action("Record microphone…", async () => { string? path = Destination(".wav", "Microphone capture"); if (path != null) { Status.Text = "Recording microphone… Cancel discards the incomplete copy."; await CaptureService.RecordMicrophone(Mic(), path, Seconds(), OperationToken); if (!IsClosed) Status.Text = "Saved microphone recording: " + path; } });
        Action("Preview webcam…", () => { new CameraPreviewWindow(owner, vm, Camera()).Show(); return Task.CompletedTask; });
        Action("Record video device…", async () => { string? path = Destination(".mkv", "Video capture"); if (path != null) { Status.Text = "Recording selected video device…"; await CaptureService.RecordVideo(Camera(), Mic(), path, Seconds(), OperationToken); if (!IsClosed) Status.Text = "Saved video-device recording: " + path; } });
        Action("Record desktop…", async () => { string? path = Destination(".mkv", "Desktop capture"); if (path != null) { Status.Text = "Recording desktop at 15 FPS without audio…"; await CaptureService.RecordDesktop(path, Seconds(), OperationToken); if (!IsClosed) Status.Text = "Saved desktop recording: " + path; } });
        Action("Export synchronized copy…", async () => { string? path = Destination(".mkv", "Synchronized recording"); if (path != null) { await CaptureService.SynchronizeRecordedAudio(sync.Video.Text.Trim(), sync.Audio.Text.Trim(), path, Integer(sync.Offset, -30000, 30000), OperationToken); if (!IsClosed) Status.Text = "Saved video-copy / replacement-audio mix with the selected offset."; } });
        CinemaUi.GroupActions(Actions, ("Record", ["Record microphone…", "Record video device…", "Record desktop…"])); ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Recording completes at the selected duration. Cancellation removes the incomplete output. Close camera preview before recording if a driver allows only one client. Synchronization replaces the video audio with the selected recording; alignment is manual.";
    }
}

public sealed class CameraPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    public CameraPreviewWindow(Window owner, MainViewModel vm, string camera)
    {
        Owner = owner; Title = "Camera preview · FluxPlayer"; Width = 900; Height = 660; MinWidth = 640; MinHeight = 480; ExtendsContentIntoTitleBar = true; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = Brushes.Transparent;
        var body = new Grid { Margin = new(16) }; body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); var image = new Image { Stretch = Stretch.Uniform, Margin = new(0, 0, 0, 12) }; body.Children.Add(image);
        var status = new TextBlock { Text = "Untested DirectShow preview · " + camera, TextWrapping = TextWrapping.Wrap }; Grid.SetRow(status, 1); body.Children.Add(status); Content = ToolWindowChrome.Wrap(this, body);
        var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); var renderer = new VlcWpfRenderer(engine, image); var media = new Media(engine.LibVLC, "dshow://", FromType.FromLocation); media.AddOption(":dshow-vdev=" + camera); media.AddOption(":dshow-adev=none"); media.AddOption(":live-caching=300"); bool closed = false;
        engine.EncounteredError += (_, _) => Dispatcher.BeginInvoke(() => { if (!closed) status.Text = "The camera could not be opened. Driver and device support remain unverified."; });
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this); Loaded += (_, _) => { engine.SetVolume(0); engine.MediaPlayer.Play(media); };
        Closed += (_, _) => { closed = true; engine.Stop(); renderer.Dispose(); media.Dispose(); engine.Dispose(); };
    }
}
