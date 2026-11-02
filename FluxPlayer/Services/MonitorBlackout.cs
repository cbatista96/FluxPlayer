using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FluxPlayer.Services;

/// <summary>Plain black windows over the monitors the video is not on (fullscreen "cinema" mode). They never take focus and are removed when fullscreen ends.</summary>
public sealed class MonitorBlackout : IDisposable
{
    private const int GwlExStyle = -20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080;
    private const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    private const int WmMouseActivate = 0x0021, MaNoActivate = 3;
    private static readonly IntPtr TopMost = new(-1);
    private readonly List<Window> _windows = new();

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    /// <summary>Covers every screen except <paramref name="keep"/>; bounds are in device pixels.</summary>
    public void Show(IEnumerable<System.Drawing.Rectangle> covered)
    {
        Hide();
        foreach (var bounds in covered)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                Background = Brushes.Black, WindowStartupLocation = WindowStartupLocation.Manual, Left = bounds.Left, Top = bounds.Top, Width = 1, Height = 1, Focusable = false
            };
            var helper = new WindowInteropHelper(window);
            IntPtr handle = helper.EnsureHandle();
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (message != WmMouseActivate) return IntPtr.Zero;
                handled = true; return new IntPtr(MaNoActivate);
            });
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            window.Show();
            SetWindowPos(handle, TopMost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
            _windows.Add(window);
        }
    }

    public void Hide()
    {
        foreach (var window in _windows) { try { window.Close(); } catch (InvalidOperationException) { } }
        _windows.Clear();
    }

    public void Dispose() => Hide();
}
