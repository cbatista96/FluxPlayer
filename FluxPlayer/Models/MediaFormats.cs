using System.IO;

namespace FluxPlayer.Models;

public static class MediaFormats
{
    public static IReadOnlySet<string> Audio { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp1", ".mp2", ".mp3", ".aac", ".adts", ".m4a", ".m4b", ".m4p", ".alac",
        ".flac", ".wav", ".wave", ".rf64", ".w64", ".aif", ".aiff", ".aifc", ".au", ".snd",
        ".ogg", ".oga", ".opus", ".spx", ".wma", ".ape", ".wv", ".mpc", ".mpp", ".mp+",
        ".tta", ".tak", ".dsf", ".dff", ".dsd", ".ac3", ".eac3", ".ec3", ".dts", ".dtshd",
        ".amr", ".awb", ".caf", ".ra", ".ram", ".mka", ".a52", ".mlp", ".thd", ".truehd",
        ".mid", ".midi", ".rmi", ".kar", ".mod", ".xm", ".it", ".s3m", ".mtm", ".669",
        ".stm", ".ult", ".med", ".okt", ".voc", ".vgm", ".vgz", ".sap", ".sid"
    };

    public static IReadOnlySet<string> Video { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".qt", ".wmv", ".asf", ".flv", ".f4v", ".webm",
        ".m4v", ".mpg", ".mpeg", ".mpe", ".m1v", ".m2v", ".mpv", ".ts", ".mts", ".m2ts",
        ".m2t", ".m2p", ".m2v", ".vob", ".vro", ".ogv", ".ogm", ".3gp", ".3g2", ".divx",
        ".xvid", ".rm", ".rmvb", ".dv", ".mxf", ".gxf", ".nut", ".nsv", ".nuv", ".y4m",
        ".h264", ".264", ".h265", ".265", ".hevc", ".av1", ".ivf", ".bik", ".bk2", ".smk",
        ".wtv", ".dvr-ms", ".tod", ".rec", ".vdr", ".amv", ".mjpg", ".mjpeg"
    };

    public static IReadOnlySet<string> Playlists { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".m3u", ".m3u8", ".pls", ".xspf", ".cue" };
    public static IReadOnlySet<string> Subtitles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".smi", ".sami", ".ttml", ".dfxp" };

    private static readonly HashSet<string> NetworkSchemes = new(StringComparer.OrdinalIgnoreCase)
        { "http", "https", "rtsp", "rtsps", "rtmp", "rtmps", "rtp", "udp", "mms", "mmsh", "mmst", "ftp", "ftps", "sftp", "smb", "srt" };

    public static bool IsNetwork(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && NetworkSchemes.Contains(uri.Scheme);
    public static bool IsAudio(string source) => Audio.Contains(Extension(source));
    public static bool IsMedia(string source) => Audio.Contains(Extension(source)) || Video.Contains(Extension(source));
    public static string Extension(string source) => Path.GetExtension(
        IsNetwork(source) && Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.AbsolutePath : source);
    public static string FileDialogFilter { get; } =
        $"Recognized media|{Patterns(Audio.Concat(Video))}|Audio|{Patterns(Audio)}|Video|{Patterns(Video)}|Playlists and CUE|{Patterns(Playlists)}|All files (detect content)|*.*";
    private static string Patterns(IEnumerable<string> extensions) => string.Join(';', extensions.OrderBy(s => s).Select(s => "*" + s));

    public static IEnumerable<string> EnumerateFolder(string directory, bool recursive = true)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };
        return Directory.EnumerateFiles(directory, "*", options).Where(IsMedia);
    }
}

public enum PlayerMode { Audio, Video }
public enum PlayerModePreference { Automatic, Audio, Video }
