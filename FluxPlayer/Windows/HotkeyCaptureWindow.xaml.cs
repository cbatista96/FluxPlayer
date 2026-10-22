using FluxPlayer.Models;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace FluxPlayer.Windows;

public partial class HotkeyCaptureWindow : Window
{
    public string? CapturedKey { get; private set; }
    public string ActionName { get; set; } = string.Empty;
    public string CurrentBinding { get; set; } = string.Empty;

    public Func<string, (bool Ok, string? Message)>? Validator { get; set; }

    public HotkeyCaptureWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ActionLabel.Text = ActionName;
            CaptureDisplay.Text = string.IsNullOrEmpty(CurrentBinding) ? "—" : KeybindingRules.ToDisplay(CurrentBinding);
            Focus();
            AnimateIn();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            DialogResult = false;
            return;
        }

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
        {
            CaptureDisplay.Text = BuildModifiers() + "+...";
            e.Handled = true;
            return;
        }

        var combo = BuildKeyCombo(key);
        CaptureDisplay.Text = KeybindingRules.ToDisplay(combo);
        e.Handled = true;

        bool ok;
        string? message;
        if (Validator != null)
        {
            (ok, message) = Validator(combo);
        }
        else
        {
            message = KeybindingRules.GetReservedReason(combo);
            ok = message == null;
        }
        CapturedKey = ok ? combo : null;
        AcceptBtn.IsEnabled = ok;
        MessageText.Text = string.IsNullOrEmpty(message) ? " " : message;
        MessageText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            !ok ? "FxDangerTextBrush" : string.IsNullOrEmpty(message) ? "TextFillColorTertiaryBrush" : "FxWarnTextBrush");
        CaptureBox.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty,
            ok ? "FxAccentBorderBrush" : "FxDangerBorderBrush");
        if (!ok) Shake();
    }

    private void Shake()
    {
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        foreach (var (ms, x) in new[] { (50, -6.0), (110, 5.0), (170, -4.0), (230, 2.0), (320, 0.0) })
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        var t = new System.Windows.Media.TranslateTransform();
        CaptureBox.RenderTransform = t;
        t.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, shake);
    }

    private static string BuildModifiers()
    {
        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts.Count == 0 ? string.Empty : string.Join("+", parts);
    }

    private static string BuildKeyCombo(Key key)
    {
        var modPart = BuildModifiers();
        var keyPart = key.ToString();
        return string.IsNullOrEmpty(modPart) ? keyPart : modPart + "+" + keyPart;
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = !string.IsNullOrEmpty(CapturedKey);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
