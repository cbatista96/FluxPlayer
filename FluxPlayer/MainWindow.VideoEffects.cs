using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace FluxPlayer;

/// <summary>360° / 180° viewing and pixel shaders, both applied to the video picture as WPF shader effects.</summary>
public partial class MainWindow
{
    private double _lookYaw, _lookPitch, _lookFov = ViewProjection.DefaultFov;
    private ProjectionEffect? _projectionEffect;
    private bool _videoEffectsHooked;
    private string? _shaderError;

    private bool ProjectionActive => ViewProjection.NormalizeMode(_runtimeSettings.VideoProjection) != "None";

    private void ResetLook()
    {
        _lookYaw = _lookPitch = 0; _lookFov = ViewProjection.DefaultFov;
        UpdateProjectionView();
    }

    private void UpdateProjectionView()
    {
        if (_projectionEffect == null) return;
        double aspect = VideoImage.ActualHeight > 0 ? VideoImage.ActualWidth / VideoImage.ActualHeight : 16d / 9;
        _projectionEffect.View = new Point4D(_lookYaw, _lookPitch, _lookFov, aspect);
    }

    private void ApplyVideoEffects()
    {
        if (!_videoEffectsHooked)
        {
            _videoEffectsHooked = true;
            VideoImage.SizeChanged += (_, _) => { UpdateProjectionView(); UpdateShaderTexel(); };
            _vm.PropertyChanged += (_, e) => { if (e.PropertyName == "CurrentItem") Dispatcher.BeginInvoke(new Action(ResetLook)); };
        }
        string mode = ViewProjection.NormalizeMode(_runtimeSettings.VideoProjection);
        if (mode != "None")
        {
            // The effect needs the whole picture to fill the element, so the usual letterboxing is switched off while it is on.
            VideoImage.Stretch = System.Windows.Media.Stretch.Fill;
            _projectionEffect ??= new ProjectionEffect();
            _projectionEffect.Mode = new Point4D(mode == "Equirect180" ? 1 : 0, 0, 0, 0);
            VideoImage.Effect = _projectionEffect;
            UpdateProjectionView();
            return;
        }
        VideoImage.Stretch = System.Windows.Media.Stretch.Uniform;
        VideoImage.Effect = null;
        string shader = ViewProjection.NormalizeShader(_runtimeSettings.VideoShader);
        if (shader == "None") return;
        try
        {
            VideoImage.Effect = shader == "Custom" ? PixelShaderEffect.FromFile(_runtimeSettings.CustomShaderFile) : PixelShaderEffect.BuiltIn(shader);
            UpdateShaderTexel();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            VideoImage.Effect = null;
            if (e.Message != _shaderError) { _shaderError = e.Message; ShowActionOsd("", Localizer.T("The pixel shader could not be used:") + " " + Localizer.T(e.Message)); }
        }
    }

    private void UpdateShaderTexel()
    {
        if (VideoImage.Effect is PixelShaderEffect effect && VideoImage.Source is System.Windows.Media.Imaging.BitmapSource source && source.PixelWidth > 0 && source.PixelHeight > 0)
            effect.Texel = new Point(1d / source.PixelWidth, 1d / source.PixelHeight);
    }

    /// <summary>Dragging inside a 360° picture turns the view; returns true when the press was taken.</summary>
    private bool BeginLook(MouseButtonEventArgs e)
    {
        if (!ProjectionActive || !_vm.HasMedia || _controlsLocked) return false;
        _dragGesture = "Look";
        _dragGestureLast = e.GetPosition(VideoMouseOverlay);
        _videoDragInitiated = true;
        _singleClickTimer.Stop();
        VideoMouseOverlay.CaptureMouse();
        return true;
    }

    private void LookDrag(Point point)
    {
        (_lookYaw, _lookPitch) = ViewProjection.Drag(_lookYaw, _lookPitch, point.X - _dragGestureLast.X, point.Y - _dragGestureLast.Y, VideoMouseOverlay.ActualWidth, VideoMouseOverlay.ActualHeight, _lookFov);
        UpdateProjectionView();
    }

    private void LookZoom(int notches)
    {
        _lookFov = ViewProjection.Zoom(_lookFov, notches);
        UpdateProjectionView();
        ShowActionOsd("", $"{Localizer.T("Field of view")} {_lookFov * 180 / Math.PI:0}°");
    }
}
