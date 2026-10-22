using System.Windows;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private int _trackRefreshScheduled;
    private int _trackRefreshDirty;
    private volatile bool _shuttingDown;
    public string? LoadedSubtitlePath { get; private set; }

    public bool LoadExternalSubtitle(string path)
    {
        if (!HasMedia || !_mediaService.LoadSubtitle(path)) return false;
        LoadedSubtitlePath = path; return true;
    }

    private void OnTracksChanged(object? sender, EventArgs e)
    {
        if (_shuttingDown) return;
        Interlocked.Exchange(ref _trackRefreshDirty, 1);
        if (Interlocked.Exchange(ref _trackRefreshScheduled, 1) != 0) return;
        _ = RefreshChangedTracksAsync(_mediaChangeToken);
    }

    private async Task RefreshChangedTracksAsync(int sourceToken)
    {
        try
        {
            // A short, coalesced refresh leaves the native ES callback before querying the player.
            await Task.Delay(150).ConfigureAwait(false);
            Interlocked.Exchange(ref _trackRefreshDirty, 0);
            var dispatcher = Application.Current?.Dispatcher;
            if (_shuttingDown || dispatcher == null || dispatcher.HasShutdownStarted) return;
            await _mediaService.EnsureSubtitleCompositionAsync().ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                if (!_shuttingDown && sourceToken == _mediaChangeToken) RefreshTracks();
            });
        }
        catch (InvalidOperationException) { }
        finally
        {
            Interlocked.Exchange(ref _trackRefreshScheduled, 0);
            if (!_shuttingDown && Volatile.Read(ref _trackRefreshDirty) != 0) OnTracksChanged(null, EventArgs.Empty);
        }
    }
}
