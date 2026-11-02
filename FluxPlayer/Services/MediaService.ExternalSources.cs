using FluxPlayer.Models;
using System.IO;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private readonly List<string> _queuedSubtitles = new(), _queuedAudio = new();

    /// <summary>Subtitle and audio files (from the command line) to attach to the next file that starts playing.</summary>
    public void QueueExternalSources(IEnumerable<string> subtitles, IEnumerable<string> audio)
    {
        lock (_playbackStateLock)
        {
            _queuedSubtitles.Clear(); _queuedAudio.Clear();
            _queuedSubtitles.AddRange(subtitles.Where(File.Exists).Select(Path.GetFullPath).Take(ExternalSources.MaximumFiles));
            _queuedAudio.AddRange(audio.Where(File.Exists).Select(Path.GetFullPath).Take(ExternalSources.MaximumFiles));
        }
    }

    /// <summary>Called as a file starts: attaches subtitles/audio found in the configured folders, then any queued from the command line.</summary>
    private void SeedExternalSources(string path)
    {
        var s = PlaybackSettings;
        if (!IsNetworkSource(path) && File.Exists(path))
        {
            if (s.AutoLoadSubtitles && s.ShowSubtitlesByDefault)
            {
                bool managed = s.IgnoreEmbeddedSubtitles;
                var found = ExternalSources.Find(path, ExternalSources.ParseFolders(s.SubtitleFolders), MediaFormats.Subtitles, managed, ExternalSources.PreferredLanguages(s.PreferredSubtitleLanguage));
                foreach (string file in found) if (!_externalSubtitleSources.Contains(file, StringComparer.OrdinalIgnoreCase)) _externalSubtitleSources.Add(file);
            }
            foreach (string file in ExternalSources.Find(path, ExternalSources.ParseFolders(s.AudioFolders), MediaFormats.Audio, false, []))
                if (!_externalAudioSources.Contains(file, StringComparer.OrdinalIgnoreCase)) _externalAudioSources.Add(file);
        }
        foreach (string file in _queuedSubtitles) if (!_externalSubtitleSources.Contains(file, StringComparer.OrdinalIgnoreCase)) _externalSubtitleSources.Add(file);
        foreach (string file in _queuedAudio) if (!_externalAudioSources.Contains(file, StringComparer.OrdinalIgnoreCase)) _externalAudioSources.Add(file);
        _queuedSubtitles.Clear(); _queuedAudio.Clear();
    }

    /// <summary>The file to hand to the player for <paramref name="path"/>: a cleaned copy when subtitle cleaning is on, otherwise the file itself.</summary>
    private string PrepareSubtitle(string path) => SubtitleCleanup.From(PlaybackSettings).Prepare(path, AppProfile.Current.CacheDirectory);
}
