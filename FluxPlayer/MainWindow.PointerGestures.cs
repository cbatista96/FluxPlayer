using FluxPlayer.Models;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FluxPlayer;

/// <summary>Mouse gestures on the video beyond click and wheel: hold to change speed, Ctrl/Alt/Shift + drag, and the sideways wheel.</summary>
public partial class MainWindow
{
    private const int WmMouseHWheel = 0x020E;

    private DispatcherTimer _holdTimer = null!;
    private bool _holdActive;
    private float _holdPreviousRate = 1;
    private string _dragGesture = "None";
    private Point _dragGestureLast;
    private int _dragGestureMargin;
    private int _horizontalWheelAccumulator;

    private void InitializePointerGestures()
    {
        _holdTimer = new DispatcherTimer();
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            if (_videoDragInitiated || _holdActive || !_vm.HasMedia || Mouse.LeftButton != MouseButtonState.Pressed) return;
            _holdActive = true;
            _holdPreviousRate = _vm.PlaybackRate;
            _vm.PlaybackRate = _runtimeSettings.HoldSpeedRate;
            SyncSpeedCombo();
            ShowActionOsd("", $"Speed {_vm.PlaybackRate:0.##}x");
        };
        SourceInitialized += (_, _) =>
        {
            if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source) source.AddHook(PointerGestureHook);
        };
        Deactivated += (_, _) => EndPointerGesture(false);
    }

    /// <summary>Starts a Ctrl/Alt/Shift drag when one is configured; returns whether the press was taken by a gesture.</summary>
    private bool BeginPointerGesture(MouseButtonEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        string action = PointerGestureRules.DragActionFor(_runtimeSettings, (mods & ModifierKeys.Control) != 0, (mods & ModifierKeys.Alt) != 0, (mods & ModifierKeys.Shift) != 0);
        if (action == "None" || !_vm.HasMedia || _controlsLocked) return false;
        _dragGesture = action;
        _dragGestureLast = e.GetPosition(VideoMouseOverlay);
        _dragGestureMargin = _runtimeSettings.SubtitleVerticalMargin;
        _videoDragInitiated = true;
        _singleClickTimer.Stop();
        VideoMouseOverlay.CaptureMouse();
        return true;
    }

    private void ApplyPointerGesture(MouseEventArgs e)
    {
        var point = e.GetPosition(VideoMouseOverlay);
        double dx = point.X - _dragGestureLast.X, dy = point.Y - _dragGestureLast.Y;
        _dragGestureLast = point;
        try
        {
            var view = _vm.Engine.Presentation.View;
            switch (_dragGesture)
            {
                case "Pan":
                    if (view.Zoom <= 1.001) { ShowActionOsd("", "Zoom in first to move the picture"); return; }
                    _vm.Engine.SetViewport(view with
                    {
                        PanX = Math.Clamp(view.PanX + PointerGestureRules.PanDelta(dx, VideoMouseOverlay.ActualWidth, view.Zoom), -1, 1),
                        PanY = Math.Clamp(view.PanY + PointerGestureRules.PanDelta(dy, VideoMouseOverlay.ActualHeight, view.Zoom), -1, 1)
                    });
                    break;
                case "Zoom":
                    var zoomed = view with { Zoom = PointerGestureRules.ZoomAfterDrag(view.Zoom, -dy) };
                    _vm.Engine.SetViewport(zoomed);
                    ShowActionOsd("", $"Zoom {zoomed.Zoom:0.##}x");
                    break;
                case "SubtitlePosition":
                    _dragGestureMargin = PointerGestureRules.SubtitleMarginAfterDrag(_dragGestureMargin, -dy);
                    ShowActionOsd("", $"Subtitle height {_dragGestureMargin}");
                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { OnErrorOccurred(this, ex.Message); EndPointerGesture(false); }
    }

    private void VideoOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasHold = _holdActive, wasGesture = _dragGesture != "None", pending = _holdTimer.IsEnabled;
        EndPointerGesture(true);
        if (wasHold || wasGesture) { e.Handled = true; return; }
        // A short press with hold-to-speed on: it was a click after all.
        if (pending && !_videoDragInitiated) StartSingleClickTimer();
    }

    private void VideoOverlay_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_holdActive || _dragGesture != "None") EndPointerGesture(true);
    }

    private void EndPointerGesture(bool commit)
    {
        _holdTimer.Stop();
        if (_holdActive)
        {
            _holdActive = false;
            if (_vm.HasMedia) { _vm.PlaybackRate = _holdPreviousRate; SyncSpeedCombo(); ShowActionOsd("", $"Speed {_vm.PlaybackRate:0.##}x"); }
        }
        string gesture = _dragGesture;
        _dragGesture = "None";
        if (VideoMouseOverlay.IsMouseCaptured) VideoMouseOverlay.ReleaseMouseCapture();
        if (commit && gesture == "SubtitlePosition" && _dragGestureMargin != _runtimeSettings.SubtitleVerticalMargin)
        {
            int margin = _dragGestureMargin;
            ApplyQuickSetting(settings => settings.SubtitleVerticalMargin = margin);
        }
    }

    private IntPtr PointerGestureHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmMouseHWheel) return IntPtr.Zero;
        string action = _runtimeSettings.HorizontalWheelAction;
        if (action == "None" || _controlsLocked || !_vm.HasMedia || !VideoMouseOverlay.IsMouseOver) return IntPtr.Zero;
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        if (_runtimeSettings.InvertWheel) delta = -delta;
        if (Math.Sign(delta) != Math.Sign(_horizontalWheelAccumulator)) _horizontalWheelAccumulator = 0;
        _horizontalWheelAccumulator += delta;
        int notches = _horizontalWheelAccumulator / Mouse.MouseWheelDeltaForOneLine;
        if (notches != 0)
        {
            _horizontalWheelAccumulator -= notches * Mouse.MouseWheelDeltaForOneLine;
            RunWheelAction(action, notches);
        }
        handled = true;
        return IntPtr.Zero;
    }
}
