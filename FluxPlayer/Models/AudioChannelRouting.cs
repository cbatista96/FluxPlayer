namespace FluxPlayer.Models;

/// <summary>Routing values from the pinned VLC 3.0.21 vlc_aout.h, not speaker layouts.</summary>
public static class AudioChannelRouting
{
    public static string Normalize(string? value) => value switch
    {
        "Original" or "Mono" or "Stereo" or "ReverseStereo" or "Left" or "Right" => value,
        // These earlier choices were stored without configuring the output.
        "2.1" or "5.1" or "7.1" => "Original",
        _ => "Stereo"
    };

    public static int NativeValue(string? value) => Normalize(value) switch
    { "Original" => 0, "ReverseStereo" => 2, "Left" => 3, "Right" => 4, "Mono" => 7, _ => 1 };

    public static string NativeLabel(int value) => value switch
    { 0 => "Original / output default", 1 => "Stereo", 2 => "Reverse stereo", 3 => "Left only", 4 => "Right only", 7 => "Mono", _ => "Output default" };
}
