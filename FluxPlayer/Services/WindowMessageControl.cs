using FluxPlayer.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluxPlayer.Services;

/// <summary>Explicitly enabled, fixed transport messages only; no pointers, paths or arbitrary data.</summary>
public sealed class WindowMessageControl : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string value);
    private readonly HwndSource _source; private readonly MainViewModel _vm;
    public uint Message { get; } = RegisterWindowMessage("FluxPlayer.Transport.v1");
    public WindowMessageControl(Window window, MainViewModel vm) { _vm = vm; _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) ?? throw new InvalidOperationException("Window is not initialized."); _source.AddHook(Hook); }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message != Message || lParam != IntPtr.Zero) return IntPtr.Zero;
        switch (wParam.ToInt64()) { case 1: if (_vm.HasMedia || _vm.Playlist.Count > 0) _vm.PlayPauseCommand.Execute(null); break; case 2: _vm.StopCommand.Execute(null); break; case 3: _vm.NextCommand.Execute(null); break; case 4: _vm.PreviousCommand.Execute(null); break; default: return IntPtr.Zero; }
        handled = true; return new IntPtr(1);
    }
    public void Dispose() { if (!_source.IsDisposed) _source.RemoveHook(Hook); }
}
