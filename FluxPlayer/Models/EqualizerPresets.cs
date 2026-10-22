namespace FluxPlayer.Models;

public sealed record EqPreset(string Name, string Category, float Preamp, float[] Bands);

public static class EqualizerPresets
{
    public const string Custom = "Custom";
    public const string Flat = "Flat";
    // VLC's equalizer multiplies the dry signal by 0.25. Offset that fixed attenuation
    // so a flat EQ at a displayed 0 dB retains the bypass level.
    public static readonly float NativeUnityOffsetDb = 20f * MathF.Log10(4f);
    public static readonly float MaximumEffectivePreampDb = 20f - NativeUnityOffsetDb;
    public static float NativePreamp(float requestedDb) =>
        Math.Clamp(float.IsFinite(requestedDb) ? requestedDb : 0, -20f, MaximumEffectivePreampDb) + NativeUnityOffsetDb;

    public static readonly string[] BandLabels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    public static readonly IReadOnlyList<EqPreset> All = new List<EqPreset>
    {
        new(Flat,                  "General",  0f, new float[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 }),
        new("Loudness", "General", -3f, new float[] {  6,  5,  2,  0, -2, -1,  1,  4,  5,  6 }),
        new("Bass boost",  "Tonal",   -4f, new float[] {  7,  6,  4,  2,  0,  0,  0,  0,  0,  0 }),
        new("Deep bass",    "Tonal",   -6f, new float[] {  9,  8,  6,  3,  1, -1, -2, -2, -2, -2 }),
        new("Bass reduction",      "Tonal",    0f, new float[] { -6, -5, -3, -1,  0,  0,  0,  0,  0,  0 }),
        new("Treble boost",  "Tonal",   -4f, new float[] {  0,  0,  0,  0,  0,  1,  3,  5,  6,  7 }),
        new("Treble reduction",      "Tonal",    0f, new float[] {  0,  0,  0,  0,  0, -1, -3, -5, -6, -7 }),
        new("Warm",              "Tonal",   -2f, new float[] {  3,  3,  2,  2,  1,  0, -1, -2, -3, -3 }),
        new("Bright",           "Tonal",   -2f, new float[] { -2, -1,  0,  0,  0,  1,  2,  3,  4,  4 }),
        new("Cinema",                "Video",   -2f, new float[] {  4,  3,  1,  0, -1,  0,  1,  2,  3,  3 }),
        new("Clear dialogue",     "Video",   -2f, new float[] { -3, -2, -1,  1,  3,  4,  4,  2,  0, -2 }),
        new("Night mode",          "Video",    0f, new float[] { -5, -4, -2,  0,  2,  3,  3,  2,  0, -1 }),
        new("Podcast",             "Video",   -1f, new float[] { -4, -3, -1,  1,  3,  3,  2,  1, -1, -3 }),
        new("Gaming",         "Video",   -3f, new float[] {  5,  4,  1,  0, -1,  1,  3,  4,  3,  2 }),
        new("Music",              "Music",  -2f, new float[] {  3,  2,  0, -2, -1,  0,  1,  2,  3,  3 }),
        new("Rock",                "Music",  -4f, new float[] {  5,  4, -2, -4, -1,  2,  4,  5,  6,  6 }),
        new("Pop",                 "Music",  -3f, new float[] { -1,  2,  4,  5,  3,  0, -1, -1, -1, -1 }),
        new("Jazz",                "Music",  -2f, new float[] {  3,  2,  1,  2, -2, -2,  0,  1,  2,  3 }),
        new("Classical",             "Music",   0f, new float[] {  0,  0,  0,  0,  0,  0, -4, -4, -4, -5 }),
        new("Electronic",         "Music",  -4f, new float[] {  6,  5,  1,  0, -2,  2,  1,  2,  5,  6 }),
        new("Hip-Hop",             "Music",  -4f, new float[] {  6,  5,  2,  3, -1, -1,  1, -1,  2,  3 }),
        new("Dance",               "Music",  -4f, new float[] {  7,  5,  2,  0,  0, -3, -4, -4,  0,  0 }),
        new("Acoustic",            "Music",  -2f, new float[] {  4,  3,  1,  1,  2,  2,  3,  3,  2,  1 }),
        new("Piano",               "Music",  -2f, new float[] {  2,  1,  0,  2,  3,  1,  3,  4,  2,  3 }),
        new("Lounge",              "Music",  -2f, new float[] { -3, -1,  0,  1,  4,  2,  0, -1,  1,  0 }),
        new("Live",             "Music",  -2f, new float[] { -3,  0,  2,  3,  3,  3,  2,  1,  1,  1 }),
        new("Party",              "Music",  -4f, new float[] {  6,  6,  0,  0,  0,  0,  0,  0,  6,  6 }),
        new("Headphones",         "Equipment",  -4f, new float[] {  3,  6,  3, -2, -1,  1,  3,  5,  6,  7 }),
        new("Portable speakers","Equipment",  -2f, new float[] { -6, -4,  2,  4,  3,  1,  1,  2,  3,  2 }),
        new("Soundbar / TV","Equipment",  -2f, new float[] {  2,  2,  1,  0,  1,  2,  2,  1,  1,  0 }),
    };

    public static readonly IReadOnlyList<string> Names =
    All.Select(p => p.Name).Append(Custom).ToList();

    private static readonly Dictionary<string, string> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flat"] = Flat,
        ["Bass Boost"] = "Bass boost",
        ["Cinema"] = "Cinema",
        ["Music"] = "Music",
        ["Voice"] = "Clear dialogue",
        ["Custom"] = Custom,
        // Preserve presets saved by the original localized interface.
        ["Personalizado"] = Custom,
        ["Plano"] = Flat,
        ["Sonoridad (Loudness)"] = "Loudness",
        ["Refuerzo de graves"] = "Bass boost",
        ["Graves profundos"] = "Deep bass",
        ["Reducir graves"] = "Bass cut",
        ["Refuerzo de agudos"] = "Treble boost",
        ["Reducir agudos"] = "Treble cut",
        ["Calido"] = "Warm",
        ["Brillante"] = "Bright",
        ["Cine"] = "Cinema",
        ["Dialogos claros"] = "Clear dialogue",
        ["Modo noche"] = "Night mode",
        ["Videojuegos"] = "Gaming",
        ["Musica"] = "Music",
        ["Clasica"] = "Classical",
        ["Electronica"] = "Electronic",
        ["Acustica"] = "Acoustic",
        ["En vivo"] = "Live",
        ["Fiesta"] = "Party",
        ["Auriculares"] = "Headphones",
        ["Altavoces portatiles"] = "Portable speakers",
        ["Barra de sonido / TV"] = "Soundbar / TV",
    };

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Flat;
        if (LegacyNames.TryGetValue(name, out var mapped)) return mapped;
        return Names.Contains(name) ? name : Flat;
    }

    public static EqPreset? Find(string? name)
    {
        string n = Normalize(name);
        return All.FirstOrDefault(p => p.Name == n);
    }
}
