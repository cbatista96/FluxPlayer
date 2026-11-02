using FluxPlayer.Models;
using System.IO;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private Dictionary<string, FilePlaybackPreference> _filePreferences = new(StringComparer.OrdinalIgnoreCase);
    private string _fileMemoryToken = "";
    private FilePlaybackPreference? _activeFileStamp, _pendingFilePreference;
    private bool _restoringFilePreference, _rememberedAudioApplied, _rememberedSubtitleApplied, _externalRestoreAttempted;

    private void SyncFilePreferenceReset()
    {
        if (_fileMemoryToken == _settings.FilePlaybackMemoryToken) return;
        _fileMemoryToken = _settings.FilePlaybackMemoryToken;
        _filePreferences.Clear(); _pendingFilePreference = null;
    }

    private void BeginFilePreferences(string path)
    {
        _activeFileStamp = _pendingFilePreference = null;
        _rememberedAudioApplied = _rememberedSubtitleApplied = _externalRestoreAttempted = false;
        if (MediaFormats.IsNetwork(path) || !(_settings.RememberFileVolume || _settings.RememberFileAudioTrack || _settings.RememberFileSubtitles)) return;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            _activeFileStamp = new(info.Length, info.LastWriteTimeUtc.Ticks, DateTime.UtcNow);
            if (!_filePreferences.TryGetValue(path, out var saved) || saved.Size != info.Length || saved.ModifiedTicks != info.LastWriteTimeUtc.Ticks) return;
            _pendingFilePreference = saved;
            if (_settings.RememberFileVolume && saved.Volume.HasValue)
            {
                _restoringFilePreference = true;
                try { Volume = Math.Clamp(saved.Volume.Value, 0, MaxVolume); }
                finally { _restoringFilePreference = false; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void RememberFilePreference(Func<FilePlaybackPreference, FilePlaybackPreference> update)
    {
        if (_restoringFilePreference || CurrentItem == null || _activeFileStamp == null || !HasMedia) return;
        string path = CurrentItem.FilePath;
        var old = _filePreferences.GetValueOrDefault(path);
        if (old == null || old.Size != _activeFileStamp.Size || old.ModifiedTicks != _activeFileStamp.ModifiedTicks) old = _activeFileStamp;
        _filePreferences[path] = update(old) with { UpdatedUtc = DateTime.UtcNow };
        if (_filePreferences.Count > FilePlaybackPreference.MaximumFiles)
            _filePreferences.Remove(_filePreferences.MinBy(pair => pair.Value.UpdatedUtc).Key);
    }

    private void RestoreFileTracks()
    {
        if (_pendingFilePreference is not { } saved || !HasMedia
            || _mediaService.MediaPlayer.State is not (LibVLCSharp.Shared.VLCState.Playing or LibVLCSharp.Shared.VLCState.Paused)) return;
        _restoringFilePreference = true;
        try
        {
            if (_settings.RememberFileAudioTrack && !_rememberedAudioApplied && saved.Audio is { } audio)
            {
                var match = AudioTracks.FirstOrDefault(track => track.Id == audio.Id && track.Name == audio.Name);
                if (match != null) { _rememberedAudioApplied = true; SelectedAudioTrack = match; }
            }
            if (!_settings.RememberFileSubtitles || _rememberedSubtitleApplied) return;
            if (saved.ExternalSubtitle != null && !_externalRestoreAttempted && saved.Subtitle?.Id != -1)
            {
                _externalRestoreAttempted = true;
                if (File.Exists(saved.ExternalSubtitle) && LoadExternalSubtitle(saved.ExternalSubtitle))
                { _rememberedSubtitleApplied = true; return; }
            }
            if (saved.Subtitle is { } subtitle)
            {
                var match = SubtitleTracks.FirstOrDefault(track => track.Id == subtitle.Id && track.Name == subtitle.Name);
                if (match != null) { _rememberedSubtitleApplied = true; SelectedSubtitleTrack = match; }
            }
        }
        finally { _restoringFilePreference = false; }
    }
}
