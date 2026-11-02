using FluxPlayer.Models;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace FluxPlayer.Services;

/// <summary>Turns an equirectangular 360° / 180° picture into the rectilinear view you are looking at (see Shaders/projection.hlsl).</summary>
public sealed class ProjectionEffect : ShaderEffect
{
    private static readonly PixelShader Shader = new() { UriSource = new Uri("pack://application:,,,/Shaders/projection.ps") };

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(ProjectionEffect), 0);
    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(nameof(View), typeof(Point4D), typeof(ProjectionEffect), new UIPropertyMetadata(new Point4D(0, 0, ViewProjection.DefaultFov, 1.7777), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(Point4D), typeof(ProjectionEffect), new UIPropertyMetadata(new Point4D(0, 0, 0, 0), PixelShaderConstantCallback(1)));

    public ProjectionEffect() { PixelShader = Shader; UpdateShaderValue(InputProperty); UpdateShaderValue(ViewProperty); UpdateShaderValue(ModeProperty); }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>X = yaw, Y = pitch, Z = vertical field of view (radians), W = aspect ratio of the view.</summary>
    public Point4D View { get => (Point4D)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    /// <summary>X = 1 for a 180° picture.</summary>
    public Point4D Mode { get => (Point4D)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
}

/// <summary>One of the built-in pixel shaders or a compiled one chosen by the user; the shader sees the picture as s0, the texel size in c0 and a strength in c1.</summary>
public sealed class PixelShaderEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(PixelShaderEffect), 0);
    public static readonly DependencyProperty TexelProperty = DependencyProperty.Register(nameof(Texel), typeof(Point), typeof(PixelShaderEffect), new UIPropertyMetadata(new Point(1d / 1920, 1d / 1080), PixelShaderConstantCallback(0)));

    private PixelShaderEffect(PixelShader shader) { PixelShader = shader; UpdateShaderValue(InputProperty); UpdateShaderValue(TexelProperty); }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Point Texel { get => (Point)GetValue(TexelProperty); set => SetValue(TexelProperty, value); }

    /// <summary>A built-in shader by name ("Grayscale", "Sepia", "Sharpen", "Invert"); null for anything else.</summary>
    public static PixelShaderEffect? BuiltIn(string name) =>
        name is "Grayscale" or "Sepia" or "Sharpen" or "Invert"
            ? new(new PixelShader { UriSource = new Uri($"pack://application:,,,/Shaders/{name.ToLowerInvariant()}.ps") })
            : null;

    /// <summary>A compiled shader file (.ps, ps_2_0 – ps_3_0, up to 64 KB). The file is read once and checked before WPF sees it.</summary>
    public static PixelShaderEffect FromFile(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > ViewProjection.MaxShaderBytes) throw new InvalidDataException("Choose a compiled pixel shader (.ps) of at most 64 KB.");
        byte[] bytes = File.ReadAllBytes(path);
        if (!ViewProjection.IsPixelShaderBytecode(bytes)) throw new InvalidDataException("That file is not a compiled pixel shader (ps_2_0 – ps_3_0).");
        var shader = new PixelShader();
        shader.SetStreamSource(new MemoryStream(bytes, writable: false));
        return new(shader);
    }
}
