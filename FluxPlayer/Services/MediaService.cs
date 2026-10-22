using FluxPlayer.Models;
using LibVLCSharp.Shared;
using System.IO;

namespace FluxPlayer.Services;

public partial class MediaService : IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly string? _pluginDirectory;
    private readonly bool _hardwareDecoding;
    private readonly string? _videoOutputModule;
    private readonly bool _adaptiveCacheBySource;
    private readonly bool _ultraFastStartup;
    private readonly bool _enableDiagnosticLogging;
    private readonly bool _resumePlaybackPerFile;
    private readonly object _diagnosticLock = new();
    private readonly SemaphoreSlim _durationParseGate = new(1, 1);
    private int _recoveringProfile;
    private StreamWriter? _diagnosticWriter;
    private readonly List<PlaybackProfile> _playbackProfiles;
    private int _activePlaybackProfileIndex;
    private string? _currentSourcePath;
    private Media? _currentMedia;
    private Equalizer? _equalizer;
    private volatile bool _disposed;
    public AppSettings PlaybackSettings { get; set; } = new();

    public MediaPlayer MediaPlayer { get; }

    public LibVLC LibVLC => _libVLC;

    public event EventHandler<MediaPlayerTimeChangedEventArgs>? TimeChanged;
    public event EventHandler<MediaPlayerMediaChangedEventArgs>? MediaChanged;
    public event EventHandler? EndReached;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;
    public event EventHandler<MediaPlayerLengthChangedEventArgs>? LengthChanged;
    public event EventHandler? EncounteredError;
    public event EventHandler<MediaPlayerVoutEventArgs>? VoutChanged;

    public MediaService(
        bool hardwareDecoding = true,
        string? videoOutputModule = null,
        bool adaptiveCacheBySource = true,
        bool ultraFastStartup = false,
        bool enableDiagnosticLogging = true,
        bool resumePlaybackPerFile = false)
    {
        _hardwareDecoding = hardwareDecoding;
        _videoOutputModule = string.IsNullOrWhiteSpace(videoOutputModule)
            ? null
            : videoOutputModule.Trim();
        _adaptiveCacheBySource = adaptiveCacheBySource;
        _ultraFastStartup = ultraFastStartup;
        _enableDiagnosticLogging = enableDiagnosticLogging;
        _resumePlaybackPerFile = resumePlaybackPerFile;
        _playbackProfiles = BuildPlaybackProfiles(_hardwareDecoding, _videoOutputModule);
        _activePlaybackProfileIndex = 0;
        var libVlcRuntimeDir = ResolveLibVlcRuntimeDirectory();
        var pluginDir = string.IsNullOrWhiteSpace(libVlcRuntimeDir)
            ? null
            : Path.Combine(libVlcRuntimeDir, "plugins");
        _pluginDirectory = pluginDir;

        if (!string.IsNullOrWhiteSpace(libVlcRuntimeDir))
            Core.Initialize(libVlcRuntimeDir);
        else
            Core.Initialize();

        if (!string.IsNullOrWhiteSpace(pluginDir) && Directory.Exists(pluginDir))
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginDir);

        int localFileCaching = _ultraFastStartup ? 120 : 220;
        int networkCaching = _ultraFastStartup ? 300 : 650;
        int liveCaching = _ultraFastStartup ? 220 : 450;

        var vlcOptions = new List<string>
        {
            $"--file-caching={localFileCaching}",
            $"--network-caching={networkCaching}",
            $"--live-caching={liveCaching}",

            "--no-video-title-show",
            "--no-snapshot-preview",
            "--no-osd",
            "--no-lua",
            "--no-media-library"
        };

        // Generate a missing cache only in a writable plugin directory.
        if (ShouldBuildPluginCache(pluginDir))
            vlcOptions.Add("--reset-plugins-cache");

        _libVLC = new LibVLC(enableDebugLogs: _enableDiagnosticLogging, vlcOptions.ToArray());
        if (_enableDiagnosticLogging)
        {
            ConfigureLogging();
            _libVLC.Log += OnLibVlcLog;
        }

        MediaPlayer = new MediaPlayer(_libVLC);
        MediaPlayer.EnableHardwareDecoding = _hardwareDecoding;
        MediaPlayer.EnableKeyInput = false;
        MediaPlayer.EnableMouseInput = false;

        MediaPlayer.TimeChanged += (s, e) => TimeChanged?.Invoke(s, e);
        MediaPlayer.MediaChanged += (s, e) =>
        {
            WriteDiagnostic("MediaChanged");
            MediaChanged?.Invoke(s, e);
        };
        MediaPlayer.EndReached += (s, e) =>
        {
            WriteDiagnostic("EndReached");
            EndReached?.Invoke(s, EventArgs.Empty);
        };
        MediaPlayer.Playing += (s, e) =>
        {
            WriteDiagnostic($"Playing | Profile={GetActivePlaybackProfileName()}");
            Playing?.Invoke(s, EventArgs.Empty);
        };
        MediaPlayer.Paused += (s, e) =>
        {
            WriteDiagnostic("Paused");
            Paused?.Invoke(s, EventArgs.Empty);
        };
        MediaPlayer.Stopped += (s, e) =>
        {
            WriteDiagnostic("Stopped");
            Stopped?.Invoke(s, EventArgs.Empty);
        };
        MediaPlayer.LengthChanged += (s, e) => LengthChanged?.Invoke(s, e);
        MediaPlayer.EncounteredError += (s, e) =>
        {
            WriteDiagnostic("EncounteredError");
            EncounteredError?.Invoke(s, EventArgs.Empty);
        };
        MediaPlayer.Vout += (s, e) =>
        {
            WriteDiagnostic($"VoutChanged | Count={e.Count}");
            VoutChanged?.Invoke(s, e);
        };

        WriteDiagnostic($"MediaService initialized | PreferredHw={_hardwareDecoding} | PreferredVout={_videoOutputModule ?? "auto"} | AdaptiveCache={_adaptiveCacheBySource} | UltraFast={_ultraFastStartup} | Logs={_enableDiagnosticLogging} | ResumePerFile={_resumePlaybackPerFile}");
    }

    public void PlayFile(string path)
    {
        Interlocked.Exchange(ref _recoveringProfile, 0);
        try
        {
            if (MediaPlayer.Media != null || MediaPlayer.IsPlaying)
                MediaPlayer.Stop();
        }
        catch
        {

        }

        _currentSourcePath = path;
        _activePlaybackProfileIndex = 0;
        WriteDiagnostic($"PlayFile | Source={path}");
        PlayWithActiveProfile(path);
    }

    private void PlayWithActiveProfile(string path)
    {
        var prev = _currentMedia;
        var profile = _playbackProfiles[_activePlaybackProfileIndex];
        WriteDiagnostic($"PlayWithActiveProfile | Name={profile.Name} | Hw={profile.HardwareDecoding} | Vout={profile.VideoOutputModule ?? "auto"}");

        bool isNetworkSource = IsNetworkSource(path);
        var uri = isNetworkSource
            ? new Uri(path)
            : new Uri(path, UriKind.Absolute);

        _currentMedia = new Media(_libVLC, uri);
        ApplyMediaOptions(_currentMedia);
        MediaPlayer.EnableHardwareDecoding = profile.HardwareDecoding;
        _currentMedia.AddOption(profile.HardwareDecoding ? ":avcodec-hw=any" : ":avcodec-hw=none");
        if (_adaptiveCacheBySource)
        {
            if (isNetworkSource)
                _currentMedia.AddOption(_ultraFastStartup ? ":network-caching=300" : ":network-caching=650");
            else
                _currentMedia.AddOption(_ultraFastStartup ? ":file-caching=120" : ":file-caching=220");
        }
        else
        {
            _currentMedia.AddOption(_ultraFastStartup ? ":file-caching=140" : ":file-caching=260");
            _currentMedia.AddOption(_ultraFastStartup ? ":network-caching=320" : ":network-caching=700");
        }
        if (!string.IsNullOrWhiteSpace(profile.VideoOutputModule))
            _currentMedia.AddOption($":vout={profile.VideoOutputModule}");
        if (!_resumePlaybackPerFile)
            _currentMedia.AddOption(":start-time=0");
        var filters = AudioFilterChain;
        if (!string.IsNullOrWhiteSpace(filters))
            _currentMedia.AddOption($":audio-filter={filters}");
        MediaPlayer.Play(_currentMedia);

        prev?.Dispose();
    }

    public bool TryRecoverVideoOutput()
    {
        if (Interlocked.Exchange(ref _recoveringProfile, 1) == 1)
            return false;

        if (string.IsNullOrWhiteSpace(_currentSourcePath))
        {
            Interlocked.Exchange(ref _recoveringProfile, 0);
            return false;
        }

        try
        {
            var nextIndex = _activePlaybackProfileIndex + 1;
            if (nextIndex >= _playbackProfiles.Count)
            {
                WriteDiagnostic("TryRecoverVideoOutput | No more profiles");
                return false;
            }

            bool noVideoOutputYet = MediaPlayer.VoutCount <= 0;
            var resumeTime = (!_resumePlaybackPerFile && noVideoOutputYet)
                ? 0
                : Math.Max(0, MediaPlayer.Time);
            _activePlaybackProfileIndex = nextIndex;
            PlayWithActiveProfile(_currentSourcePath);
            WriteDiagnostic($"TryRecoverVideoOutput | SwitchedProfile={GetActivePlaybackProfileName()} | ResumeTime={resumeTime}");

            if (resumeTime > 0)
                _ = Task.Delay(120).ContinueWith(_ => SeekTo(resumeTime));

            return true;
        }
        finally
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(450);
                Interlocked.Exchange(ref _recoveringProfile, 0);
            });
        }
    }

    public int GetVoutCount() => (int)MediaPlayer.VoutCount;
    public int GetVideoTrackCount() => MediaPlayer.VideoTrackCount;
    public string GetActivePlaybackProfileName() => _playbackProfiles[_activePlaybackProfileIndex].Name;

    public void Play()
    {
        if (MediaPlayer.Media == null)
            return;

        try
        {

            MediaPlayer.SetPause(false);
        }
        catch
        {

        }

        if (!MediaPlayer.IsPlaying)
            MediaPlayer.Play();
    }

    public void Pause()
    {
        if (MediaPlayer.Media == null)
            return;

        if (!MediaPlayer.IsPlaying)
            return;

        try
        {
            MediaPlayer.SetPause(true);
        }
        catch
        {
            MediaPlayer.Pause();
        }
    }

    public void TogglePlayPause()
    {
        if (MediaPlayer.IsPlaying)
            Pause();
        else
            Play();
    }

    public void Stop()
    {

        if (MediaPlayer.IsPlaying)
            MediaPlayer.Stop();
    }

    public bool IsPlaying => MediaPlayer.IsPlaying;

    public long GetTime() => MediaPlayer.Time;
    public long GetDuration() => MediaPlayer.Length;

    public void SeekTo(long milliseconds)
    {
        var length = MediaPlayer.Length;
        var clamped = Math.Max(0, milliseconds);
        if (length > 0)
            clamped = Math.Min(clamped, length);

        if (MediaPlayer.IsSeekable || length > 0)
            MediaPlayer.Time = clamped;
    }

    public void SeekRelative(long offsetMs)
    {
        long newTime = Math.Max(0, Math.Min(MediaPlayer.Length, MediaPlayer.Time + offsetMs));
        SeekTo(newTime);
    }

    public void SeekToPercent(float percent)
    {
        var clamped = Math.Clamp(percent, 0f, 1f);
        long length = MediaPlayer.Length;
        long targetTime = length > 0 ? (long)(length * clamped) : -1;
        bool seekApplied = false;

        if (targetTime >= 0)
        {
            try
            {
                MediaPlayer.Time = targetTime;
                seekApplied = true;
            }
            catch
            {

            }
        }

        if (!seekApplied && MediaPlayer.IsSeekable)
        {
            try
            {
                MediaPlayer.Position = clamped;
                seekApplied = true;
            }
            catch
            {

            }
        }

        if (!seekApplied)
        {
            _ = Task.Delay(120).ContinueWith(_ =>
            {
                try
                {
                    long retryLength = MediaPlayer.Length;
                    if (retryLength > 0)
                        MediaPlayer.Time = (long)(retryLength * clamped);
                    else if (MediaPlayer.IsSeekable)
                        MediaPlayer.Position = clamped;
                }
                catch { }
            });
        }
    }

    public void SetVolume(int volume) => MediaPlayer.Volume = Math.Clamp(volume, 0, 200);

    public string? AudioFilterChain { get; set; }
    public void SetMute(bool mute) => MediaPlayer.Mute = mute;
    public int GetVolume() => MediaPlayer.Volume;

    public void SetRate(float rate) => MediaPlayer.SetRate(rate);

    public void NextFrame() => MediaPlayer.NextFrame();

    public long GetAudioDelay() => MediaPlayer.AudioDelay;
    public void SetAudioDelay(long microseconds) => MediaPlayer.SetAudioDelay(microseconds);
    public void SetSubtitleDelay(long microseconds) => MediaPlayer.SetSpuDelay(microseconds);

    public TrackInfo[] GetAudioTracks()
        => (MediaPlayer.AudioTrackDescription ?? Array.Empty<LibVLCSharp.Shared.Structures.TrackDescription>())
            .Select(t => new TrackInfo(t.Id, t.Name))
            .ToArray();

    public void SetAudioTrack(int id) => MediaPlayer.SetAudioTrack(id);
    public int GetAudioTrack() => MediaPlayer.AudioTrack;

    public TrackInfo[] GetVideoTracks()
        => (MediaPlayer.VideoTrackDescription ?? Array.Empty<LibVLCSharp.Shared.Structures.TrackDescription>())
            .Select(t => new TrackInfo(t.Id, t.Name))
            .ToArray();

    public void SetVideoTrack(int id) => MediaPlayer.SetVideoTrack(id);
    public int GetVideoTrack() => MediaPlayer.VideoTrack;

    public TrackInfo[] GetSubtitleTracks()
        => (MediaPlayer.SpuDescription ?? Array.Empty<LibVLCSharp.Shared.Structures.TrackDescription>())
            .Select(t => new TrackInfo(t.Id, t.Name))
            .ToArray();

    public void SetSubtitleTrack(int id) => MediaPlayer.SetSpu(id);

    public void LoadSubtitle(string path)
    {
        MediaPlayer.AddSlave(MediaSlaveType.Subtitle, path, true);
    }

    public void ApplyEqualizer(float[] bands, float preamp = 0f)
    {
        _equalizer?.Dispose();
        _equalizer = new Equalizer();
        _equalizer.SetPreamp(preamp);
        for (uint i = 0; i < Math.Min((uint)bands.Length, 10u); i++)
            _equalizer.SetAmp(bands[i], i);
        MediaPlayer.SetEqualizer(_equalizer);
    }

    public void DisableEqualizer()
    {
        _equalizer?.Dispose();
        _equalizer = null;
        MediaPlayer.UnsetEqualizer();
    }

    public bool TakeSnapshot(string outputPath)
    {
        return MediaPlayer.TakeSnapshot(0, outputPath, 0, 0);
    }

    public string GetMediaInfo()
    {
        var lines = new List<string>();

        try
        {
            if (MediaPlayer.Media != null)
            {
                var media = MediaPlayer.Media;

                foreach (var track in media.Tracks)
                {
                    if (track.TrackType == TrackType.Video)
                    {
                        lines.Add($"Video: {track.Data.Video.Width}x{track.Data.Video.Height}");
                        if (track.Data.Video.FrameRateNum > 0 && track.Data.Video.FrameRateDen > 0)
                        {
                            var fps = (double)track.Data.Video.FrameRateNum / track.Data.Video.FrameRateDen;
                            lines.Add($"FPS: {fps:0.##}");
                        }
                        if (track.Codec != 0)
                            lines.Add($"Video codec: {FourCCToString(track.Codec)}");
                    }
                    else if (track.TrackType == TrackType.Audio)
                    {
                        lines.Add($"Audio: {track.Data.Audio.Channels}ch {track.Data.Audio.Rate}Hz");
                        if (track.Codec != 0)
                            lines.Add($"Audio codec: {FourCCToString(track.Codec)}");
                    }
                }
            }
        }
        catch
        {

        }

        return string.Join("\n", lines);
    }

    private static string FourCCToString(uint fourcc)
    {
        var bytes = BitConverter.GetBytes(fourcc);
        var chars = bytes.Select(b => b >= 32 && b < 127 ? (char)b : '?').ToArray();
        return new string(chars).TrimEnd('\0', '?');
    }

    public void SetAspectRatio(string? ratio)
    {
        MediaPlayer.AspectRatio = ratio;
    }

    public async Task<long> GetMediaDurationAsync(string path)
    {
        if (MediaFormats.IsNetwork(path))
            return -1;

        if (!File.Exists(path)) return -1;

        if (_disposed) return -1;
        await _durationParseGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return -1;

            using var media = new Media(_libVLC, new Uri(path, UriKind.Absolute));
            var result = await media.Parse(MediaParseOptions.ParseLocal, timeout: 5000).ConfigureAwait(false);
            await Task.Yield();
            return result == MediaParsedStatus.Done ? media.Duration : -1;
        }
        catch
        {
            return -1;
        }
        finally
        {
            _durationParseGate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        // No native media parse may outlive the LibVLC instance it belongs to.
        _durationParseGate.Wait();
        try { MediaPlayer.Stop(); } catch { }
        _currentMedia?.Dispose();
        _equalizer?.Dispose();
        MediaPlayer.Dispose();
        if (_enableDiagnosticLogging)
            _libVLC.Log -= OnLibVlcLog;
        lock (_diagnosticLock)
        {
            try
            {
                _diagnosticWriter?.Flush();
                _diagnosticWriter?.Dispose();
            }
            catch { }
            _diagnosticWriter = null;
        }
        _libVLC.Dispose();
        _durationParseGate.Release();

    }

    private static bool IsNetworkSource(string path)
    {
        return MediaFormats.IsNetwork(path);
    }

    private static string? ResolveLibVlcRuntimeDirectory()
    {
        var archDir = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "libvlc", archDir),
            Path.Combine(Directory.GetCurrentDirectory(), "libvlc", archDir)
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static bool ShouldBuildPluginCache(string? pluginDir)
    {
        if (string.IsNullOrWhiteSpace(pluginDir) || !Directory.Exists(pluginDir))
            return false;
        if (File.Exists(Path.Combine(pluginDir, "plugins.dat")))
            return false;

        try
        {
            var probe = Path.Combine(pluginDir, ".flux-write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool BuildPluginCacheAndExit()
    {
        try
        {
            var runtimeDir = ResolveLibVlcRuntimeDirectory();
            if (string.IsNullOrWhiteSpace(runtimeDir)) return false;
            var pluginDir = Path.Combine(runtimeDir, "plugins");
            Core.Initialize(runtimeDir);
            using var libVlc = new LibVLC(enableDebugLogs: false,
                "--reset-plugins-cache", "--no-lua", "--no-media-library");
            return File.Exists(Path.Combine(pluginDir, "plugins.dat"));
        }
        catch
        {
            return false;
        }
    }

    private static List<PlaybackProfile> BuildPlaybackProfiles(bool preferredHardwareDecoding, string? preferredVideoOutput)
    {
        var profiles = new List<PlaybackProfile>();

        void AddProfile(string name, bool hw, string? vout)
        {
            var normalizedVout = string.IsNullOrWhiteSpace(vout) ? null : vout.Trim();
            if (profiles.Any(p =>
                p.HardwareDecoding == hw &&
                string.Equals(p.VideoOutputModule, normalizedVout, StringComparison.OrdinalIgnoreCase)))
                return;

            profiles.Add(new PlaybackProfile(name, hw, normalizedVout));
        }

        AddProfile("Preferred", preferredHardwareDecoding, preferredVideoOutput);
        AddProfile("Auto SW", false, null);
        AddProfile("Direct3D11 SW", false, "direct3d11");
        AddProfile("Direct3D9 SW", false, "direct3d9");
        AddProfile("OpenGL Win32 SW", false, "glwin32");
        AddProfile("GDI SW", false, "wingdi");
        AddProfile("Auto HW", true, null);

        return profiles;
    }

    private void ConfigureLogging()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FluxPlayer",
                "logs");
            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, $"engine-{DateTime.Now:yyyyMMdd}.log");
            var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _diagnosticWriter = new StreamWriter(stream) { AutoFlush = true };
            WriteDiagnostic("===== Session Start =====");
        }
        catch
        {

        }
    }

    private void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        try
        {
            if (e.Level >= LogLevel.Warning)
                WriteDiagnostic($"[libvlc {e.Level}] {e.Module}: {e.Message}");
        }
        catch
        {

        }
    }

    private void WriteDiagnostic(string message)
    {
        if (!_enableDiagnosticLogging)
            return;

        lock (_diagnosticLock)
        {
            if (_diagnosticWriter == null)
                return;

            _diagnosticWriter.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}");
        }
    }

    private sealed record PlaybackProfile(string Name, bool HardwareDecoding, string? VideoOutputModule);
}
