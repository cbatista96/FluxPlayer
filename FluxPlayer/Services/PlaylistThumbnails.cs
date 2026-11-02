using FluxPlayer.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

/// <summary>Small preview pictures for the playlist's thumbnail view: photos are decoded directly, videos get one frame from FFmpeg. Results are cached on disk.</summary>
public sealed class PlaylistThumbnails
{
    public const int Width = 192;
    private static readonly string[] PhotoExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];
    private readonly SemaphoreSlim _gate = new(2);
    private readonly string _folder;
    private bool _ffmpegMissing;

    public PlaylistThumbnails(string cacheDirectory) => _folder = Path.Combine(cacheDirectory, "playlist-thumbs");

    public static string CacheName(string path, long length, long modifiedTicks) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path.GetFullPath(path).ToLowerInvariant()}|{length}|{modifiedTicks}")))[..24] + ".jpg";

    /// <summary>The second to take the frame from: a tenth of the way in (credits and black starts are skipped), 5 s when the length is unknown, never past 10 minutes.</summary>
    public static double FrameSecond(long durationMs) => durationMs > 0 ? Math.Min(600, durationMs / 10000d) : 5;

    public static string[] FfmpegArguments(string input, string output, double second) =>
        ["-ss", second.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-i", input, "-frames:v", "1", "-vf", $"scale={Width}:-2", "-q:v", "4", "-loglevel", "error", output];

    public static bool IsPhoto(string path) => PhotoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The picture for <paramref name="item"/>, or null for streams, audio, missing files, or when FFmpeg is not installed.</summary>
    public async Task<BitmapSource?> GetAsync(MediaItem item, CancellationToken token)
    {
        string path = item.FilePath;
        if (item.IsNetwork || item.IsAudio || !File.Exists(path)) return null;
        try
        {
            if (IsPhoto(path)) return await Task.Run(() => Load(path, decodeWidth: Width), token).ConfigureAwait(false);
            if (_ffmpegMissing || ConversionService.FindExecutable() == null) { _ffmpegMissing = true; return null; }
            var info = new FileInfo(path);
            string target = Path.Combine(_folder, CacheName(path, info.Length, info.LastWriteTimeUtc.Ticks));
            if (File.Exists(target)) return Load(target);
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_folder);
                string temp = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".tmp.jpg");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    await FfmpegJob.Run(FfmpegArguments(path, temp, FrameSecond(item.DurationMs)), timeout.Token).ConfigureAwait(false);
                    if (!File.Exists(temp)) return null;
                    try { File.Move(temp, target, overwrite: false); } catch (IOException) { }
                    return Load(File.Exists(target) ? target : temp);
                }
                finally { try { File.Delete(temp); } catch (IOException) { } }
            }
            finally { _gate.Release(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or FileNotFoundException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static BitmapSource Load(string path, int decodeWidth = 0)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
