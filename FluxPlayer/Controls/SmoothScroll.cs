using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluxPlayer.Controls;

public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(ScrollState), typeof(SmoothScroll));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
            fe.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement host) return;
        var sv = host as ScrollViewer ?? FindScrollViewer(host);
        if (sv == null || sv.ScrollableHeight <= 0) return;

        if (e.OriginalSource is DependencyObject src && FindInnerScrollable(src, sv, e.Delta) != null) return;

        var state = (ScrollState?)sv.GetValue(StateProperty);
        if (state == null)
        {
            state = new ScrollState(sv);
            sv.SetValue(StateProperty, state);
        }

        if (sv.CanContentScroll && !IsPixelScrolling(sv)) return;

        e.Handled = true;
        state.AddDelta(-e.Delta * 0.55);
    }

    private static bool IsPixelScrolling(ScrollViewer sv)
    {
        if (sv.TemplatedParent is ItemsControl ic)
            return VirtualizingPanel.GetScrollUnit(ic) == ScrollUnit.Pixel;
        return false;
    }

    private static ScrollViewer? FindInnerScrollable(DependencyObject start, ScrollViewer outer, int delta)
    {
        DependencyObject? cur = start;
        while (cur != null && !ReferenceEquals(cur, outer))
        {
            if (cur is ScrollViewer inner && inner.ScrollableHeight > 0)
            {
                bool canMove = delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;
                if (canMove) return inner;
            }
            cur = cur is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(cur)
                : LogicalTreeHelper.GetParent(cur);
        }
        return null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private sealed class ScrollState
    {
        private readonly ScrollViewer _sv;
        private double _target;
        private double _current;
        private double _lastApplied = -1;
        private bool _running;
        private TimeSpan _lastFrame;

        public ScrollState(ScrollViewer sv)
        {
            _sv = sv;

            sv.ScrollChanged += (_, e) =>
            {
                if (Math.Abs(e.VerticalChange) > 0 && Math.Abs(sv.VerticalOffset - _lastApplied) > 1)
                    _current = _target = _lastApplied = sv.VerticalOffset;
            };
        }

        public void AddDelta(double delta)
        {
            if (!_running)
                _current = _target = _sv.VerticalOffset;

            _target = Math.Clamp(_target + delta, 0, _sv.ScrollableHeight);
            if (_running) return;

            _running = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
            if (now == _lastFrame) return;
            double dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
            _lastFrame = now;
            dt = Math.Clamp(dt, 0.001, 0.05);

            const double tau = 0.07;
            _current += (_target - _current) * (1 - Math.Exp(-dt / tau));

            double scale = VisualTreeHelper.GetDpi(_sv).DpiScaleY;
            bool done = Math.Abs(_target - _current) < 0.5 / scale;
            double applied = done ? _target : _current;
            applied = Math.Round(applied * scale) / scale;
            _lastApplied = applied;
            _sv.ScrollToVerticalOffset(applied);

            if (done || !_sv.IsVisible)
            {
                _current = _target;
                _running = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }
}
