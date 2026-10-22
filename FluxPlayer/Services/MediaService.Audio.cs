using FluxPlayer.Models;
using LibVLCSharp.Shared;
using System.Globalization;
using System.IO;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private void ApplyMediaOptions(Media media)
    {
        var s = PlaybackSettings;
        media.AddOption($":audio-replay-gain-mode={s.ReplayGainMode}");
        media.AddOption($":audio-replay-gain-preamp={s.ReplayGainPreamp.ToString(CultureInfo.InvariantCulture)}");
        media.AddOption(s.ReplayGainPeakProtection ? ":audio-replay-gain-peak-protection" : ":no-audio-replay-gain-peak-protection");
        if (!string.IsNullOrWhiteSpace(s.PreferredAudioLanguage)) media.AddOption($":audio-language={s.PreferredAudioLanguage}");
        if (!string.IsNullOrWhiteSpace(s.PreferredSubtitleLanguage)) media.AddOption($":sub-language={s.PreferredSubtitleLanguage}");
        if (!s.AutoLoadSubtitles) media.AddOption(":no-sub-autodetect-file");
        if (!s.ShowSubtitlesByDefault) media.AddOption(":sub-track=-1");
        if (s.DeinterlaceEnabled) media.AddOption($":deinterlace=1");
    }

    public AudioDevice[] GetAudioDevices()
    {
        try
        {
            return new[] { new AudioDevice("Default", "System default") }
                .Concat(MediaPlayer.AudioOutputDeviceEnum.Select(d => new AudioDevice(d.DeviceIdentifier, d.Description)))
                .DistinctBy(d => d.Id).ToArray();
        }
        catch { return new[] { new AudioDevice("Default", "System default") }; }
    }

    public void SelectAudioDevice(string id) => MediaPlayer.SetOutputDevice(id == "Default" ? null! : id);

    public void ApplyPlaybackSettings()
    {
        var s = PlaybackSettings;
        MediaPlayer.SetRate(s.PlaybackRate);
        MediaPlayer.SetAudioDelay(s.AudioDelay * 1000L);
        MediaPlayer.SetSpuDelay(s.SubtitleDelay * 1000L);
        if (s.AudioOutputDevice != "Default") SelectAudioDevice(s.AudioOutputDevice);
        MediaPlayer.SetDeinterlace(s.DeinterlaceEnabled ? s.DeinterlaceMode.ToLowerInvariant() switch
        {
            "auto" => "yadif",
            "bob" => "bob",
            "blend" => "blend",
            "yadif2x" => "yadif2x",
            _ => "yadif"
        } : null!);
    }

    public MediaMetadata? ReadCurrentMetadata()
    {
        if (_currentMedia == null) return null;
        return ReadMetadata(_currentMedia);
    }

    public async Task<MediaMetadata?> ReadMetadataAsync(string path, CancellationToken token = default)
    {
        if (_disposed || !File.Exists(path)) return null;
        await _durationParseGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed || token.IsCancellationRequested) return null;
            using var media = new Media(_libVLC, new Uri(path));
            var status = await media.Parse(MediaParseOptions.ParseLocal, timeout: 3000).ConfigureAwait(false);
            // LibVLCSharp 3 can complete Parse inline on the native event thread.
            // Never read/dispose a media object from inside that callback.
            await Task.Yield();
            if (status != MediaParsedStatus.Done) return null;
            return ReadMetadata(media);
        }
        finally { _durationParseGate.Release(); }
    }

    private static MediaMetadata ReadMetadata(Media media)
    {
        var tracks = media.Tracks;
        bool video = tracks.Any(t => t.TrackType == TrackType.Video &&
            !(IsCoverCodec(t.Codec) && t.Data.Video.FrameRateNum <= t.Data.Video.FrameRateDen));
        var audio = tracks.Where(t => t.TrackType == TrackType.Audio).ToArray();
        string technical = string.Join(" · ", audio.Select(t =>
            $"{FourCCToString(t.Codec).ToUpperInvariant()} · {t.Data.Audio.Rate / 1000.0:0.###} kHz · {t.Data.Audio.Channels} ch"));
        return new(media.Meta(MetadataType.Title) ?? "", media.Meta(MetadataType.Artist) ?? "",
            media.Meta(MetadataType.Album) ?? "", media.Meta(MetadataType.Genre) ?? "",
            media.Meta(MetadataType.ArtworkURL) ?? "", media.Duration, audio.Length > 0, video, technical);
    }

    private static bool IsCoverCodec(uint codec) => FourCCToString(codec).ToLowerInvariant().Trim() is "jpeg" or "png" or "mjpg";

    public ChapterInfo[] GetChapters()
    {
        try
        {
            return MediaPlayer.FullChapterDescriptions(Math.Max(MediaPlayer.Title, 0))
                .Select((c, i) => new ChapterInfo(i, string.IsNullOrWhiteSpace(c.Name) ? $"Chapter {i + 1}" : c.Name, c.TimeOffset)).ToArray();
        }
        catch { return Array.Empty<ChapterInfo>(); }
    }

    public async Task<bool> SaveTagsAsync(string path, string title, string artist, string album, string genre)
    {
        if (_disposed || !File.Exists(path) || path == _currentSourcePath && MediaPlayer.IsPlaying) return false;
        await _durationParseGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return false;
            using var media = new Media(_libVLC, new Uri(path));
            await media.Parse(MediaParseOptions.ParseLocal, timeout: 3000).ConfigureAwait(false);
            await Task.Yield();
            media.SetMeta(MetadataType.Title, title); media.SetMeta(MetadataType.Artist, artist);
            media.SetMeta(MetadataType.Album, album); media.SetMeta(MetadataType.Genre, genre);
            return media.SaveMeta();
        }
        finally { _durationParseGate.Release(); }
    }
}
