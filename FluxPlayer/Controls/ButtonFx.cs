using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FluxPlayer.Controls;

public static class ButtonFx
{
    public static bool ReduceMotion { get; set; }

    private static readonly List<WeakReference<FrameworkElement>> KeyActionElements = new();

    public static readonly DependencyProperty IconEffectProperty =
        DependencyProperty.RegisterAttached("IconEffect", typeof(string), typeof(ButtonFx),
            new PropertyMetadata(null, OnIconEffectChanged));

    public static string? GetIconEffect(DependencyObject d) => (string?)d.GetValue(IconEffectProperty);
    public static void SetIconEffect(DependencyObject d, string? value) => d.SetValue(IconEffectProperty, value);

    public static readonly DependencyProperty KeyActionsProperty =
        DependencyProperty.RegisterAttached("KeyActions", typeof(string), typeof(ButtonFx),
            new PropertyMetadata(null, OnKeyActionsChanged));

    public static string? GetKeyActions(DependencyObject d) => (string?)d.GetValue(KeyActionsProperty);
    public static void SetKeyActions(DependencyObject d, string? value) => d.SetValue(KeyActionsProperty, value);

    private static readonly DependencyProperty IconTransformProperty =
        DependencyProperty.RegisterAttached("IconTransform", typeof(TransformGroup), typeof(ButtonFx));

    private static readonly DependencyProperty PulseTransformProperty =
        DependencyProperty.RegisterAttached("PulseTransform", typeof(ScaleTransform), typeof(ButtonFx));

    private static void OnIconEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnButtonClick;
        if (e.NewValue is string { Length: > 0 } effect && effect != "None")
            button.Click += OnButtonClick;
    }

    private static void OnKeyActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && e.NewValue is string { Length: > 0 } && e.OldValue == null)
            KeyActionElements.Add(new WeakReference<FrameworkElement>(fe));
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) PlayIconEffect(fe);
    }

    public static IEnumerable<FrameworkElement> GetButtonsForAction(string action)
    {
        KeyActionElements.RemoveAll(w => !w.TryGetTarget(out _));
        var seen = new HashSet<FrameworkElement>();
        foreach (var weak in KeyActionElements.ToArray())
        {
            if (weak.TryGetTarget(out var fe) && Matches(GetKeyActions(fe), action) && seen.Add(fe))
                yield return fe;
        }
        foreach (var fe in HotkeyHint.GetElements(action))
        {
            if (fe is ButtonBase && seen.Add(fe))
                yield return fe;
        }
    }

    private static bool Matches(string? actions, string action)
        => actions != null && actions
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));

    public static void Pulse(FrameworkElement button)
    {
        if (ReduceMotion || !button.IsVisible || button.ActualWidth <= 0) return;

        PlayIconEffect(button);

        if (GetPulseTransform(button) is { } st)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.84, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)),
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        ShowRipple(button);
    }

    public static void PlayIconEffect(FrameworkElement button)
    {
        if (ReduceMotion || !button.IsVisible) return;
        string? effect = GetIconEffect(button);
        if (string.IsNullOrEmpty(effect) || effect == "None") return;

        var target = FindIconTarget(button);
        if (target == null) return;
        var group = GetIconTransform(target);
        var scale = (ScaleTransform)group.Children[0];
        var rotate = (RotateTransform)group.Children[1];
        var move = (TranslateTransform)group.Children[2];

        switch (effect)
        {
            case "SpinCW":
            case "SpinCCW":
                double dir = effect == "SpinCW" ? 1 : -1;
                Run(rotate, RotateTransform.AngleProperty, 560,
                    (0, 0, null),
                    (90, -22 * dir, new CubicEase { EasingMode = EasingMode.EaseOut }),
                    (560, 360 * dir, new CubicEase { EasingMode = EasingMode.EaseOut }));
                Run(scale, ScaleTransform.ScaleXProperty, 560, (0, 1, null), (90, 0.86, null),
                    (560, 1, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
                Run(scale, ScaleTransform.ScaleYProperty, 560, (0, 1, null), (90, 0.86, null),
                    (560, 1, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
                break;

            case "NudgeLeft":
            case "NudgeRight":
                double dx = effect == "NudgeRight" ? 6 : -6;
                Run(move, TranslateTransform.XProperty, 420,
                    (0, 0, null),
                    (110, dx, new CubicEase { EasingMode = EasingMode.EaseOut }),
                    (420, 0, new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 }));
                break;

            case "Wiggle":
                Run(rotate, RotateTransform.AngleProperty, 520,
                    (0, 0, null), (80, -16, null), (180, 13, null), (280, -8, null), (380, 4, null), (520, 0, null));
                break;

            case "Bounce":
                Run(move, TranslateTransform.YProperty, 460,
                    (0, 0, null),
                    (120, -5, new CubicEase { EasingMode = EasingMode.EaseOut }),
                    (460, 0, new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 2.5 }));
                break;

            case "Snap":

                Run(scale, ScaleTransform.ScaleXProperty, 460, (0, 1, null), (70, 0.62, null),
                    (200, 1.22, new CubicEase { EasingMode = EasingMode.EaseOut }), (460, 1, null));
                Run(scale, ScaleTransform.ScaleYProperty, 460, (0, 1, null), (70, 0.62, null),
                    (200, 1.22, new CubicEase { EasingMode = EasingMode.EaseOut }), (460, 1, null));
                Run(target, UIElement.OpacityProperty, 300, (0, 1, null), (70, 0.35, null), (300, 1, null));
                break;

            default:
                Run(scale, ScaleTransform.ScaleXProperty, 420, (0, 1, null), (90, 0.78, null),
                    (420, 1, new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 }));
                Run(scale, ScaleTransform.ScaleYProperty, 420, (0, 1, null), (90, 0.78, null),
                    (420, 1, new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 }));
                break;
        }
    }

    private static void Run(IAnimatable target, DependencyProperty property, int totalMs,
        params (int Ms, double Value, IEasingFunction? Ease)[] frames)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(totalMs),
            FillBehavior = FillBehavior.Stop
        };
        foreach (var (ms, value, ease) in frames)
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), ease));
        target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private static TransformGroup GetIconTransform(FrameworkElement target)
    {
        if (target.GetValue(IconTransformProperty) is TransformGroup existing &&
            ReferenceEquals(target.RenderTransform, existing))
            return existing;

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform());
        group.Children.Add(new RotateTransform());
        group.Children.Add(new TranslateTransform());
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        target.RenderTransform = group;
        target.SetValue(IconTransformProperty, group);
        return group;
    }

    private static ScaleTransform? GetPulseTransform(FrameworkElement button)
    {
        if (button.GetValue(PulseTransformProperty) is ScaleTransform existing &&
            ReferenceEquals(button.RenderTransform, existing))
            return existing;

        if (button.RenderTransform != null && !button.RenderTransform.Value.IsIdentity)
            return null;

        var st = new ScaleTransform();
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = st;
        button.SetValue(PulseTransformProperty, st);
        return st;
    }

    private static FrameworkElement? FindIconTarget(DependencyObject root)
    {
        FrameworkElement? glyph = null;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!ReferenceEquals(node, root) && node is FrameworkElement fe)
            {
                if (fe.Name == "fxIcon" || fe is PlayPauseIcon) return fe;
                if (glyph == null && fe is TextBlock tb && IsIconFont(tb.FontFamily)) glyph = tb;
            }
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return glyph;
    }

    private static bool IsIconFont(FontFamily? family)
    {
        string source = family?.Source ?? string.Empty;
        return source.Contains("Fluent Icons", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("MDL2", StringComparison.OrdinalIgnoreCase);
    }

    private static void ShowRipple(FrameworkElement button)
    {
        var layer = AdornerLayer.GetAdornerLayer(button);
        if (layer == null) return;

        var brush = button.TryFindResource("AccentBrush") as Brush ?? Brushes.DeepSkyBlue;
        var adorner = new RippleAdorner(button, brush);
        layer.Add(adorner);
        adorner.Start(() => layer.Remove(adorner));
    }

    private sealed class RippleAdorner : Adorner
    {
        private readonly Ellipse _ring;

        public RippleAdorner(UIElement adorned, Brush brush) : base(adorned)
        {
            IsHitTestVisible = false;
            var fill = brush.CloneCurrentValue();
            fill.Opacity = 0.22;
            fill.Freeze();
            _ring = new Ellipse
            {
                Stroke = brush,
                StrokeThickness = 2,
                Fill = fill,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.55, 0.55)
            };
            AddVisualChild(_ring);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _ring;

        protected override Size MeasureOverride(Size constraint)
        {
            _ring.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = AdornedElement.RenderSize;
            double d = Math.Max(size.Width, size.Height);
            _ring.Arrange(new Rect((size.Width - d) / 2, (size.Height - d) / 2, d, d));
            return finalSize;
        }

        public void Start(Action onDone)
        {
            var duration = TimeSpan.FromMilliseconds(520);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0.55, 1.55, duration) { EasingFunction = ease };
            var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration), ease));
            fade.Completed += (_, _) => onDone();

            var st = (ScaleTransform)_ring.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            _ring.BeginAnimation(OpacityProperty, fade);
        }
    }
}
