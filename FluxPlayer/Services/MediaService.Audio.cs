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
        media.AddOption(":sub-language=" + SubtitleLanguages(s));
        if (!s.AutoLoadSubtitles) media.AddOption(":no-sub-autodetect-file");
        if (s.DeinterlaceEnabled) media.AddOption($":deinterlace=1");
    }

    public static string SubtitleLanguages(AppSettings settings)
    {
        if (!settings.ShowSubtitlesByDefault) return "none";
        var languages = (settings.PreferredSubtitleLanguage ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(language => language.Length <= 80 && !language.Any(char.IsControl))
            .Select(language => language.ToLowerInvariant()).Distinct().Take(16).ToList();
        // Prefer the requested languages, then allow a track without language metadata.
        // An explicit 'none' remains a deliberate stop condition in VLC's language list.
        if (!languages.Contains("any") && !languages.Contains("none")) languages.Add("any");
        return string.Join(',', languages);
    }

    public AudioDevice[] GetAudioDevices()
    {
        try
        {
            return new[] { new AudioDevice("Default", "System default") }
                .Concat(MediaPlayer.AudioOutputDeviceEnum.Select(d => new AudioDevice(d.DeviceIdentifier, d.Description)))
                .Where(device => AudioOutputProfile.ValidDeviceId(device.Id))
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
        lock (_playbackStateLock)
        {
            if (_disposed || _currentMedia == null) return null;
            return ReadMetadata(_currentMedia);
        }
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

    public bool IsSourceInUse(string path) => !_disposed
        && string.Equals(path, _currentSourcePath, StringComparison.OrdinalIgnoreCase)
        && MediaPlayer.State is VLCState.Opening or VLCState.Buffering or VLCState.Playing or VLCState.Paused;

    public Task<bool> SaveTagsAsync(string path, string title, string artist, string album, string genre) =>
        SaveMetadataPatchAsync(path, new MetadataPatch(title, artist, album, genre));

    public Task<bool> SaveMetadataPatchAsync(string path, MetadataPatch patch, CancellationToken token = default)
    {
        patch.Validate();
        return Task.Run(async () =>
        {
            if (_disposed || MediaFormats.IsNetwork(path) || !File.Exists(path)) return false;
            path = Path.GetFullPath(path);
            if (IsSourceInUse(path)) return false;
            await _durationParseGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_disposed || IsSourceInUse(path)) return false;
                token.ThrowIfCancellationRequested();
                return WriteMetadataPatch(path, patch, token);
            }
            finally { _durationParseGate.Release(); }
        }, token);
    }

    // TagLib is loaded only for explicit edits. LibVLC's writer cannot reliably clear empty fields.
    private bool WriteMetadataPatch(string path, MetadataPatch patch, CancellationToken token)
    {
        try
        {
            using (var file = TagLib.File.Create(path, TagLib.ReadStyle.None | TagLib.ReadStyle.PictureLazy))
            {
                token.ThrowIfCancellationRequested();
                if (IsSourceInUse(path)) return false;
                if (patch.Title != null) file.Tag.Title = patch.Title;
                if (patch.Artist != null) file.Tag.Performers = patch.Artist.Length == 0 ? Array.Empty<string>() : new[] { patch.Artist };
                if (patch.Album != null) file.Tag.Album = patch.Album;
                if (patch.Genre != null) file.Tag.Genres = patch.Genre.Length == 0 ? Array.Empty<string>() : new[] { patch.Genre };
                // Once started, a file save finishes before cancellation is observed.
                file.Save();
            }
            using var saved = TagLib.File.Create(path, TagLib.ReadStyle.None | TagLib.ReadStyle.PictureLazy);
            return (patch.Title == null || (saved.Tag.Title ?? "") == patch.Title)
                && (patch.Artist == null || string.Join("; ", saved.Tag.Performers) == patch.Artist)
                && (patch.Album == null || (saved.Tag.Album ?? "") == patch.Album)
                && (patch.Genre == null || string.Join("; ", saved.Tag.Genres) == patch.Genre);
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException)
        { return false; }
    }
}
