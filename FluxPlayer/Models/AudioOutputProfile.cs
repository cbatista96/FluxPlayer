namespace FluxPlayer.Models;

/// <summary>A saved adjustment for one output endpoint, independent of other endpoints.</summary>
public sealed record AudioOutputProfile
{
    public string DeviceName { get; init; } = "System default";
    public double Volume { get; init; } = 80;
    public bool EqualizerEnabled { get; init; }
    public float[] Bands { get; init; } = new float[10];
    public float Preamp { get; init; }
    public bool AntiClip { get; init; } = true;
    public string Preset { get; init; } = EqualizerPresets.Flat;
    public int AudioDelayMs { get; init; }

    public const int MaximumProfiles = 64;
    public static bool ValidDeviceId(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.Length <= 4096 && !id.Any(char.IsControl);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceName) || DeviceName.Length > 512 || DeviceName.Any(char.IsControl))
            throw new ArgumentException("The output device name must contain 1–512 printable characters.");
        if (!double.IsFinite(Volume) || Volume is < 0 or > 200 || !float.IsFinite(Preamp) || Preamp is < -20 or > 20
            || Bands == null || !EqualizerLayout.IsSupported(Bands.Length) || Bands.Any(value => !float.IsFinite(value) || value is < -20 or > 20)
            || AudioDelayMs is < -86_400_000 or > 86_400_000)
            throw new ArgumentException("Use 10, 20 or 31 finite bands and requested preamp within ±20 dB, volume 0–200%, and audio delay within 24 hours.");
    }

    public static AudioOutputProfile Capture(AppSettings settings, string deviceName) => new()
    {
        DeviceName = deviceName,
        Volume = settings.Volume,
        EqualizerEnabled = settings.EqualizerEnabled,
        Bands = (float[])settings.EqualizerBands.Clone(),
        Preamp = settings.EqualizerPreamp,
        AntiClip = settings.EqualizerAntiClip,
        Preset = settings.EqualizerPreset,
        AudioDelayMs = settings.AudioDelay
    };

    public void Apply(AppSettings settings)
    {
        Validate();
        settings.Volume = Volume; settings.EqualizerEnabled = EqualizerEnabled;
        settings.EqualizerBands = (float[])Bands.Clone(); settings.EqualizerPreamp = Preamp;
        settings.EqualizerAntiClip = AntiClip; settings.EqualizerPreset = EqualizerPresets.Normalize(Preset);
        settings.AudioDelay = AudioDelayMs;
    }
}
