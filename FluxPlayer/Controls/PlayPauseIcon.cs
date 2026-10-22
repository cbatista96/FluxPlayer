using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluxPlayer.Controls;

public sealed class PlayPauseIcon : FrameworkElement
{
    private const double Box = 18.0;

    private static readonly Point[] PlayLeft = { new(4.5, 2), new(10.25, 5.4), new(10.25, 12.6), new(4.5, 16) };
    private static readonly Point[] PlayRight = { new(10.25, 5.4), new(16, 9), new(16, 9), new(10.25, 12.6) };
    private static readonly Point[] PauseLeft = { new(3.5, 2.5), new(7.6, 2.5), new(7.6, 15.5), new(3.5, 15.5) };
    private static readonly Point[] PauseRight = { new(10.4, 2.5), new(14.5, 2.5), new(14.5, 15.5), new(10.4, 15.5) };

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(PlayPauseIcon),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public static readonly DependencyProperty ProgressProperty =
    DependencyProperty.Register(nameof(Progress), typeof(double), typeof(PlayPauseIcon),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(PlayPauseIcon),
            new FrameworkPropertyMetadata(Brushes.White,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public PlayPauseIcon()
    {
        SnapsToDevicePixels = false;
        Loaded += (_, _) => { if (!HasAnimatedProperties) Progress = IsPlaying ? 1 : 0; };
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (PlayPauseIcon)d;
        double target = (bool)e.NewValue ? 1 : 0;

        if (!icon.IsLoaded || ButtonFx.ReduceMotion)
        {
            icon.BeginAnimation(ProgressProperty, null);
            icon.Progress = target;
            return;
        }

        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        anim.Completed += (_, _) =>
        {

            icon.BeginAnimation(ProgressProperty, null);
            icon.Progress = icon.IsPlaying ? 1 : 0;
        };
        icon.BeginAnimation(ProgressProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double s = Math.Min(w, h) / Box;
        double ox = (w - Box * s) / 2, oy = (h - Box * s) / 2;
        double p = Math.Clamp(Progress, 0, 1);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            AddQuad(ctx, PlayLeft, PauseLeft, p, s, ox, oy);
            AddQuad(ctx, PlayRight, PauseRight, p, s, ox, oy);
        }
        geometry.Freeze();

        var brush = Foreground ?? Brushes.White;

        var pen = new Pen(brush, 1.3 * s) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(brush, pen, geometry);
    }

    private static void AddQuad(StreamGeometryContext ctx, Point[] from, Point[] to, double p,
        double s, double ox, double oy)
    {
        Point Lerp(int i) => new(
            ox + (from[i].X + (to[i].X - from[i].X) * p) * s,
            oy + (from[i].Y + (to[i].Y - from[i].Y) * p) * s);

        ctx.BeginFigure(Lerp(0), isFilled: true, isClosed: true);
        ctx.LineTo(Lerp(1), isStroked: true, isSmoothJoin: true);
        ctx.LineTo(Lerp(2), isStroked: true, isSmoothJoin: true);
        ctx.LineTo(Lerp(3), isStroked: true, isSmoothJoin: true);
    }
}
