using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FluxPlayer.Windows;

/// <summary>Monitor-sized presentation with exact restoration of the owned window and its chrome.</summary>
internal sealed class ToolWindowFullscreen(Window window, FrameworkElement body, params UIElement[] chrome)
{
    private Rect _bounds;
    private WindowState _state;
    private WindowStyle _style;
    private ResizeMode _resize;
    private bool _topmost;
    private Thickness _margin;
    private Visibility[] _visibility = [];
    private int _corner;
    public bool IsActive { get; private set; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int bytes);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int bytes);
    public void Toggle()
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!IsActive)
        {
            _state = window.WindowState; _style = window.WindowStyle; _resize = window.ResizeMode; _topmost = window.Topmost; _margin = body.Margin;
            _bounds = _state == WindowState.Normal ? new(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
            _visibility = chrome.Select(element => element.Visibility).ToArray(); foreach (var element in chrome) element.Visibility = Visibility.Collapsed; body.Margin = new(0);
            _ = DwmGetWindowAttribute(handle, 33, out _corner, sizeof(int)); int square = 1; _ = DwmSetWindowAttribute(handle, 33, ref square, sizeof(int));
            window.WindowState = WindowState.Normal; window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize; window.Topmost = true;
            var screen = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
            var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point first = transform.Transform(new Point(screen.Left, screen.Top)), last = transform.Transform(new Point(screen.Right, screen.Bottom));
            window.Left = first.X; window.Top = first.Y; window.Width = last.X - first.X; window.Height = last.Y - first.Y; IsActive = true;
        }
        else
        {
            window.Topmost = _topmost; window.WindowStyle = _style; window.ResizeMode = _resize; window.WindowState = WindowState.Normal;
            for (int index = 0; index < chrome.Length; index++) chrome[index].Visibility = _visibility[index]; body.Margin = _margin;
            window.Left = _bounds.Left; window.Top = _bounds.Top; window.Width = _bounds.Width; window.Height = _bounds.Height; window.WindowState = _state;
            _ = DwmSetWindowAttribute(handle, 33, ref _corner, sizeof(int)); IsActive = false;
        }
    }
}
