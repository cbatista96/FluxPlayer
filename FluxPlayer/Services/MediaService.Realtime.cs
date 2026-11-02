namespace FluxPlayer.Services;

public partial class MediaService
{
    public RealtimeAudioSession? Realtime { get; private set; }
    public event Action<RealtimePosition>? RealtimePositionChanged;
    public event Action<long>? RealtimeSeekRequested;
    private Timer? _realtimeClock;
    private int _realtimeClockBusy;
    public void AttachRealtime(RealtimeAudioSession session, bool paused = false)
    {
        Stop(); Realtime = session;
        session.PlaybackEnded += () => { if (ReferenceEquals(Realtime, session)) EndReached?.Invoke(this, EventArgs.Empty); };
        _realtimeClock = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _realtimeClockBusy, 1) != 0) return;
            try { if (ReferenceEquals(Realtime, session)) RealtimePositionChanged?.Invoke(session.Position()); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
            finally { Volatile.Write(ref _realtimeClockBusy, 0); }
        }, null, 0, 100);
        if (!paused) { session.Play(); Playing?.Invoke(this, EventArgs.Empty); }
    }
    public void StopRealtime()
    {
        _realtimeClock?.Dispose(); _realtimeClock = null; var previous = Realtime; Realtime = null;
        if (previous != null) _ = previous.DisposeAsync().AsTask();
    }
}
