using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FluxPlayer.Services;

public sealed class WindowDock : IDisposable
{
    private readonly Window _owner, _child; private readonly string _side; private bool _moving;
    public WindowDock(Window owner, Window child, string side)
    { if (side is not ("Left" or "Right")) throw new ArgumentException("Choose Left or Right docking."); _owner = owner; _child = child; _side = side; owner.LocationChanged += Changed; owner.SizeChanged += Sized; child.SizeChanged += Sized; owner.Loaded += Loaded; child.Loaded += Loaded; child.Closed += Closed; Reposition(); }
    private void Loaded(object sender, RoutedEventArgs e) => Reposition();
    private void Changed(object? sender, EventArgs e) => Reposition();
    private void Sized(object sender, SizeChangedEventArgs e) => Reposition();
    private void Closed(object? sender, EventArgs e) => Dispose();
    private void Reposition()
    {
        if (_moving || !_owner.IsLoaded || !_child.IsLoaded || _owner.WindowState == WindowState.Minimized) return;
        _moving = true;
        try
        {
            var work = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(_owner).Handle).WorkingArea; var transform = PresentationSource.FromVisual(_owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point first = transform.Transform(new Point(work.Left, work.Top)), last = transform.Transform(new Point(work.Right, work.Bottom));
            double x = _side == "Left" ? _owner.Left - _child.ActualWidth - 8 : _owner.Left + _owner.ActualWidth + 8;
            _child.Left = Math.Clamp(x, first.X, Math.Max(first.X, last.X - _child.ActualWidth)); _child.Top = Math.Clamp(_owner.Top, first.Y, Math.Max(first.Y, last.Y - _child.ActualHeight));
        }
        finally { _moving = false; }
    }
    public void Dispose() { _owner.LocationChanged -= Changed; _owner.SizeChanged -= Sized; _child.SizeChanged -= Sized; _owner.Loaded -= Loaded; _child.Loaded -= Loaded; _child.Closed -= Closed; }
}
