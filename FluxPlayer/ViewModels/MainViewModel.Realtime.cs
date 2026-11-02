using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public event Action? AuxiliaryAudioStopRequested;
    private CancellationTokenSource? _realtimeStart;
    private MediaItem[] _realtimeItems = [];
    private RealtimeAudioOptions? _realtimeOptions;
    private Action<PcmAudioChain>? _realtimeEffects;
    private int _realtimeIndex = -1;
    private bool _openingRealtime;
    public bool IsRealtimeAudio => Engine.Realtime != null;
    private void InitializeRealtimeAudio()
    {
        Engine.RealtimePositionChanged += position => PostOnUi(() =>
        {
            if (!IsRealtimeAudio || position.TrackIndex >= _realtimeItems.Length) return;
            if (_realtimeIndex != position.TrackIndex)
            {
                FinishHistory("Completed"); foreach (var item in Playlist) item.IsPlaying = false;
                _realtimeIndex = position.TrackIndex; CurrentItem = _realtimeItems[_realtimeIndex]; CurrentIndex = Playlist.IndexOf(CurrentItem); CurrentItem.IsPlaying = true;
                PrepareHistory(CurrentItem); HistoryPlaying(); NowPlayingTitle = CurrentItem.Title; WindowTitle = "FluxPlayer — " + CurrentItem.Title;
            }
            TotalDurationMs = position.DurationMs; TotalTime = FormatTime(TimeSpan.FromMilliseconds(position.DurationMs));
            SyncTimeDisplay(position.PositionMs); IsPlaying = position.Playing; IsBuffering = false;
        });
        Engine.RealtimeSeekRequested += milliseconds => PostOnUi(() =>
        {
            if (_realtimeOptions == null || CurrentIndex < 0) return;
            bool paused = !Engine.IsPlaying;
            _ = RestartRealtime(_realtimeOptions with { StartSeconds = Math.Clamp(milliseconds / 1000d, 0, Math.Max(0, TotalDurationMs / 1000d - .001)) }, paused);
        });
    }
    public async Task StartRealtimeAudio(RealtimeAudioOptions options, Action<PcmAudioChain>? effects = null, int startIndex = -1, bool paused = false)
    {
        options.Validate(); int first = startIndex < 0 ? Math.Max(0, CurrentIndex) : startIndex;
        var items = options.DirectPcm ? Playlist.Skip(first).Take(1).ToArray() : Playlist.Skip(first).ToArray();
        if (items.Length is < 1 or > 32 || items.Any(i => i.IsNetwork || !MediaFormats.IsAudio(i.FilePath))) throw new ArgumentException("Queue 1–32 local audio sources, then start a real-time session.");
        Stop(); var cancellation = new CancellationTokenSource(); _realtimeStart = cancellation;
        _realtimeOptions = options; _realtimeEffects = effects; IsBuffering = true;
        var session = new RealtimeAudioSession();
        try
        {
            await session.Prepare(items.Select(i => i.FilePath).ToArray(), options, effects, cancellation.Token);
            if (_shuttingDown || cancellation.IsCancellationRequested || items.Any(i => !Playlist.Contains(i))) throw new OperationCanceledException();
            session.OpenOutput(); session.Volume = (float)(Volume / 100); session.Muted = IsMuted;
            _realtimeItems = items; _realtimeIndex = -1;
            foreach (var item in Playlist) item.IsPlaying = false;
            CurrentItem = items[0]; CurrentIndex = Playlist.IndexOf(CurrentItem); CurrentItem.IsPlaying = true; HasMedia = true;
            SetVirtualEdit(null); CancelLinkedEdition(); ClearBilingual();
            ResetNowPlaying(CurrentItem); ResolvePlayerMode(null);
            NowPlayingTitle = CurrentItem.Title; NowPlayingArtist = CurrentItem.Artist; WindowTitle = "FluxPlayer — " + CurrentItem.Title;
            TotalDurationMs = (long)(session.Tracks[0].Seconds * 1000); TotalTime = FormatTime(TimeSpan.FromMilliseconds(TotalDurationMs));
            _openingRealtime = true; Engine.AttachRealtime(session, paused); _openingRealtime = false; IsBuffering = false; IsPlaying = !paused;
        }
        catch
        {
            _openingRealtime = false; await session.DisposeAsync(); if (ReferenceEquals(_realtimeStart, cancellation)) IsBuffering = false; throw;
        }
        finally { if (ReferenceEquals(_realtimeStart, cancellation)) _realtimeStart = null; cancellation.Dispose(); }
    }
    private void CancelRealtimeStart()
    { _realtimeStart?.Cancel(); _realtimeStart = null; if (!_openingRealtime) Engine.StopRealtime(); }
    private async Task RestartRealtime(RealtimeAudioOptions options, bool paused)
    { try { await StartRealtimeAudio(options, _realtimeEffects, CurrentIndex, paused); } catch (OperationCanceledException) { } catch (Exception ex) when (ex is not OutOfMemoryException) { ErrorOccurred?.Invoke(this, ex.Message); } }
    private void StopDirectPcmForControl()
    {
        if (Engine.Realtime?.Options.DirectPcm != true) return;
        Stop(); ErrorOccurred?.Invoke(this, "Direct PCM stopped before applying software volume, mute or speed. Direct output bypasses processing; use the device's hardware level control.");
    }
    private bool HandleRealtimeEnd()
    {
        if (Engine.Realtime is not { } session) return false;
        string? error = session.Error;
        long underruns = session.Position().UnderrunFrames;
        FinishHistory(error == null ? "Completed" : "Error"); Stop();
        if (error != null) ErrorOccurred?.Invoke(this, error);
        else if (underruns > 0) ErrorOccurred?.Invoke(this, $"The real-time session had {underruns} underrun frames; continuity is not verified. Increase its prebuffer or reduce processing load.");
        return true;
    }
}
