using FluxPlayer.Services;
using System.Windows;
using System.Windows.Input;

namespace FluxPlayer;

public partial class MainWindow
{
    private readonly TouchGestures _touch = new();
    private readonly HashSet<TouchDevice> _touchDevices = new();
    private string? _touchSource, _tapSource;
    private long _lastTap;
    private long _touchStarted;
    private Point _lastTapPosition;
    private void InitializeTouchGestures()
    {
        VideoMouseOverlay.PreviewTouchDown += (_, e) =>
        {
            if (!_vm.CurrentSettings.Enhancements.TouchGestures) return;
            e.Handled = true;
            if (_controlsLocked || !_vm.HasMedia) { CancelTouch(); return; }
            if (!_touch.Active) { _touchSource = _vm.CurrentItem?.FilePath; _touchStarted = Environment.TickCount64; }
            var p = e.GetTouchPoint(VideoMouseOverlay).Position;
            _touch.Begin(e.TouchDevice.Id, p.X, p.Y, VideoMouseOverlay.ActualWidth, VideoMouseOverlay.ActualHeight);
            if (!e.TouchDevice.Capture(VideoMouseOverlay)) { CancelTouch(); return; }
            _touchDevices.Add(e.TouchDevice);
        };
        VideoMouseOverlay.PreviewTouchMove += (_, e) =>
        {
            if (!_touchDevices.Contains(e.TouchDevice)) return;
            e.Handled = true; var p = e.GetTouchPoint(VideoMouseOverlay).Position;
            _touch.Move(e.TouchDevice.Id, p.X, p.Y);
        };
        VideoMouseOverlay.PreviewTouchUp += (_, e) =>
        {
            if (!_touchDevices.Remove(e.TouchDevice)) return;
            e.Handled = true; var p = e.GetTouchPoint(VideoMouseOverlay).Position;
            var decision = _touch.End(e.TouchDevice.Id, p.X, p.Y);
            e.TouchDevice.Capture(null);
            if (_controlsLocked || !_vm.CurrentSettings.Enhancements.TouchGestures || _touchSource != _vm.CurrentItem?.FilePath) { CancelTouch(); return; }
            if (decision == null) return;
            try
            {
                if (decision.Tap)
                {
                    long now = Environment.TickCount64;
                    if (now - _touchStarted > 350) { _lastTap = 0; return; }
                    if (_lastTap != 0 && now - _lastTap <= 400 && _tapSource == _touchSource && (p - _lastTapPosition).Length <= 40)
                    { _lastTap = 0; _vm.PlayPauseCommand.Execute(null); }
                    else { _lastTap = now; _lastTapPosition = p; _tapSource = _touchSource; }
                    return;
                }
                _lastTap = 0;
                if (decision.SeekSeconds != 0) SeekWithOsd((int)Math.Round(decision.SeekSeconds));
                if (decision.VolumeDelta != 0) { _vm.ChangeVolumeBy((int)Math.Round(decision.VolumeDelta)); ShowVolumeOsd(); }
                if (decision.ZoomFactor != 1 || decision.PanX != 0 || decision.PanY != 0)
                {
                    var view = _vm.Engine.Presentation.View;
                    _vm.Engine.SetViewport(view with { Zoom = Math.Clamp(view.Zoom * decision.ZoomFactor, 1, 8), PanX = Math.Clamp(view.PanX + decision.PanX, -1, 1), PanY = Math.Clamp(view.PanY + decision.PanY, -1, 1) });
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { OnErrorOccurred(this, ex.Message); }
        };
        VideoMouseOverlay.LostTouchCapture += (_, e) => { if (_touchDevices.Contains(e.TouchDevice)) CancelTouch(); };
        Deactivated += (_, _) => CancelTouch();
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == "CurrentItem") CancelTouch(); };
    }
    private void CancelTouch()
    {
        _touch.Cancel(); _lastTap = 0;
        var devices = _touchDevices.ToArray(); _touchDevices.Clear();
        foreach (var device in devices) if (device.Captured == VideoMouseOverlay) device.Capture(null);
    }
}
