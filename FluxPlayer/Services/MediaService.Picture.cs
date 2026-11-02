using FluxPlayer.Models;
using LibVLCSharp.Shared;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private readonly List<string> _externalAudioSources = new();
    private VideoPicture _appliedPicture = new();
    public VideoPicture AppliedPicture => _appliedPicture;
    public VideoCrop? CurrentCrop { get; private set; }
    private int _memoryCropPresenters;
    private (uint Width, uint Height) _cropSourceSize;
    internal event EventHandler? PresentationCropChanged;
    internal (VideoCrop? Crop, uint Width, uint Height) PresentationCrop
    { get { lock (_playbackStateLock) return (CurrentCrop, _cropSourceSize.Width, _cropSourceSize.Height); } }
    internal void RegisterCropPresenter(bool register)
    {
        lock (_playbackStateLock) { _memoryCropPresenters += register ? 1 : -1; if (register && !_disposed && CurrentCrop != null) MediaPlayer.CropGeometry = ""; }
    }
    public (uint Width, uint Height) VideoSize()
    {
        lock (_playbackStateLock)
        {
            if (_disposed || _currentMedia == null) return (0, 0);
            var track = _currentMedia.Tracks.FirstOrDefault(item => item.TrackType == TrackType.Video && item.Data.Video.Width > 0);
            return (track.Data.Video.Width, track.Data.Video.Height);
        }
    }
    public void SetCrop(VideoCrop? crop)
        => ApplyCrop(crop, false);
    private void ApplyCrop(VideoCrop? crop, bool fromViewport)
    {
        lock (_playbackStateLock)
        {
            if (_disposed) return;
            if (!fromViewport) _presentation = _presentation with { View = new() };
            if (crop != null) { var size = VideoSize(); crop.Validate(size.Width, size.Height); _cropSourceSize = size; }
            // VLC 3's memory output does not reliably honor crop offsets. Map a viewport
            // of the same shared memory section instead; no per-frame copy is introduced.
            if (_memoryCropPresenters == 0) MediaPlayer.CropGeometry = crop?.Geometry ?? "";
            CurrentCrop = crop;
            PresentationCropChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public void ApplyPicture(VideoPicture picture)
    {
        if (!picture.IsValid) throw new ArgumentException("Invalid picture settings.");
        lock (_playbackStateLock)
        {
            if (_disposed) return;
            // VLC 3's Enable getter checks sub-source, although adjust is a video-filter.
            // Track our applied state instead; all application picture changes use this path.
            if (picture == _appliedPicture) return;
            if (picture.Neutral != _appliedPicture.Neutral) MediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, picture.Neutral ? 0 : 1);
            _appliedPicture = picture;
            if (picture.Neutral) return;
            MediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, picture.Brightness);
            MediaPlayer.SetAdjustFloat(VideoAdjustOption.Contrast, picture.Contrast);
            MediaPlayer.SetAdjustFloat(VideoAdjustOption.Saturation, picture.Saturation);
            MediaPlayer.SetAdjustFloat(VideoAdjustOption.Hue, picture.Hue > 180 ? picture.Hue - 360 : picture.Hue);
            MediaPlayer.SetAdjustFloat(VideoAdjustOption.Gamma, picture.Gamma);
        }
    }
    public bool LoadExternalAudio(string path)
    {
        lock (_playbackStateLock)
        {
            if (_disposed || MediaPlayer.Media == null || MediaFormats.IsNetwork(path) || !System.IO.File.Exists(path)) return false;
            path = System.IO.Path.GetFullPath(path);
            if (_externalAudioSources.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
            if (_externalAudioSources.Count >= 16) return false;
            if (!MediaPlayer.AddSlave(MediaSlaveType.Audio, new Uri(path).AbsoluteUri, true)) return false;
            _externalAudioSources.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)); _externalAudioSources.Add(path); return true;
        }
    }
}
