using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public class ProcessingEditor : FeatureWorkbench
{
    public ProcessingEditor(Window owner, MainViewModel vm, string title, string description) : base(owner, vm, title, description + " Source implementation awaiting compilation and testing.") { UseFormLayout(800); }
    protected T Group<T>(string name, Func<T> create, bool open = false)
    {
        int before = Form.Children.Count; T result = create(); var controls = Form.Children.Cast<UIElement>().Skip(before).ToArray(); var content = new StackPanel { Margin = new(12, 14, 12, 4) };
        foreach (var child in controls) { Form.Children.Remove(child); content.Children.Add(child); }
        Form.Children.Add(new Expander { Header = name, Content = content, IsExpanded = open, Margin = new(0, 0, 0, 12), Padding = new(8) }); return result;
    }
    protected TextBox FileInput(string label, string path = "", bool image = false)
    {
        var input = Text(label, path, 32767); var button = new Button { Content = "Choose file…", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 7, 12, 7), Margin = new(0, -4, 0, 12) }; Form.Children.Add(button);
        button.Click += (_, _) => { var picker = new OpenFileDialog { Filter = image ? "Local image|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" : MediaFormats.FileDialogFilter }; if (picker.ShowDialog(this) == true) input.Text = picker.FileName; }; return input;
    }
    protected static double Number(TextBox box, double min, double max)
    { if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"Use a finite number from {min} to {max}, with a decimal point."); return value; }
    protected static int Integer(TextBox box, int min, int max) => CinemaText.Integer(box.Text, min, max);
    protected static int SelectedNumber(ComboBox box) => int.Parse(box.SelectedItem?.ToString() ?? "0", CultureInfo.InvariantCulture);
    protected static string CurrentLocal(MainViewModel vm) => vm.CurrentItem is { IsNetwork: false } item ? item.FilePath : "";
}

public sealed class AudioLabWindow : ProcessingEditor
{
    public AudioLabWindow(Window owner, MainViewModel vm) : base(owner, vm, "Offline audio processing", "Create a stereo WAV/FLAC copy using selected cleanup, time/pitch, convolution and quantization settings. FFmpeg runs only when exporting. Playback DSP is independent.")
    {
        var input = Group("Source and output format", () => (Source: FileInput("Local source", CurrentLocal(vm)), Format: Choice("Output container", ["WAV", "FLAC"]), Rate: Choice("Sample rate", ["44100", "48000", "96000", "192000"], 1), Bits: Choice("PCM bit depth", ["16", "24", "32"], 1), Resampler: Choice("Resampling filter", AudioLab.Resamplers, 2)), true);
        var cleanup = Group("Signal cleanup and sample-peak normalization", () => (Dc: Toggle("Remove DC / very low frequencies (20 Hz high-pass)"), Riaa: Toggle("Restore RIAA curve — only for raw pre-emphasized recordings"), Normalize: Toggle("Measure and normalize processed sample peak (two passes)"), Peak: Text("Target sample peak (−12 to −0.1 dBFS)", "-1", 12)));
        var time = Group("Pitch and tempo", () => (Pitch: Text("Pitch shift (−12 to +12 semitones)", "0", 12), Tempo: Text("Tempo multiplier (0.5–2; independent of pitch)", "1", 12)));
        var convolution = Group("FIR / impulse-response convolution", () => (Impulse: FileInput("Optional impulse-response file (empty disables convolution)"), Gain: Text("Impulse coefficient gain (0–1)", "1", 12)));
        var quantization = Group("Dither and noise shaping", () => (Dither: Choice("Base 16-bit dither", AudioLab.Dithers), Shape: Choice("Noise-shaping dither (replaces base dither)", AudioLab.NoiseShapes)));
        quantization.Shape.SelectionChanged += (_, _) => quantization.Dither.IsEnabled = quantization.Shape.SelectedIndex == 0;
        AudioLabOptions Read() => new() { SampleRate = SelectedNumber(input.Rate), Bits = SelectedNumber(input.Bits), Resampler = (string)input.Resampler.SelectedItem, Dither = quantization.Shape.SelectedIndex == 0 ? (string)quantization.Dither.SelectedItem : "None", NoiseShape = (string)quantization.Shape.SelectedItem, RemoveDc = cleanup.Dc.IsChecked == true, RestoreRiaa = cleanup.Riaa.IsChecked == true, NormalizePeak = cleanup.Normalize.IsChecked == true, TargetPeakDb = Number(cleanup.Peak, -12, -.1), PitchSemitones = Number(time.Pitch, -12, 12), Tempo = Number(time.Tempo, .5, 2), Impulse = convolution.Impulse.Text.Trim(), ImpulseGain = Number(convolution.Gain, 0, 1) };
        Action("Review recipe", () =>
        {
            var options = Read(); _ = AudioLab.Graph(options);
            Status.Text = $"Stereo {options.SampleRate:N0} Hz / {options.Bits}-bit · {options.Resampler} · Pitch {options.PitchSemitones:+0.##;-0.##;0} semitones · Tempo {options.Tempo:0.##}× · Convolution {(options.Impulse == "" ? "off" : "on")} · Peak normalization {(options.NormalizePeak ? "two-pass" : "off")}. Recipe review does not render or test the output."; return Task.CompletedTask;
        });
        Action("Export new processed copy…", async () =>
        {
            var options = Read(); string extension = input.Format.SelectedIndex == 0 ? ".wav" : ".flac";
            var picker = new SaveFileDialog { Filter = "Processed audio|*" + extension, DefaultExt = extension, FileName = "Processed audio" + extension };
            if (picker.ShowDialog(this) != true) return;
            var progress = new Progress<string>(message => { if (!IsClosed) Status.Text = message; });
            await AudioLab.Export(input.Source.Text.Trim(), picker.FileName, options, progress, OperationToken);
            if (!IsClosed) Status.Text = "Saved a new processed copy: " + picker.FileName;
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Impulse processing uses the first 10 seconds, downmixed to mono, at output rate. FIR latency/tails and peak/dither quality remain unverified. No true-peak or bit-perfect claim.";
    }
}

public sealed class FirDesignerWindow : ProcessingEditor
{
    public FirDesignerWindow(Window owner, MainViewModel vm) : base(owner, vm, "FIR impulse designer", "Generate a mono floating-point WAV impulse using a Hamming-windowed sinc design. Apply the saved impulse through Offline audio processing. This does not measure or calibrate a room.")
    {
        var mode = Choice("Filter design", ["Low-pass", "High-pass", "Band-pass"]); var rate = Choice("Design sample rate", ["44100", "48000", "96000", "192000"], 1);
        var taps = Text("Odd tap count (31–8,191)", "511", 5); var low = Text("Low cutoff in Hz (high-pass/band-pass)", "100", 12); var high = Text("High cutoff in Hz (low-pass/band-pass)", "8000", 12);
        void ActiveCutoffs() { low.IsEnabled = mode.SelectedIndex != 0; high.IsEnabled = mode.SelectedIndex != 1; } mode.SelectionChanged += (_, _) => ActiveCutoffs(); ActiveCutoffs();
        Action("Save new impulse WAV…", async () =>
        {
            var options = new FirDesign((string)mode.SelectedItem, SelectedNumber(rate), Integer(taps, 31, 8191), mode.SelectedIndex == 0 ? 10 : Number(low, 10, 94000), mode.SelectedIndex == 1 ? 10000 : Number(high, 11, 94000));
            var picker = new SaveFileDialog { Filter = "Mono floating-point impulse|*.wav", DefaultExt = ".wav", FileName = "FIR impulse.wav" }; if (picker.ShowDialog(this) != true) return;
            await FirDesigner.Write(picker.FileName, options, OperationToken); if (!IsClosed) Status.Text = $"Saved impulse. Intended linear-phase delay: {(options.Taps - 1) / 2d / options.SampleRate * 1000:0.###} ms. Frequency response has not been verified.";
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "For narrow bands, increase the tap count. Cutoffs must remain below 49% of design sample rate. New files only; existing impulses are preserved.";
    }
}

public sealed class VideoLabWindow : ProcessingEditor
{
    public VideoLabWindow(Window owner, MainViewModel vm) : base(owner, vm, "Offline video processing", "Export a new H.264/AAC MP4 using explicit scaling, restoration, interpolation, overlays and optional HDR-to-SDR conversion. These are export filters, not changes to the playback renderer.")
    {
        var input = Group("Source and scaling", () => (Source: FileInput("Local video source", CurrentLocal(vm)), Width: Text("Maximum output width (even; 64–3,840)", "1280", 5), Height: Text("Maximum output height (even; 64–2,160)", "720", 5), Reduce: Toggle("Only reduce dimensions; do not upscale", true), Kernel: Choice("Scaling kernel", VideoLab.Kernels, 1)), true);
        var restoration = Group("Restoration and motion", () => (Deband: Text("Deband threshold (0 disables; up to 0.1)", "0", 12), Noise: Text("Noise reduction strength (0 disables; up to 10)", "0", 12), Stabilize: Toggle("Attempt small-shake stabilization (16-pixel search)"), Fps: Choice("Motion-interpolated output FPS (0 keeps source timing)", ["0", "24", "25", "30", "50", "60"])));
        var color = Group("HDR-to-SDR and color range", () => (Tone: Choice("Tone mapping (None assumes SDR input)", VideoLab.ToneMaps), Transfer: Choice("Declared HDR input transfer (BT.2020 primaries)", ["PQ", "HLG"]), SourcePeak: Text("Declared source peak in nits", "1000", 12), Target: Text("SDR reference/target luminance in nits (50–400)", "100", 12), InputRange: Choice("Input range interpretation", ["Auto", "Limited", "Full"]), OutputRange: Choice("Output range", ["Limited", "Full"])));
        var overlay = Group("Text and logo overlay", () => (Image: FileInput("Optional local logo image", image: true), Text: Text("Optional caption (up to 500 characters)", "", 500), X: Text("Overlay left position in output pixels", "24", 5), Y: Text("Overlay top position in output pixels", "24", 5), Width: Text("Overlay width (64–1,024 pixels)", "300", 5)));
        VideoLabOptions Read() => new() { Width = Integer(input.Width, 64, 3840), Height = Integer(input.Height, 64, 2160), ReduceOnly = input.Reduce.IsChecked == true, Kernel = (string)input.Kernel.SelectedItem, Deband = Number(restoration.Deband, 0, .1), Denoise = Number(restoration.Noise, 0, 10), Stabilize = restoration.Stabilize.IsChecked == true, InterpolateFps = SelectedNumber(restoration.Fps), ToneMap = (string)color.Tone.SelectedItem, HdrTransfer = (string)color.Transfer.SelectedItem, SourcePeakNits = Number(color.SourcePeak, 50, 10000), TargetNits = Number(color.Target, 50, 400), InputRange = (string)color.InputRange.SelectedItem, OutputRange = (string)color.OutputRange.SelectedItem, OverlayImage = overlay.Image.Text.Trim(), OverlayText = overlay.Text.Text.Trim(), OverlayX = Integer(overlay.X, 0, 3840), OverlayY = Integer(overlay.Y, 0, 2160), OverlayWidth = Integer(overlay.Width, 64, 1024) };
        void ColorMode() { bool enabled = color.Tone.SelectedIndex != 0; color.Transfer.IsEnabled = color.SourcePeak.IsEnabled = color.Target.IsEnabled = enabled; }
        color.Tone.SelectionChanged += (_, _) => ColorMode(); ColorMode();
        Action("Review recipe", () => { var options = Read(); _ = VideoLab.Graph(options, options.OverlayText != ""); Status.Text = $"Up to {options.Width}×{options.Height}, {options.Kernel}, {options.OutputRange} range · Tone mapping {options.ToneMap} · Motion FPS {options.InterpolateFps} · Restoration and overlays follow the selected settings. HDR settings are manual assumptions. No preview render has been executed."; return Task.CompletedTask; });
        Action("Export new video copy…", async () =>
        {
            var options = Read(); var picker = new SaveFileDialog { Filter = "Processed H.264/AAC MP4|*.mp4", DefaultExt = ".mp4", FileName = "Processed video.mp4" }; if (picker.ShowDialog(this) != true) return;
            string caption = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(options.OverlayText)) caption = OverlayRaster.CreateCaption(options.OverlayText, options.OverlayWidth);
                var progress = new Progress<string>(message => { if (!IsClosed) Status.Text = message; });
                await VideoLab.Export(input.Source.Text.Trim(), picker.FileName, options, caption, progress, OperationToken); if (!IsClosed) Status.Text = "Saved a new processed video: " + picker.FileName;
            }
            finally { if (caption != "" && File.Exists(caption)) File.Delete(caption); }
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Output: first video and optional first audio stream, no subtitles/additional tracks. HDR conversion requires zscale/tonemap in FFmpeg. Target nits do not change the physical monitor. Existing files are preserved.";
    }
}
