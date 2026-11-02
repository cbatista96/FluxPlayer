using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluxPlayer.Services;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace FluxPlayer.Windows;

/// <summary>Small prompt that jumps to a typed time: seconds, m:ss or h:mm:ss (fractions allowed).</summary>
public sealed class GoToTimeWindow : FluentWindow
{
    private readonly TextBox _input = new() { MinWidth = 220, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, MinHeight = 18, FontSize = 12 };

    public long? TargetMs { get; private set; }

    public GoToTimeWindow(string current, string total)
    {
        Title = Localizer.T("Go to a specific time");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendsContentIntoTitleBar = false;
        ShowInTaskbar = false;
        _input.Text = current;
        var ok = new Button { Content = Localizer.T("Go"), IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Localizer.T("Cancel"), IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Accept();
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel } };
        Content = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 16),
            Children =
            {
                new TextBlock { Text = Localizer.T("Time to jump to") + $" ({total})", FontWeight = FontWeights.SemiBold },
                _input, _error, buttons
            }
        };
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    public static bool TryParse(string text, out long milliseconds)
    {
        milliseconds = 0;
        string[] parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return false;
        double seconds = 0, scale = 1;
        for (int i = parts.Length - 1; i >= 0; i--, scale *= 60)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0 || double.IsInfinity(value)) return false;
            if (i != parts.Length - 1 && value != Math.Floor(value)) return false;
            seconds += value * scale;
        }
        if (seconds > 36000d * 24) return false;
        milliseconds = (long)Math.Round(seconds * 1000);
        return true;
    }

    private void Accept()
    {
        if (!TryParse(_input.Text, out long ms)) { _error.Text = Localizer.T("Use seconds, m:ss or h:mm:ss."); return; }
        TargetMs = ms;
        DialogResult = true;
    }
}
