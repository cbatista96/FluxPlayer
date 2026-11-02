namespace FluxPlayer.Services;

public partial class MediaService
{
    public LibVLCSharp.Shared.MediaStats? ReadStatistics()
    { lock (_playbackStateLock) { if (_disposed || _currentMedia == null) return null; return _currentMedia.Statistics; } }
}
