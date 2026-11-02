using FluxPlayer.Models;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private VideoPresentation _presentation = new(new(), 0, false, false);
    public VideoPresentation Presentation { get { lock (_playbackStateLock) return _presentation; } }
    public void SetViewport(VideoViewport view)
    {
        lock (_playbackStateLock)
        {
            var size = VideoSize(); var crop = view.Rectangle(size.Width, size.Height);
            _presentation = _presentation with { View = view }; ApplyCrop(view.Zoom == 1 && view.Eye == StereoEye.FullPicture ? null : crop, true);
        }
    }
    public void SetOrientation(int rotation, bool mirror, bool flip)
    {
        if (rotation % 90 != 0) throw new ArgumentException("Choose orientation in 90-degree steps.");
        lock (_playbackStateLock) { if (_disposed) return; _presentation = _presentation with { Rotation = (rotation % 360 + 360) % 360, MirrorHorizontal = mirror, FlipVertical = flip }; PresentationCropChanged?.Invoke(this, EventArgs.Empty); }
    }
}
