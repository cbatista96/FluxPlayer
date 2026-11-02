using FluxPlayer.Services;

namespace FluxPlayer.Models;

public sealed record PcmEffectSetting
{
    public string Kind { get; init; } = "Gain";
    public float Value { get; init; } = 1;
    public float[][] Matrix { get; init; } = [];
    public float[] Gains { get; init; } = [];
    public int[] DelaysMs { get; init; } = [];
    public string AssemblyPath { get; init; } = "";
    public string TypeName { get; init; } = "";
    public static Action<PcmAudioChain> Configure(PcmEffectSetting[] settings)
    {
        if (settings.Length > 32 || settings.Any(s => s == null || s.Matrix == null || s.Matrix.Length > 8 || s.Matrix.Any(r => r == null || r.Length > 8) || s.Gains == null || s.Gains.Length > 8 || s.DelaysMs == null || s.DelaysMs.Length > 8)) throw new ArgumentException("Use up to 32 effects with non-null matrices, gain and delay arrays of at most eight channels.");
        var detached = settings.Select(s => s with { Matrix = s.Matrix.Select(r => r.ToArray()).ToArray(), Gains = s.Gains.ToArray(), DelaysMs = s.DelaysMs.ToArray() }).ToArray();
        return chain =>
        {
            foreach (var effect in detached)
                switch (effect.Kind)
                {
                    case "Gain": chain.Add(new PcmGain(effect.Value)); break;
                    case "Limiter": chain.Add(new PcmLimiter(effect.Value)); break;
                    case "Matrix": chain.Add(new PcmChannelMatrix(effect.Matrix, effect.Gains, effect.DelaysMs)); break;
                    case "Plugin": chain.LoadTrustedPlugin(effect.AssemblyPath, effect.TypeName); break;
                    default: throw new ArgumentException("Unknown PCM effect kind: " + effect.Kind);
                }
        };
    }
}
