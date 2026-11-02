namespace FluxPlayer.Models;

/// <summary>Looking around inside a 360° / 180° (equirectangular) picture, and the choice of pixel shader; the arithmetic is kept apart from WPF so it can be tested.</summary>
public static class ViewProjection
{
    public static readonly string[] Modes = ["None", "Equirect360", "Equirect180"];
    public static readonly string[] Shaders = ["None", "Grayscale", "Sepia", "Sharpen", "Invert", "Custom"];
    public const double MinFov = 30 * Math.PI / 180, MaxFov = 120 * Math.PI / 180, DefaultFov = 90 * Math.PI / 180, MaxPitch = 89 * Math.PI / 180;
    public const int MaxShaderBytes = 64 * 1024;

    public static string NormalizeMode(string? mode) => Modes.FirstOrDefault(item => string.Equals(item, mode, StringComparison.OrdinalIgnoreCase)) ?? "None";
    public static string NormalizeShader(string? shader) => Shaders.FirstOrDefault(item => string.Equals(item, shader, StringComparison.OrdinalIgnoreCase)) ?? "None";

    /// <summary>Yaw wraps around the full circle; pitch stops just short of straight up/down so the view never flips over.</summary>
    public static (double Yaw, double Pitch) Clamp(double yaw, double pitch)
    {
        if (!double.IsFinite(yaw)) yaw = 0;
        if (!double.IsFinite(pitch)) pitch = 0;
        yaw %= 2 * Math.PI;
        if (yaw > Math.PI) yaw -= 2 * Math.PI; else if (yaw < -Math.PI) yaw += 2 * Math.PI;
        return (yaw, Math.Clamp(pitch, -MaxPitch, MaxPitch));
    }

    /// <summary>Dragging the picture right turns the view left, as if grabbing the scene. A drag across the whole view turns it by its field of view.</summary>
    public static (double Yaw, double Pitch) Drag(double yaw, double pitch, double dxPixels, double dyPixels, double viewWidth, double viewHeight, double fov)
    {
        if (!(viewWidth > 0) || !(viewHeight > 0)) return Clamp(yaw, pitch);
        double horizontalFov = 2 * Math.Atan(Math.Tan(fov / 2) * viewWidth / viewHeight);
        return Clamp(yaw - dxPixels / viewWidth * horizontalFov, pitch + dyPixels / viewHeight * fov);
    }

    /// <summary>Wheel notches (positive = forward) narrow the field of view, i.e. zoom in.</summary>
    public static double Zoom(double fov, int notches) => Math.Clamp((double.IsFinite(fov) ? fov : DefaultFov) * Math.Pow(0.9, notches), MinFov, MaxFov);

    /// <summary>A compiled pixel shader starts with a version token 0xFFFF.... (ps_2_0 … ps_3_0) and must be small.</summary>
    public static bool IsPixelShaderBytecode(ReadOnlySpan<byte> bytes) =>
        bytes.Length is >= 16 and <= MaxShaderBytes && bytes[2] == 0xFF && bytes[3] == 0xFF && bytes[1] is >= 2 and <= 3 && bytes[0] <= 1;
}
