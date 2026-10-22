using System.Windows;

namespace FluxPlayer.Controls;

public static class HotkeyHint
{
    private static readonly List<WeakReference<FrameworkElement>> Elements = new();

    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.RegisterAttached("Action", typeof(string), typeof(HotkeyHint),
            new PropertyMetadata(null, OnActionChanged));

    private static readonly DependencyProperty BaseTextProperty =
        DependencyProperty.RegisterAttached("BaseText", typeof(string), typeof(HotkeyHint),
            new PropertyMetadata(null));

    public static string? GetAction(DependencyObject d) => (string?)d.GetValue(ActionProperty);
    public static void SetAction(DependencyObject d, string? value) => d.SetValue(ActionProperty, value);

    private static void OnActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && e.NewValue is string { Length: > 0 })
            Elements.Add(new WeakReference<FrameworkElement>(fe));
    }

    public static void Refresh(Func<string, string> gestureText)
    {
        Elements.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in Elements)
        {
            if (!weak.TryGetTarget(out var fe)) continue;
            if (fe is System.Windows.Controls.MenuItem menuItem)
            {
                menuItem.InputGestureText = string.Join(" / ", SplitKeys(GetAction(fe), gestureText));
                continue;
            }
            var baseText = (string?)fe.GetValue(BaseTextProperty);
            if (baseText == null)
            {
                if (fe.ToolTip is not string tip) continue;
                baseText = tip;
                fe.SetValue(BaseTextProperty, baseText);
            }

            var keys = SplitKeys(GetAction(fe), gestureText);
            fe.ToolTip = keys.Count == 0 ? baseText : $"{baseText} ({string.Join(" / ", keys)})";
        }
    }

    public static IEnumerable<FrameworkElement> GetElements(string action)
    {
        Elements.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in Elements.ToArray())
        {
            if (!weak.TryGetTarget(out var fe)) continue;
            var actions = GetAction(fe);
            if (actions == null) continue;
            foreach (var a in actions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(a, action, StringComparison.OrdinalIgnoreCase))
                {
                    yield return fe;
                    break;
                }
            }
        }
    }

    private static List<string> SplitKeys(string? actions, Func<string, string> gestureText)
        => (actions ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(gestureText)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();
}
