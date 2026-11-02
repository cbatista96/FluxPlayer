using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public sealed class AudioEffectsWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly Dictionary<string, Slider> _values = new();
    private readonly Dictionary<AudioEffect, CheckBox> _toggles = new();
    private readonly CheckBox _master = new() { Content = "Enable audio processing", Margin = new(0, 0, 0, 14) };
    private readonly ComboBox _group = new() { ItemsSource = new[] { "Parametric EQ", "Tone and stereo", "Dynamics", "Time effects", "Channel controls", "Channel matrix" }, Margin = new(0, 0, 0, 14) };
    private readonly Border _page = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private string? _lastError;
    private readonly List<StackPanel> _groups = new();
    private readonly List<DataGrid> _tables = new();
    private BandRow[] _bands = Array.Empty<BandRow>(); private ChannelRow[] _channels = Array.Empty<ChannelRow>();
    private MatrixRow[] _matrix = Array.Empty<MatrixRow>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public sealed class BandRow { public int Number { get; set; } public float Frequency { get; set; } public float GainDb { get; set; } public float Q { get; set; } }
    public sealed class ChannelRow { public int Number { get; set; } public float GainDb { get; set; } public float DelayMs { get; set; } }
    public sealed class MatrixRow { public int Number { get; set; } public float[] Values { get; set; } = new float[8]; }
    public AudioEffectsWindow(Window owner, MainViewModel vm, int group)
    {
        Owner = owner; _vm = vm; Title = "Audio processing · FluxPlayer"; Width = 960; Height = 760; MinWidth = 840; MinHeight = 660;
        AutomationProperties.SetName(_group, "Audio processing group");
        ExtendsContentIntoTitleBar = true; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new Grid { Margin = new(24, 18, 24, 20) }; foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) body.RowDefinitions.Add(new() { Height = height });
        var heading = new StackPanel { Margin = new(0, 0, 0, 18) }; heading.Children.Add(new TextBlock { Text = "Audio processing", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Native effects. Disabled by default; Apply saves and updates processing. Leave headroom when combining boosts. Logical channel order follows the decoder/output.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) }); body.Children.Add(heading);
        Grid.SetRow(_master, 1); body.Children.Add(_master); Grid.SetRow(_group, 2); body.Children.Add(_group);
        var scroller = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroller, 3); body.Children.Add(scroller);
        var footer = new StackPanel { Margin = new(0, 14, 0, 0) }; var actions = new WrapPanel();
        void Button(string text, Action click) { var button = new Button { Content = text, Padding = new(16, 8, 16, 8), Margin = new(0, 0, 8, 0) }; button.Click += (_, _) => Run(click); actions.Children.Add(button); }
        Button("Apply", Apply); Button("Bypass all", () => { _vm.CommitAudioEffects(_vm.CurrentSettings.AudioEffects with { Enabled = false }); Build(_vm.CurrentSettings.AudioEffects); UpdateStatus(); }); Button("Reset controls", () => Build(new()));
        footer.Children.Add(actions); footer.Children.Add(_status); Grid.SetRow(footer, 4); body.Children.Add(footer); Content = ToolWindowChrome.Wrap(this, body);
        Build(vm.CurrentSettings.AudioEffects); _group.SelectedIndex = Math.Clamp(group, 0, 5); _group.SelectionChanged += (_, _) => Render(); Render();
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        _timer.Tick += (_, _) => UpdateStatus(); Loaded += (_, _) => { UpdateStatus(); _timer.Start(); }; Closed += (_, _) => _timer.Stop();
    }
    private void Run(Action action) { _lastError = null; try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException) { _status.Text = _lastError = ex.Message; } }
    private void Render() { _page.Child = null; if (_group.SelectedIndex >= 0 && _group.SelectedIndex < _groups.Count) _page.Child = _groups[_group.SelectedIndex]; }
    public void SelectGroup(int group) => _group.SelectedIndex = Math.Clamp(group, 0, 5);
    private void Build(AudioEffects settings)
    {
        _page.Child = null; _groups.Clear(); _values.Clear(); _toggles.Clear(); _tables.Clear(); _master.IsChecked = settings.Enabled;
        for (int index = 0; index < 6; index++) _groups.Add(new StackPanel());
        StackPanel Card(int group, AudioEffect effect, string name, string description)
        {
            var panel = new StackPanel { Margin = new(16) }; var surface = new Border { CornerRadius = new(8), Margin = new(0, 0, 0, 12), Child = panel }; surface.SetResourceReference(BackgroundProperty, "FxCardBrush");
            var toggle = new CheckBox { Content = name, IsChecked = settings.Effects.HasFlag(effect), FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) }; AutomationProperties.SetName(toggle, name); _toggles[effect] = toggle; panel.Children.Add(toggle);
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) }); _groups[group].Children.Add(surface); return panel;
        }
        void Value(StackPanel parent, string property, string name, double min, double max, string format = "0.00")
        {
            var row = new Grid { Margin = new(0, 6, 0, 6) }; row.ColumnDefinitions.Add(new() { Width = new(155) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(70) });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }); var slider = new Slider { Minimum = min, Maximum = max, Value = (float)typeof(AudioEffects).GetProperty(property)!.GetValue(settings)!, Margin = new(8, 0, 12, 0), SmallChange = (max - min) / 100 };
            AutomationProperties.SetName(slider, name); Grid.SetColumn(slider, 1); row.Children.Add(slider); _values[property] = slider;
            var label = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; label.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = slider, StringFormat = "{0:" + format + "}" }); Grid.SetColumn(label, 2); row.Children.Add(label); parent.Children.Add(row);
        }
        var peq = Card(0, AudioEffect.Parametric, "Ten-band parametric equalizer", "Frequency: 20–20,000 Hz · Gain: −18 to +18 dB · Q: 0.1–12. A band above Nyquist is bypassed. Set gain to zero to bypass a band.");
        _bands = settings.Bands.Select((band, index) => new BandRow { Number = index + 1, Frequency = band.Frequency, GainDb = band.Enabled ? band.GainDb : 0, Q = band.Q }).ToArray();
        var bandGrid = Table(_bands, ("Band", "Number", true), ("Frequency (Hz)", "Frequency", false), ("Gain (dB)", "GainDb", false), ("Q", "Q", false)); bandGrid.Height = 370; peq.Children.Add(bandGrid); _tables.Add(bandGrid);
        var bass = Card(1, AudioEffect.Bass, "Bass shelf", "Raise or lower low frequencies with a shelving filter."); Value(bass, nameof(AudioEffects.BassDb), "Gain (dB)", -12, 12); Value(bass, nameof(AudioEffects.BassHz), "Shelf frequency (Hz)", 30, 500, "0");
        var cross = Card(1, AudioEffect.Crossfeed, "Headphone crossfeed", "Blend a low-pass signal into the opposite ear. Stereo only."); Value(cross, nameof(AudioEffects.Crossfeed), "Amount", 0, 1); Value(cross, nameof(AudioEffects.CrossfeedHz), "Low-pass (Hz)", 300, 2000, "0");
        var balance = Card(1, AudioEffect.Balance, "Stereo balance", "Negative values attenuate the right side; positive values attenuate the left."); Value(balance, nameof(AudioEffects.Balance), "Balance (left / right)", -1, 1);
        var width = Card(1, AudioEffect.Width, "Stereo width", "Mid/side processing: 0 folds to mono, 1 keeps width, 2 increases side content."); Value(width, nameof(AudioEffects.Width), "Width", 0, 2);
        Card(1, AudioEffect.VoiceReduction, "Center voice reduction", "Subtract the common stereo center. This can remove centered instruments and does not separate vocals using AI. Mono is unaffected.");
        var compressor = Card(2, AudioEffect.Compressor, "Linked compressor", "Peak envelope with attack, release, ratio and makeup gain. Channels share gain reduction to retain the stereo image.");
        Value(compressor, nameof(AudioEffects.ThresholdDb), "Threshold (dBFS)", -60, 0); Value(compressor, nameof(AudioEffects.Ratio), "Ratio", 1, 20); Value(compressor, nameof(AudioEffects.AttackMs), "Attack (ms)", .1, 200); Value(compressor, nameof(AudioEffects.ReleaseMs), "Release (ms)", 10, 2000); Value(compressor, nameof(AudioEffects.MakeupDb), "Makeup (dB)", 0, 18);
        var limiter = Card(2, AudioEffect.Limiter, "Sample-peak limiter", "An instantaneous linked limiter caps processed sample peaks. This is not a look-ahead true-peak limiter."); Value(limiter, nameof(AudioEffects.LimiterDb), "Ceiling (dBFS)", -18, 0);
        var level = Card(2, AudioEffect.Leveling, "Adaptive RMS leveling", "Link channel gain toward an RMS target, with a silence gate and limited boost. This is adaptive volume control, not LUFS/EBU loudness compliance. Leave headroom or enable the limiter."); Value(level, nameof(AudioEffects.LevelTargetDb), "Target (dBFS RMS)", -30, -6); Value(level, nameof(AudioEffects.LevelMaxGainDb), "Maximum boost (dB)", 0, 18); Value(level, nameof(AudioEffects.LevelResponseMs), "Response (ms)", 100, 5000, "0");
        var echo = Card(3, AudioEffect.Echo, "Echo", "A delayed repeat with bounded feedback."); Value(echo, nameof(AudioEffects.EchoMs), "Delay (ms)", 10, 1500); Value(echo, nameof(AudioEffects.EchoMix), "Wet amount", 0, 1); Value(echo, nameof(AudioEffects.EchoFeedback), "Feedback", 0, .8);
        var reverb = Card(3, AudioEffect.Reverb, "Reverb", "A short multi-tap feedback room effect."); Value(reverb, nameof(AudioEffects.ReverbMix), "Wet amount", 0, 1); Value(reverb, nameof(AudioEffects.ReverbDecay), "Decay", 0, .75);
        var flanger = Card(3, AudioEffect.Flanger, "Flanger", "Modulated short delay mixed with the dry signal."); Value(flanger, nameof(AudioEffects.FlangerMix), "Wet amount", 0, 1); Value(flanger, nameof(AudioEffects.FlangerRate), "Rate (Hz)", .1, 5); Value(flanger, nameof(AudioEffects.FlangerDepthMs), "Depth (ms)", 1, 8);
        var chorus = Card(3, AudioEffect.Chorus, "Chorus", "A longer modulated delay with a small inter-channel offset."); Value(chorus, nameof(AudioEffects.ChorusMix), "Wet amount", 0, 1); Value(chorus, nameof(AudioEffects.ChorusRate), "Rate (Hz)", .1, 5); Value(chorus, nameof(AudioEffects.ChorusDepthMs), "Depth (ms)", 2, 20);
        var gains = Card(4, AudioEffect.ChannelGain, "Individual channel gain", "Logical channels 1–8 follow the native DSP order. Missing channels are ignored; physical speaker mapping depends on the output.");
        Card(4, AudioEffect.ChannelDelay, "Individual channel delay", "Enable delays entered in the table below. Range: 0–200 ms. Time buffers are allocated when the effect chain opens, not during an audio block.");
        _channels = Enumerable.Range(0, 8).Select(index => new ChannelRow { Number = index + 1, GainDb = settings.ChannelGains[index], DelayMs = settings.ChannelDelays[index] }).ToArray();
        var channelGrid = Table(_channels, ("Channel", "Number", true), ("Gain (dB)", "GainDb", false), ("Delay (ms)", "DelayMs", false)); channelGrid.Height = 300; gains.Children.Add(channelGrid); _tables.Add(channelGrid);
        var matrix = Card(5, AudioEffect.Matrix, "Logical channel matrix", "Rows are output logical channels; columns are input logical channels. Coefficients −2…2; absolute row sum at most 4. Missing channels are ignored. This does not select a physical speaker layout.");
        _matrix = Enumerable.Range(0, 8).Select(row => new MatrixRow { Number = row + 1, Values = settings.Matrix.Skip(row * 8).Take(8).ToArray() }).ToArray();
        var matrixColumns = new List<(string, string, bool)> { ("Output", "Number", true) }; for (int column = 0; column < 8; column++) matrixColumns.Add(("In " + (column + 1), "Values[" + column + "]", false)); var matrixGrid = Table(_matrix, matrixColumns.ToArray()); matrixGrid.Height = 300; matrix.Children.Add(matrixGrid); _tables.Add(matrixGrid); Render();
    }
    private static DataGrid Table(object source, params (string Name, string Property, bool ReadOnly)[] columns)
    {
        var grid = new DataGrid { ItemsSource = (System.Collections.IEnumerable)source, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 32, GridLinesVisibility = DataGridGridLinesVisibility.None };
        foreach (var column in columns) grid.Columns.Add(new DataGridTextColumn { Header = column.Name, Binding = new Binding(column.Property) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true }, IsReadOnly = column.ReadOnly, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); return grid;
    }
    private void Apply()
    {
        foreach (var table in _tables) if (!table.CommitEdit(DataGridEditingUnit.Cell, true) || !table.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("Finish editing valid numeric table values before applying.");
        float F(string name) => (float)_values[name].Value;
        var settings = new AudioEffects { Enabled = _master.IsChecked == true, Effects = _toggles.Where(pair => pair.Value.IsChecked == true).Aggregate(AudioEffect.None, (flags, pair) => flags | pair.Key),
            BassDb = F(nameof(AudioEffects.BassDb)), BassHz = F(nameof(AudioEffects.BassHz)), Crossfeed = F(nameof(AudioEffects.Crossfeed)), CrossfeedHz = F(nameof(AudioEffects.CrossfeedHz)), ThresholdDb = F(nameof(AudioEffects.ThresholdDb)), Ratio = F(nameof(AudioEffects.Ratio)), AttackMs = F(nameof(AudioEffects.AttackMs)), ReleaseMs = F(nameof(AudioEffects.ReleaseMs)), MakeupDb = F(nameof(AudioEffects.MakeupDb)), LimiterDb = F(nameof(AudioEffects.LimiterDb)), Balance = F(nameof(AudioEffects.Balance)), Width = F(nameof(AudioEffects.Width)), EchoMs = F(nameof(AudioEffects.EchoMs)), EchoFeedback = F(nameof(AudioEffects.EchoFeedback)), EchoMix = F(nameof(AudioEffects.EchoMix)), ReverbMix = F(nameof(AudioEffects.ReverbMix)), ReverbDecay = F(nameof(AudioEffects.ReverbDecay)), FlangerMix = F(nameof(AudioEffects.FlangerMix)), FlangerRate = F(nameof(AudioEffects.FlangerRate)), FlangerDepthMs = F(nameof(AudioEffects.FlangerDepthMs)), ChorusMix = F(nameof(AudioEffects.ChorusMix)), ChorusRate = F(nameof(AudioEffects.ChorusRate)), ChorusDepthMs = F(nameof(AudioEffects.ChorusDepthMs)),
            Bands = _bands.Select(row => new ParametricBand(row.Frequency, row.GainDb, row.Q)).ToImmutableArray(), ChannelGains = _channels.Select(row => row.GainDb).ToImmutableArray(), ChannelDelays = _channels.Select(row => row.DelayMs).ToImmutableArray(), Matrix = _matrix.SelectMany(row => row.Values).ToImmutableArray(), LevelTargetDb = F(nameof(AudioEffects.LevelTargetDb)), LevelMaxGainDb = F(nameof(AudioEffects.LevelMaxGainDb)), LevelResponseMs = F(nameof(AudioEffects.LevelResponseMs)) };
        _vm.CommitAudioEffects(settings); UpdateStatus();
    }
    private void UpdateStatus()
    {
        if (_lastError != null) { _status.Text = _lastError; return; }
        var status = _vm.Engine.ExtendedEqualizerStatus;
        _status.Text = !_vm.Engine.SupportsExtendedEqualizer ? "Build the native DSP module to enable these effects." : status is { Active: true, CurrentSettingsApplied: true, Error: 0 } ? $"Native processing acknowledged · {status.SampleRate:N0} Hz · {status.Channels} logical channels · {status.Effects & ~AudioEffect.Meters}" : _vm.CurrentSettings.AudioEffects.Active == AudioEffect.None ? "Effects bypassed. Changes apply only when saved with Apply." : "Waiting for decoded audio / native acknowledgement. Effects support up to 8 channels at 192 kHz.";
    }
}
