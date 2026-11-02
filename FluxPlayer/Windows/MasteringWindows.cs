using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

internal static class MasteringFields
{
    public static double[] Numbers(string text, int count, double min, double max)
    {
        var parts = text.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != count) throw new ArgumentException($"Enter exactly {count} values; use decimal points and commas/spaces between values.");
        return parts.Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n >= min && n <= max ? n : throw new ArgumentException($"Use finite values from {min} to {max}.")).ToArray();
    }
    public static int[] Integers(string text, int count, int min, int max)
    {
        var values = Numbers(text, count, min, max); if (values.Any(v => v != Math.Truncate(v))) throw new ArgumentException("Channel indices and millisecond delays must be whole numbers."); return values.Select(v => (int)v).ToArray();
    }
    public static TextBox Multiline(TextBox box, double height)
    { box.AcceptsReturn = true; box.TextWrapping = TextWrapping.NoWrap; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; box.Height = height; return box; }
    public static string Row(IEnumerable<double> values) => string.Join(", ", values.Select(v => v.ToString("0.########", CultureInfo.InvariantCulture)));
}

public sealed class ChannelMasteringWindow : ProcessingEditor
{
    public ChannelMasteringWindow(Window owner, MainViewModel vm) : base(owner, vm, "Multichannel file mastering", "Render a new multichannel WAV/FLAC with explicit routing, speaker controls and optional bass redirection. This is an offline workflow; it does not change your output device or live playback DSP.")
    {
        var source = Group("Sources and speaker layouts", () => (Primary: FileInput("Primary local media", CurrentLocal(vm)), Secondary: FileInput("Optional second source (empty disables mixing)"), Input: Choice("Normalize each source to this input layout", ChannelMastering.Layouts, 1), Output: Choice("Physical output speaker layout", ChannelMastering.Layouts, 1), Mix: Text("Second-source gain (0–1; no automatic normalization)", "0.5", 12)), true);
        var routing = Group("Channel routing", () => (Preset: Choice("Routing preset to load", ["Match speaker names", "Stereo spread into surround"]), Matrix: MasteringFields.Multiline(Text("Matrix: one row per output, one coefficient per input (−2 to +2; row absolute sum ≤4)", "1, 0\n0, 1", 8192), 150)), true);
        var channels = Group("Output gain, delay and channel order", () => (Names: Text("Output speaker order (reference only)", "FL, FR", 200), Gain: Text("Gain in dB for each physical output (−60 to +12)", "0, 0", 200), Delay: Text("Delay in milliseconds for each output (0–2,000)", "0, 0", 200), Order: Text("Post-matrix channel permutation (zero-based, each index exactly once)", "0, 1", 100), Headroom: Text("Global headroom (−24 to 0 dB; reduce further for large matrix sums)", "-6", 12)));
        channels.Names.IsReadOnly = true;
        var bass = Group("Bass management", () => (Enabled: Toggle("High-pass main speakers and redirect low-frequency sum to LFE"), Cutoff: Text("Two-pole high/low-pass crossover (40–200 Hz)", "80", 12)));
        var format = Group("New output file", () => (Container: Choice("Container", ["WAV", "FLAC"]), Rate: Choice("Sample rate", ["44100", "48000", "96000", "192000"], 1), Bits: Choice("PCM bits (32-bit requires WAV)", ["16", "24", "32"], 1)));
        ChannelMasteringOptions Read()
        {
            string input = (string)source.Input.SelectedItem, output = (string)source.Output.SelectedItem;
            int inputs = ChannelMastering.Speakers(input).Length, outputs = ChannelMastering.Speakers(output).Length;
            var rows = routing.Matrix.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length != outputs) throw new ArgumentException("The output layout changed. Load a routing preset, then edit its output rows.");
            return new() { InputLayout = input, OutputLayout = output, Matrix = rows.Select(r => MasteringFields.Numbers(r, inputs, -2, 2)).ToArray(), GainDb = MasteringFields.Numbers(channels.Gain.Text, outputs, -60, 12), DelayMs = MasteringFields.Integers(channels.Delay.Text, outputs, 0, 2000), Order = MasteringFields.Integers(channels.Order.Text, outputs, 0, outputs - 1), HeadroomDb = Number(channels.Headroom, -24, 0), BassManagement = bass.Enabled.IsChecked == true, CrossoverHz = Number(bass.Cutoff, 40, 200), SecondaryGain = Number(source.Mix, 0, 1), SampleRate = SelectedNumber(format.Rate), Bits = SelectedNumber(format.Bits) };
        }
        Action("Load routing preset / reset channel controls", () =>
        {
            string input = (string)source.Input.SelectedItem, output = (string)source.Output.SelectedItem;
            if (routing.Preset.SelectedIndex == 1 && input != "stereo") throw new ArgumentException("Stereo spread requires the stereo input layout.");
            var speakers = ChannelMastering.Speakers(output); routing.Matrix.Text = string.Join('\n', ChannelMastering.Preset(input, output, routing.Preset.SelectedIndex == 1).Select(MasteringFields.Row));
            channels.Names.Text = string.Join(", ", speakers); channels.Gain.Text = string.Join(", ", speakers.Select(_ => "0")); channels.Delay.Text = channels.Gain.Text; channels.Order.Text = string.Join(", ", Enumerable.Range(0, speakers.Length));
            Status.Text = "Routing preset loaded; gains, delays and permutation reset. Source/layout choices are explicit; the matrix is still editable."; return Task.CompletedTask;
        });
        Action("Review routing recipe", () => { var o = Read(); _ = ChannelMastering.Graph(o, source.Secondary.Text.Trim() != ""); Status.Text = $"{o.InputLayout} → {o.OutputLayout}, {o.SampleRate:N0} Hz / {o.Bits}-bit. Order: {string.Join(", ", o.Order)}. Gain/delay follow physical output positions after permutation. This review does not render or test audio."; return Task.CompletedTask; });
        Action("Export new multichannel file…", async () =>
        {
            var o = Read(); ChannelMastering.Validate(o); string ext = format.Container.SelectedIndex == 0 ? ".wav" : ".flac";
            if (ext == ".flac" && o.Bits == 32) throw new ArgumentException("Select WAV for 32-bit PCM.");
            var picker = new SaveFileDialog { Filter = "Multichannel audio|*" + ext, DefaultExt = ext, FileName = "Multichannel master" + ext }; if (picker.ShowDialog(this) != true) return;
            Status.Text = "Rendering multichannel copy…";
            await ChannelMastering.Export(source.Primary.Text.Trim(), source.Secondary.Text.Trim(), picker.FileName, o, OperationToken);
            if (!IsClosed) Status.Text = "Saved a new multichannel master: " + picker.FileName;
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "No limiter or automatic safety normalization. Positive gains and summed routes may clip integer output. Two-source mixing ends at the shorter source. Bass management is an electrical crossover, not room calibration or certified acoustic alignment. Double-precision stages are requested before final PCM quantization.";
    }
}

public sealed class VideoFinishingWindow : ProcessingEditor
{
    public VideoFinishingWindow(Window owner, MainViewModel vm) : base(owner, vm, "Video finishing and color", "Create an SDR H.264/AAC copy with stereo-3D conversion, deinterlacing, framing and color finishing. Source color assumptions are manual; these settings do not alter the playback renderer.")
    {
        var source = FileInput("Local video source", CurrentLocal(vm));
        var fields = Group("Interlaced material", () => (Algorithm: Choice("Software deinterlacer", VideoFinishing.Deinterlacers), Parity: Choice("Source field order", ["Auto", "Top first", "Bottom first"])));
        var stereo = Group("Stereoscopic conversion", () => (Input: Choice("Source eye packing (left eye first)", VideoFinishing.StereoInputs), Output: Choice("Output eye packing / anaglyph", VideoFinishing.StereoOutputs)));
        var crop = Group("Overscan crop", () => (Left: Text("Left crop (%)", "0", 3), Right: Text("Right crop (%)", "0", 3), Top: Text("Top crop (%)", "0", 3), Bottom: Text("Bottom crop (%)", "0", 3)));
        var canvas = Group("Canvas, exact placement and anamorphic pixels", () => (Width: Text("Canvas width (even; 128–3,840)", "1280", 5), Height: Text("Canvas height (even; 128–2,160)", "720", 5), X: Text("Image left offset (even pixels)", "0", 5), Y: Text("Image top offset (even pixels)", "0", 5), SarN: Text("Sample aspect numerator (1–100)", "1", 3), SarD: Text("Sample aspect denominator (1–100)", "1", 3)));
        var color = Group("SDR color and 3D LUT", () =>
        {
            var gamut = Toggle("Interpret source as limited-range BT.2020 SDR / BT.709 transfer, then convert to BT.709");
            var matrix = MasteringFields.Multiline(Text("RGB transform: three rows of R/G/B coefficients (−2 to +2)", "1, 0, 0\n0, 1, 0\n0, 0, 1", 600), 90);
            var lut = Text("Optional .cube 3D LUT (empty disables)", "", 32767);
            var browse = new Button { Content = "Choose 3D LUT…", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 7, 12, 7), Margin = new(0, 0, 0, 12) }; Form.Children.Add(browse);
            browse.Click += (_, _) => { var picker = new OpenFileDialog { Filter = "3D LUT|*.cube" }; if (picker.ShowDialog(this) == true) lut.Text = picker.FileName; };
            return (Gamut: gamut, Matrix: matrix, Lut: lut, Interpolation: Choice("LUT interpolation", ["Trilinear", "Tetrahedral"], 1), Legal: Toggle("Clamp final 8-bit luma/chroma to limited-range code values"));
        });
        VideoFinishingOptions Read() => new() { Deinterlace = (string)fields.Algorithm.SelectedItem, FieldOrder = (string)fields.Parity.SelectedItem, StereoInput = (string)stereo.Input.SelectedItem, StereoOutput = (string)stereo.Output.SelectedItem, CropLeft = Integer(crop.Left, 0, 20), CropRight = Integer(crop.Right, 0, 20), CropTop = Integer(crop.Top, 0, 20), CropBottom = Integer(crop.Bottom, 0, 20), Width = Integer(canvas.Width, 128, 3840), Height = Integer(canvas.Height, 128, 2160), X = Integer(canvas.X, 0, 3776), Y = Integer(canvas.Y, 0, 2096), SarNumerator = Integer(canvas.SarN, 1, 100), SarDenominator = Integer(canvas.SarD, 1, 100), RgbMatrix = MasteringFields.Numbers(color.Matrix.Text, 9, -2, 2), Bt2020SdrTo709 = color.Gamut.IsChecked == true, Lut = color.Lut.Text.Trim(), LutInterpolation = (string)color.Interpolation.SelectedItem, Legalize = color.Legal.IsChecked == true };
        LuminanceReport? report = null;
        Action("Review finishing recipe", () => { var o = Read(); _ = VideoFinishing.Graph(o); Status.Text = $"{o.Width}×{o.Height} canvas, image origin {o.X},{o.Y}, sample aspect {o.SarNumerator}:{o.SarDenominator}. Deinterlace {o.Deinterlace}; stereo source {o.StereoInput}. SDR output; recipe review does not render or test it."; return Task.CompletedTask; });
        Action("Measure excerpt luminance", async () => { report = null; Status.Text = "Sampling up to 60 seconds at one frame per second…"; report = await VideoFinishing.AnalyzeLuminance(source.Text.Trim(), OperationToken); if (!IsClosed) Status.Text = $"{report.Samples} samples · frame-average Y min {report.MinimumFrameAverage:0.##}, mean {report.MeanFrameAverage:0.##}, max {report.MaximumFrameAverage:0.##} / 255. {report.Scope}"; });
        Action("Save luminance report…", async () => { if (report == null) throw new ArgumentException("Measure a source excerpt first."); var picker = new SaveFileDialog { Filter = "Luminance report|*.json", DefaultExt = ".json", FileName = "Luminance report.json" }; if (picker.ShowDialog(this) == true) { await VideoFinishing.SaveReport(report, picker.FileName, OperationToken); if (!IsClosed) Status.Text = "Saved a new excerpt-luminance report."; } });
        Action("Export finished copy…", async () => { var o = Read(); VideoFinishing.Validate(o); var picker = new SaveFileDialog { Filter = "SDR H.264/AAC MP4|*.mp4", DefaultExt = ".mp4", FileName = "Finished video.mp4" }; if (picker.ShowDialog(this) != true) return; Status.Text = "Rendering finished copy…"; await VideoFinishing.Export(source.Text.Trim(), picker.FileName, o, OperationToken); if (!IsClosed) Status.Text = "Saved a new finished copy: " + picker.FileName; });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = "Use SDR material; HDR sources need explicit HDR-to-SDR processing first. LUTs: .cube, 2–65 grid points, ≤16 MiB. Crop applies after stereo conversion and a ≤1080p working raster. Exact offsets fit the image in the remaining canvas. 3D output is a file conversion, not VR/headset or HDMI frame-packing support.";
    }
}

public sealed class MasteringFormatsWindow : ProcessingEditor
{
    public MasteringFormatsWindow(Window owner, MainViewModel vm, bool video) : base(owner, vm, video ? "Video mastering formats" : "Audio mastering formats", "Export one local source to a new file using an explicit encoder profile. Installed FFmpeg must include the selected encoder and muxer. No encoder probe runs until you request an export.")
    {
        var source = FileInput("Local source", CurrentLocal(vm));
        var profile = Field("Mastering format", new ComboBox { ItemsSource = MasteringFormats.Profiles.Where(p => p.Video == video).ToArray(), SelectedIndex = 0 });
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) }; Form.Children.Add(detail);
        void Description() => detail.Text = ((MasteringFormat)profile.SelectedItem).Description;
        profile.SelectionChanged += (_, _) => Description(); Description();
        var stream = Text("Audio stream index (zero-based; video export permits missing audio)", "0", 2);
        var duration = Text("Maximum source duration in seconds (0 = full source; up to 7,200)", "0", 4);
        Action("Export new master…", async () =>
        {
            var chosen = (MasteringFormat)profile.SelectedItem; int audio = Integer(stream, 0, 63), seconds = Integer(duration, 0, 7200);
            var picker = new SaveFileDialog { Filter = chosen.Name.Replace('|', '/') + "|*" + chosen.Extension, DefaultExt = chosen.Extension, FileName = "Master" + chosen.Extension }; if (picker.ShowDialog(this) != true) return;
            Status.Text = "Encoding " + chosen.Name + "…"; await MasteringFormats.Export(source.Text.Trim(), picker.FileName, chosen.Name, audio, seconds, OperationToken);
            if (!IsClosed) Status.Text = "Saved a new " + chosen.Name + " file: " + picker.FileName;
        });
        ((StackPanel)Actions.Parent).Children.Add(CancelAction());
        Status.Text = video ? "Video profiles are intended for SDR inputs and up to 1080p. MXF uses a fixed padded 1080p canvas. First video / selected optional audio only; no subtitles or extra tracks. Encoder thread limits are requests, not a measured performance guarantee." : "Audio profiles render the selected track to stereo. DXD-rate export means 352.8 kHz PCM resampling, not native DSD conversion or increased source resolution. RF64 support in other applications varies.";
    }
}
