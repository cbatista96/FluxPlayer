using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public sealed record MpvAction(string Key, string Name, string Group, string Mode, string Kind, string Default, double Minimum, double Maximum, ImmutableArray<string> Choices, ImmutableArray<string> Command)
{ public override string ToString() => Name; }
public static class MpvActionCatalog
{
    private static MpvAction P(string group, string key, string name, string kind, string value, double min = 0, double max = 0, string choices = "") => new(key, name, group, "property", kind, value, min, max, choices.Split('|', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray(), []);
    private static MpvAction S(string group, string key, string name, string kind, string value, double min = 0, double max = 0, string choices = "") => P(group, key, name, kind, value, min, max, choices) with { Mode = "startup" };
    private static MpvAction C(string group, string key, string name, string kind, string value, double min, double max, params string[] command) => new(key, name, group, "command", kind, value, min, max, [], command.ToImmutableArray());
    public static ImmutableArray<MpvAction> Actions { get; } = [
        C("Sources and queue", "open-file", "Open local media in mpv", "path", "", 0, 0, "loadfile", "$", "replace"),
        C("Sources and queue", "open-url", "Open a media URL in mpv", "url", "https://", 0, 0, "loadfile", "$", "replace"),
        C("Sources and queue", "append-file", "Append a local source", "path", "", 0, 0, "loadfile", "$", "append"),
        C("Sources and queue", "next", "Play next queue entry", "none", "", 0, 0, "playlist-next"),
        C("Sources and queue", "previous", "Play previous queue entry", "none", "", 0, 0, "playlist-prev"),
        P("Transport", "pause", "Pause or resume mpv", "bool", "true"),
        C("Transport", "stop", "Stop the mpv source", "none", "", 0, 0, "stop"),
        C("Transport", "seek-absolute", "Seek to an absolute time", "number", "0", 0, 86400, "seek", "$", "absolute+exact"),
        C("Transport", "seek-relative", "Seek by a signed interval", "number", "10", -86400, 86400, "seek", "$", "relative+exact"),
        C("Transport", "frame-next", "Step forward one decoded frame", "none", "", 0, 0, "frame-step"),
        C("Transport", "frame-back", "Step backward one decoded frame", "none", "", 0, 0, "frame-back-step"),
        C("Transport", "seek-revert", "Return to the prior seek position", "none", "", 0, 0, "revert-seek"),
        C("Transport", "ab-cycle", "Set or cycle A–B loop points", "none", "", 0, 0, "ab-loop"),
        C("Transport", "ab-clear", "Clear both A–B loop points", "none", "", 0, 0, "set_property", "ab-loop-a", "no"),
        P("Transport", "loop-file", "Repeat the current file", "choice", "no", choices: "no|inf"),
        P("Sources and queue", "loop-playlist", "Repeat the mpv queue", "choice", "no", choices: "no|inf"),
        C("Sources and queue", "shuffle", "Shuffle the mpv queue", "none", "", 0, 0, "playlist-shuffle"),
        C("Sources and queue", "queue-move", "Move an mpv queue occurrence", "pair", "[0,1]", 0, 99999, "playlist-move", "$0", "$1"),
        C("Sources and queue", "queue-remove", "Remove an mpv queue occurrence", "integer", "0", 0, 99999, "playlist-remove", "$"),
        C("Sources and queue", "queue-clear", "Clear queued entries after the current source", "none", "", 0, 0, "playlist-clear"),
        P("Sources and queue", "playlist-pos", "Play a selected queue position", "integer", "0", 0, 99999),
        P("Transport", "speed", "Set mpv playback speed", "number", "1", .25, 4),
        P("Transport", "chapter", "Select an mpv chapter", "integer", "0", 0, 9999),
        P("Transport", "edition", "Select an alternate edition", "integer", "0", 0, 999),
        P("Audio", "volume", "Set mpv volume", "number", "70", 0, 100),
        P("Audio", "mute", "Mute the mpv session", "bool", "false"),
        P("Audio", "audio-delay", "Set audio synchronization offset", "number", "0", -10, 10),
        P("Audio", "aid", "Select an mpv audio track ID", "integer", "1", 1, 9999),
        C("Audio", "audio-add", "Attach a local external audio source", "path", "", 0, 0, "audio-add", "$", "select"),
        C("Audio", "audio-remove", "Remove an external audio track", "integer", "1", 1, 9999, "audio-remove", "$"),
        P("Subtitles", "sid", "Select the primary subtitle ID", "integer", "1", 1, 9999),
        P("Subtitles", "secondary-sid", "Select a secondary subtitle ID", "integer", "2", 1, 9999),
        C("Subtitles", "sub-add", "Attach a local subtitle document", "path", "", 0, 0, "sub-add", "$", "select"),
        C("Subtitles", "sub-remove", "Remove an external subtitle track", "integer", "1", 1, 9999, "sub-remove", "$"),
        P("Subtitles", "sub-delay", "Set primary subtitle delay", "number", "0", -60, 60),
        P("Subtitles", "sub-scale", "Scale mpv subtitle text", "number", "1", .25, 4),
        P("Subtitles", "sub-pos", "Position primary subtitles", "number", "100", 0, 100),
        P("Subtitles", "sub-visibility", "Show or hide primary subtitles", "bool", "true"),
        P("Subtitles", "secondary-sub-visibility", "Show or hide secondary subtitles", "bool", "true"),
        P("Subtitle typography", "sub-font", "Choose a subtitle font family", "string", "Segoe UI"),
        P("Subtitle typography", "sub-font-size", "Set subtitle font size", "number", "36", 8, 120),
        P("Subtitle typography", "sub-color", "Set subtitle RGBA color", "color", "#FFFFFFFF"),
        P("Subtitle typography", "sub-outline-size", "Set subtitle outline thickness", "number", "1.65", 0, 10),
        P("Subtitle typography", "sub-shadow-offset", "Set subtitle shadow distance", "number", "1", 0, 10),
        P("Subtitle typography", "sub-bold", "Use bold subtitle text", "bool", "false"),
        P("Subtitle typography", "sub-italic", "Use italic subtitle text", "bool", "false"),
        P("Subtitle typography", "sub-ass-override", "Select ASS style override policy", "choice", "yes", choices: "no|yes|scale|force|strip"),
        S("Subtitle typography", "sub-font-provider", "Select subtitle font discovery provider", "choice", "auto", choices: "auto|none|directwrite|fontconfig"),
        C("Subtitles", "sub-seek", "Seek by subtitle-cue intervals", "integer", "1", -1000, 1000, "sub-seek", "$"),
        P("Subtitles", "secondary-sub-delay", "Set secondary subtitle delay", "number", "0", -60, 60),
        P("Subtitle typography", "sub-spacing", "Set subtitle character spacing", "number", "0", -5, 20),
        P("Subtitle typography", "sub-align-x", "Align subtitle text horizontally", "choice", "center", choices: "left|center|right"),
        P("Subtitle typography", "sub-align-y", "Align subtitle text vertically", "choice", "bottom", choices: "top|center|bottom"),
        P("Video and window", "vid", "Select an mpv video track ID", "integer", "1", 1, 9999),
        P("Video and window", "fullscreen", "Use mpv fullscreen", "bool", "false"),
        P("Video and window", "ontop", "Keep the mpv window above others", "bool", "false"),
        P("Video and window", "window-scale", "Set the mpv window scale", "number", "1", .25, 2),
        P("Video and window", "keepaspect", "Preserve source aspect ratio", "bool", "true"),
        P("Video and window", "video-aspect-override", "Override displayed aspect ratio", "number", "1.7777778", .5, 4),
        P("Video and window", "video-zoom", "Set video zoom", "number", "0", -2, 2),
        P("Video and window", "video-pan-x", "Pan video horizontally", "number", "0", -1, 1),
        P("Video and window", "video-pan-y", "Pan video vertically", "number", "0", -1, 1),
        P("Video and window", "video-rotate", "Rotate decoded video", "choice", "0", choices: "0|90|180|270"),
        P("Picture", "brightness", "Adjust mpv picture brightness", "number", "0", -100, 100),
        P("Picture", "contrast", "Adjust mpv picture contrast", "number", "0", -100, 100),
        P("Picture", "saturation", "Adjust mpv color saturation", "number", "0", -100, 100),
        P("Picture", "gamma", "Adjust mpv gamma", "number", "0", -100, 100),
        P("Picture", "hue", "Adjust mpv hue", "number", "0", -100, 100),
        P("Video decoding", "deinterlace", "Enable mpv deinterlacing", "bool", "false"),
        P("Video decoding", "hwdec", "Select hardware decoder policy", "choice", "no", choices: "no|auto|d3d11va|d3d11va-copy|dxva2|dxva2-copy|nvdec|nvdec-copy"),
        S("Audio output", "ao", "Select mpv audio output backend", "choice", "wasapi", choices: "wasapi|auto"),
        S("Video output", "vo", "Select mpv video renderer", "choice", "gpu-next", choices: "gpu-next|gpu|direct3d|sdl"),
        S("Video output", "gpu-api", "Select graphics API", "choice", "auto", choices: "auto|d3d11|vulkan|opengl"),
        S("Video output", "gpu-context", "Select graphics context", "choice", "auto", choices: "auto|d3d11|win|winvk"),
        P("Debanding", "deband", "Enable renderer debanding", "bool", "false"),
        P("Debanding", "deband-iterations", "Set debanding iteration count", "integer", "1", 1, 4),
        P("Debanding", "deband-threshold", "Set debanding threshold", "number", "48", 0, 256),
        P("Debanding", "deband-range", "Set debanding sampling radius", "number", "16", 1, 64),
        P("Debanding", "deband-grain", "Set debanding grain", "number", "0", 0, 64),
        P("Scaling and dither", "dither-depth", "Select output dither precision", "choice", "auto", choices: "no|auto|8|10|12|16"),
        P("Scaling and dither", "temporal-dither", "Enable temporal dithering", "bool", "false"),
        P("Scaling and dither", "scale", "Select luma upscaling kernel", "choice", "bilinear", choices: "bilinear|bicubic|lanczos|ewa_lanczos|spline36"),
        P("Scaling and dither", "cscale", "Select chroma scaling kernel", "choice", "bilinear", choices: "bilinear|bicubic|lanczos|spline36"),
        P("Scaling and dither", "dscale", "Select downscaling kernel", "choice", "bilinear", choices: "bilinear|bicubic|lanczos|spline36"),
        P("Scaling and dither", "sigmoid-upscaling", "Use sigmoid-domain upscaling", "bool", "false"),
        P("Scaling and dither", "correct-downscaling", "Correct downscaling filter support", "bool", "true"),
        P("Scaling and dither", "linear-downscaling", "Use linear-light downscaling", "bool", "false"),
        P("Video timing", "interpolation", "Enable display-timed frame interpolation", "bool", "false"),
        P("Video timing", "video-sync", "Select video synchronization policy", "choice", "audio", choices: "audio|display-resample|display-vdrop|display-adrop|desync"),
        P("HDR and color", "tone-mapping", "Select renderer tone-mapping algorithm", "choice", "auto", choices: "auto|clip|reinhard|hable|bt.2390|bt.2446a|spline"),
        P("HDR and color", "target-peak", "Set assumed display peak luminance", "integer", "203", 48, 10000),
        P("HDR and color", "target-prim", "Select target color primaries", "choice", "auto", choices: "auto|bt.709|bt.2020|display-p3"),
        P("HDR and color", "target-trc", "Select target transfer curve", "choice", "auto", choices: "auto|bt.1886|srgb|pq|hlg"),
        P("HDR and color", "gamut-mapping-mode", "Select gamut-mapping policy", "choice", "auto", choices: "auto|clip|perceptual|relative|saturation|absolute|desaturate|darken|warn|linear"),
        P("HDR and color", "hdr-compute-peak", "Select dynamic HDR peak detection", "choice", "no", choices: "auto|yes|no"),
        S("Audio output", "audio-exclusive", "Request exclusive audio output", "bool", "false"),
        P("Audio output", "audio-device", "Select an mpv output device ID", "string", "auto"),
        S("Audio output", "audio-channels", "Select decoded output channel layout", "choice", "stereo", choices: "auto|stereo|5.1|7.1"),
        S("Audio output", "audio-format", "Select output sample representation", "choice", "float", choices: "s16|s32|float|double"),
        S("Audio output", "audio-samplerate", "Request output sample rate", "integer", "48000", 8000, 384000)
    ];
    public static object? Value(MpvAction action, string text)
    {
        if (text.Length > 32768 || text.Any(char.IsControl)) throw new ArgumentException("Enter a bounded single-line value.");
        if (action.Kind == "none") return null;
        if (action.Kind == "bool") return bool.TryParse(text, out bool boolean) ? boolean : throw new ArgumentException("Choose true or false.");
        if (action.Kind is "number" or "integer") { if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric) || !double.IsFinite(numeric) || numeric < action.Minimum || numeric > action.Maximum || action.Kind == "integer" && numeric != Math.Truncate(numeric)) throw new ArgumentException("Check the allowed numeric range."); return numeric; }
        if (action.Kind == "pair") { var json = InteropCatalog.ParseInput(text); if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() != 2 || json.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n) || n < action.Minimum || n > action.Maximum)) throw new ArgumentException("Enter exactly two queue indices, for example [0,1]."); return json.EnumerateArray().Select(v => v.GetInt32()).ToArray(); }
        if (action.Kind == "choice" && !action.Choices.Contains(text)) throw new ArgumentException("Choose a supported value.");
        if (action.Kind == "color" && !Regex.IsMatch(text, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new ArgumentException("Use #RRGGBB or #RRGGBBAA.");
        if (action.Kind == "path") return FfmpegJob.Source(text);
        if (action.Kind == "url" && (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "rtsp" or "rtmp") || uri.UserInfo != "")) throw new ArgumentException("Use an HTTP(S), RTSP or RTMP media URL without embedded credentials.");
        if (text.Length is < 1 or > 4096) throw new ArgumentException("Enter a value of 1–4,096 characters."); return text;
    }
}
