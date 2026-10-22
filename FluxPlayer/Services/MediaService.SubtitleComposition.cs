using LibVLCSharp.Shared;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private readonly object _playbackStateLock = new();
    private readonly SemaphoreSlim _subtitleCompositionGate = new(1, 1);
    private readonly CancellationTokenSource _subtitleCompositionLifetime = new();
    private CancellationTokenSource? _subtitleTransition;
    private readonly List<string> _externalSubtitleSources = new();
    private long _sourceGeneration;
    private volatile bool _opaqueVideoSurface;
    public bool UsesSubtitleSoftwareFallback { get; private set; }
    public bool HasOpaqueVideoSurface => _opaqueVideoSurface;
    public event EventHandler? VideoSurfaceChanged;

    internal void ReportVideoSurfaceChroma(uint chroma)
    {
        if (_disposed) return;
        // VLC 3's Windows opaque decoder surfaces: DX11, DX10, DXA9 and DXA0.
        _opaqueVideoSurface = chroma is 0x31315844 or 0x30315844 or 0x39415844 or 0x30415844;
        VideoSurfaceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>VLC 3 cannot blend YUVA subtitles into opaque D3D11 frames before memory output conversion.</summary>
    public async Task<bool> EnsureSubtitleCompositionAsync()
    {
        if (_disposed) return false;
        CancellationToken token;
        try
        {
            token = _subtitleCompositionLifetime.Token;
            if (!await _subtitleCompositionGate.WaitAsync(0, token).ConfigureAwait(false)) return false;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { return false; }
        bool ownsRecovery = false;
        long generation = -1;
        CancellationTokenSource? transition = null;
        EventHandler<EventArgs>? started = null;
        try
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long position; int selected; bool paused;
            lock (_playbackStateLock)
            {
                if (_disposed || string.IsNullOrEmpty(_currentSourcePath)
                    || !_opaqueVideoSurface || !_playbackProfiles[_activePlaybackProfileIndex].HardwareDecoding
                    || MediaPlayer.Spu < 0 || MediaPlayer.VideoTrackCount <= 0) return false;
                if (Interlocked.CompareExchange(ref _recoveringProfile, 1, 0) != 0) return false;
                ownsRecovery = true;
                generation = _sourceGeneration;
                int software = _playbackProfiles.FindIndex(profile => !profile.HardwareDecoding);
                if (software < 0) return false;
                position = Math.Max(0, MediaPlayer.Time); selected = MediaPlayer.Spu;
                transition = CancellationTokenSource.CreateLinkedTokenSource(token); _subtitleTransition = transition;
                paused = MediaPlayer.State == VLCState.Paused;
                started = (_, _) => ready.TrySetResult(); MediaPlayer.Playing += started;
                UsesSubtitleSoftwareFallback = true; _activePlaybackProfileIndex = software;
                WriteDiagnostic("Subtitle composition requires software decoding with the VLC 3 memory video surface.");
                PlayWithActiveProfile(_currentSourcePath);
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), transition.Token).ConfigureAwait(false);
            lock (_playbackStateLock)
            {
                if (_disposed || generation != _sourceGeneration) return false;
                ApplyPlaybackSettings();
                if (position > 0) SeekTo(position);
                var tracks = GetSubtitleTracks();
                if (tracks.Any(track => track.Id == selected)) SetSubtitleTrack(selected);
                if (paused) MediaPlayer.SetPause(true);
            }
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or VLCException or InvalidOperationException)
        { return false; }
        finally
        {
            if (started != null) MediaPlayer.Playing -= started;
            lock (_playbackStateLock)
                if (ReferenceEquals(_subtitleTransition, transition)) _subtitleTransition = null;
            transition?.Dispose();
            if (ownsRecovery && generation == Interlocked.Read(ref _sourceGeneration)) Interlocked.Exchange(ref _recoveringProfile, 0);
            _subtitleCompositionGate.Release();
        }
    }
}
