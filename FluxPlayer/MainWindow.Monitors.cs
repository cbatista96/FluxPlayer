using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FluxPlayer;

/// <summary>Multi-monitor behaviour: which screen fullscreen uses, black-out of the others, keeping the window reachable, and the colour behind the video.</summary>
public partial class MainWindow
{
    private readonly MonitorBlackout _blackout = new();
    private DispatcherTimer? _keepOnScreenTimer;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private void InitializeMonitorOptions()
    {
        _keepOnScreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _keepOnScreenTimer.Tick += (_, _) =>
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed) return;   // still being dragged
            _keepOnScreenTimer.Stop();
            KeepWindowReachable();
        };
        LocationChanged += (_, _) =>
        {
            if (!_runtimeSettings.KeepWindowOnScreen || _isFullscreen || WindowState != WindowState.Normal) return;
            _keepOnScreenTimer.Stop(); _keepOnScreenTimer.Start();
        };
        Closed += (_, _) => _blackout.Dispose();
    }

    private Matrix DeviceToDips() => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

    private static Rect ToDips(System.Drawing.Rectangle device, Matrix fromDevice)
    {
        var topLeft = fromDevice.Transform(new Point(device.Left, device.Top));
        var bottomRight = fromDevice.Transform(new Point(device.Right, device.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void KeepWindowReachable()
    {
        if (!_runtimeSettings.KeepWindowOnScreen || _isFullscreen || WindowState != WindowState.Normal || !IsLoaded) return;
        var fromDevice = DeviceToDips();
        var areas = System.Windows.Forms.Screen.AllScreens.Select(screen => ToDips(screen.WorkingArea, fromDevice)).ToList();
        var window = new Rect(Left, Top, ActualWidth, ActualHeight);
        var fixedRect = MonitorPlan.KeepOnScreen(window, areas);
        if (fixedRect == window) return;
        Left = fixedRect.Left; Top = fixedRect.Top; Width = fixedRect.Width; Height = fixedRect.Height;
    }

    /// <summary>The screen fullscreen should open on, following the "Fullscreen monitor" setting.</summary>
    private System.Windows.Forms.Screen ChooseFullscreenScreen(IntPtr hwnd)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var here = System.Windows.Forms.Screen.FromHandle(hwnd);
        int current = Array.FindIndex(screens, screen => screen.DeviceName == here.DeviceName), primary = Array.FindIndex(screens, screen => screen.Primary);
        return screens[MonitorPlan.ChooseFullscreenMonitor(_runtimeSettings.FullscreenMonitor, current, primary, screens.Length)];
    }

    /// <summary>Puts the (borderless) window exactly on <paramref name="target"/> in device pixels, which is reliable across monitors with different scaling.</summary>
    private void PlaceOnScreen(IntPtr hwnd, System.Windows.Forms.Screen target)
    {
        var b = target.Bounds;
        SetWindowPos(hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height, 0x0004 | 0x0010);   // no z-order change, no activation
    }

    private void UpdateBlackout(System.Windows.Forms.Screen used)
    {
        if (!_runtimeSettings.BlackOutOtherMonitors) { _blackout.Hide(); return; }
        _blackout.Show(System.Windows.Forms.Screen.AllScreens.Where(screen => screen.DeviceName != used.DeviceName).Select(screen => screen.Bounds));
    }

    private void ApplyVideoBackground()
    {
        if (!MonitorPlan.TryParseColor(_runtimeSettings.VideoBackgroundColor, out byte r, out byte g, out byte b)) return;
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        VideoBackdrop.Fill = brush;
    }
}
