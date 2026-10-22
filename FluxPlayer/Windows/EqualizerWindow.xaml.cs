using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace FluxPlayer.Windows;

public partial class EqualizerWindow : FluentWindow
{
    private const double KnobRadius = 9;
    private const double MaxDb = 20;

    private readonly MainViewModel _vm;
    private Slider[] _bands = Array.Empty<Slider>();
    private TextBlock[] _values = Array.Empty<TextBlock>();
    private bool _syncing;
    private bool _syncPending;
    private bool _cloaked;

    public EqualizerWindow(MainViewModel vm, AppTheme theme)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        LayoutCombo.ItemsSource = new[] { new LayoutChoice(10, "10 bands · legacy", true),
            new LayoutChoice(20, "20 bands · logarithmic", vm.SupportsExtendedEqualizer), new LayoutChoice(31, "31 bands · ISO", vm.SupportsExtendedEqualizer) };
        LayoutCombo.ItemContainerStyle = new Style(typeof(ComboBoxItem), TryFindResource(typeof(ComboBoxItem)) as Style)
        { Setters = { new Setter(IsEnabledProperty, new Binding(nameof(LayoutChoice.Available))) } };

        SourceInitialized += (_, _) =>
        {
            SetCloak(true);
            ThemeService.Apply(theme, this);
            ResizeForLayout();
        };
        ContentRendered += async (_, _) =>
        {
            await Task.Delay(30);
            SetCloak(false);
        };

        BuildBands();
        PresetCombo.ItemsSource = EqualizerPresets.Names;
        SyncFromVm();

        _vm.PropertyChanged += Vm_PropertyChanged;
        Closed += (_, _) => _vm.PropertyChanged -= Vm_PropertyChanged;

        CurveCanvas.SizeChanged += (_, _) => DrawCurve();
        GridCanvas.SizeChanged += (_, _) => DrawGrid();
    }

    public void SyncTheme(AppTheme theme) => ThemeService.Apply(theme, this);

    private sealed record LayoutChoice(int Count, string Label, bool Available)
    { public override string ToString() => Label; }

    private void ResizeForLayout()
    {
        var reference = Owner ?? this;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(reference).Handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(reference);
        double workWidth = area.Width / dpi.DpiScaleX;
        Width = Math.Min(_vm.EqualizerBandCount switch { 20 => 960, 31 => 1220, _ => 660 }, Math.Max(600, workWidth - 32));
        if (IsLoaded && !double.IsNaN(Left))
            Left = Math.Clamp(Left, area.Left / dpi.DpiScaleX + 8, Math.Max(area.Left / dpi.DpiScaleX + 8, area.Right / dpi.DpiScaleX - Width - 8));
    }

    private void BuildBands()
    {
        int count = _vm.EqualizerBandCount;
        BandSliders.Children.Clear(); BandValues.Children.Clear(); BandLabels.Children.Clear();
        _bands = new Slider[count]; _values = new TextBlock[count];
        BandSliders.Columns = BandValues.Columns = BandLabels.Columns = count;
        EqualizerPlot.MinWidth = count * 31 + 94;
        ResizeForLayout();
        var style = (Style)FindResource("EqSlider");
        double[] frequencies = EqualizerLayout.Frequencies(count);
        for (int i = 0; i < count; i++)
        {
            int band = i;
            string freq = EqualizerLayout.Label(frequencies[i]);

            var value = new TextBlock
            {
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Text = "0.0"
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            _values[i] = value;
            BandValues.Children.Add(value);

            var slider = new Slider { Style = style, ToolTip = $"{frequencies[i]:0.###} Hz (double-click: 0 dB)" };
            System.Windows.Automation.AutomationProperties.SetName(slider, $"Equalizer {frequencies[i]:0.###} Hz");
            slider.ValueChanged += (_, e) =>
            {
                UpdateValueText(_values[band], e.NewValue);
                DrawCurve();
                if (!_syncing) _vm.SetEqualizerBand(band, (float)e.NewValue);
            };
            slider.MouseDoubleClick += (_, e) => { slider.Value = 0; e.Handled = true; };
            _bands[i] = slider;
            BandSliders.Children.Add(slider);

            var label = new TextBlock
            {
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Text = freq
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            BandLabels.Children.Add(label);
        }
    }

    private static void UpdateValueText(TextBlock tb, double v)
    {
        tb.Text = v > 0.04 ? $"+{v:0.0}" : v < -0.04 ? $"{v:0.0}" : "0.0";
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing) return;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.EqualizerBands):
            case nameof(MainViewModel.EqualizerPreamp):
            case nameof(MainViewModel.EqualizerPreset):
            case nameof(MainViewModel.IsEqualizerEnabled):
            case nameof(MainViewModel.EqualizerAntiClip):
            case nameof(MainViewModel.EffectiveEqualizerPreamp):
            case nameof(MainViewModel.EqualizerBackendStatus):
            case nameof(MainViewModel.EqualizerBandCount):
                if (!_syncPending)
                {
                    _syncPending = true;
                    Dispatcher.BeginInvoke(() => { _syncPending = false; SyncFromVm(); });
                }
                break;
        }
    }

    private void SyncFromVm()
    {
        _syncing = true;
        try
        {
            if (_bands.Length != _vm.EqualizerBandCount) BuildBands();
            LayoutCombo.SelectedItem = LayoutCombo.Items.Cast<LayoutChoice>().First(choice => choice.Count == _vm.EqualizerBandCount);
            EnableSwitch.IsChecked = _vm.IsEqualizerEnabled;
            EnableSwitch.IsEnabled = _vm.EqualizerBandCount == 10 || _vm.SupportsExtendedEqualizer;
            AntiClipCheck.IsChecked = _vm.EqualizerAntiClip;
            if (!Equals(PresetCombo.SelectedItem, _vm.EqualizerPreset))
                PresetCombo.SelectedItem = _vm.EqualizerPreset;
            if (Math.Abs(PreampSlider.Value - _vm.EqualizerPreamp) > 0.001)
                PreampSlider.Value = _vm.EqualizerPreamp;
            UpdateValueText(PreampValue, PreampSlider.Value);
            for (int i = 0; i < _bands.Length; i++)
            {
                if (Math.Abs(_bands[i].Value - _vm.EqualizerBands[i]) > 0.001)
                    _bands[i].Value = _vm.EqualizerBands[i];
                UpdateValueText(_values[i], _bands[i].Value);
            }
        }
        finally { _syncing = false; }

        DrawCurve();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool on = _vm.IsEqualizerEnabled;
        CurveLine.Opacity = on ? 0.95 : 0.35;
        CurveFill.Opacity = on ? 0.22 : 0.08;
        StatusText.Text = (on ? $"Effective input gain: {_vm.EffectiveEqualizerPreamp:+0.0;-0.0;0.0} dB · " : "Off · ") + _vm.EqualizerBackendStatus;
    }

    private double DbToY(double db, double h)
        => KnobRadius + (MaxDb - db) / (2 * MaxDb) * Math.Max(1, h - 2 * KnobRadius);

    private void DrawGrid()
    {
        GridCanvas.Children.Clear();
        ScaleCanvas.Children.Clear();
        double w = GridCanvas.ActualWidth, h = GridCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        foreach (double db in new[] { 20.0, 10.0, 0.0, -10.0, -20.0 })
        {
            double y = Math.Round(DbToY(db, h)) + 0.5;
            var line = new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = y,
                Y2 = y,
                StrokeThickness = 1,
                Opacity = db == 0 ? 0.9 : 0.55,
                StrokeDashArray = db == 0 ? null : new DoubleCollection { 2, 3 },
                SnapsToDevicePixels = true
            };
            line.SetResourceReference(Shape.StrokeProperty, "FxDividerBrush");
            GridCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = db > 0 ? $"+{db:0}" : $"{db:0}",
                FontSize = 10,
                Width = 28,
                TextAlignment = TextAlignment.Right
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);
            ScaleCanvas.Children.Add(label);
        }
    }

    private void DrawCurve()
    {
        double w = CurveCanvas.ActualWidth, h = CurveCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (_bands.Length == 0) return;
        var pts = new List<Point>(_bands.Length + 2);
        double step = w / _bands.Length;
        pts.Add(new Point(0, DbToY(_bands[0].Value, h)));
        for (int i = 0; i < _bands.Length; i++)
            pts.Add(new Point((i + 0.5) * step, DbToY(_bands[i].Value, h)));
        pts.Add(new Point(w, DbToY(_bands[^1].Value, h)));

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var c = line.Open()) Trace(c, pts, false, h, w);
        using (var c = fill.Open()) Trace(c, pts, true, h, w);
        line.Freeze();
        fill.Freeze();
        CurveLine.Data = line;
        CurveFill.Data = fill;
    }

    private static void Trace(StreamGeometryContext c, List<Point> p, bool closed, double h, double w)
    {
        if (closed)
        {
            c.BeginFigure(new Point(0, h), true, true);
            c.LineTo(p[0], false, false);
        }
        else
        {
            c.BeginFigure(p[0], false, false);
        }

        for (int i = 0; i < p.Count - 1; i++)
        {
            Point p0 = i > 0 ? p[i - 1] : p[i];
            Point p1 = p[i];
            Point p2 = p[i + 1];
            Point p3 = i + 2 < p.Count ? p[i + 2] : p2;
            var cp1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var cp2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            c.BezierTo(cp1, cp2, p2, true, true);
        }

        if (closed) c.LineTo(new Point(w, h), false, false);
    }

    private void PreampSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateValueText(PreampValue, e.NewValue);
        if (_syncing) return;
        _vm.SetEqualizerPreamp((float)e.NewValue);
        UpdateStatus();
    }

    private void PreampSlider_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        PreampSlider.Value = 0;
        e.Handled = true;
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PresetCombo.SelectedItem is not string name) return;
        if (name == _vm.EqualizerPreset) return;
        _vm.ApplyEqPresetCommand.Execute(name);

        if (!_vm.IsEqualizerEnabled && name != EqualizerPresets.Custom)
            _vm.IsEqualizerEnabled = true;
    }

    private void StepPreset(int delta)
    {
        var names = EqualizerPresets.All.Select(p => p.Name).ToList();
        int idx = names.IndexOf(_vm.EqualizerPreset);
        idx = idx < 0 ? 0 : (idx + delta + names.Count) % names.Count;
        PresetCombo.SelectedItem = names[idx];
    }

    private void PrevPreset_Click(object sender, RoutedEventArgs e) => StepPreset(-1);
    private void NextPreset_Click(object sender, RoutedEventArgs e) => StepPreset(1);

    private void LayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LayoutCombo.SelectedItem is not LayoutChoice choice) return;
        if (!_vm.SetEqualizerLayout(choice.Count)) StatusText.Text = "Build the native DSP module to enable this layout.";
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _vm.ResetEqualizerCommand.Execute(null);

    private void EnableSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _vm.IsEqualizerEnabled = EnableSwitch.IsChecked == true;
        UpdateStatus();
    }

    private void AntiClip_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _vm.EqualizerAntiClip = AntiClipCheck.IsChecked == true;
        UpdateStatus();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.None
                                    && Keyboard.FocusedElement is not ComboBox))
        {
            Close();
            e.Handled = true;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void SetCloak(bool cloak)
    {
        if (_cloaked == cloak) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int v = cloak ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 13, ref v, sizeof(int));
        _cloaked = cloak;
    }
}
