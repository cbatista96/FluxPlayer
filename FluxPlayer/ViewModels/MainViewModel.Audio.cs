using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAudioMode), nameof(ModeLabel))]
    private PlayerMode _activeMode = PlayerMode.Video;
    [ObservableProperty] private PlayerModePreference _modePreference;
    [ObservableProperty] private string _nowPlayingTitle = "Open your music";
    [ObservableProperty] private string _nowPlayingArtist = "Local files and internet radio";
    [ObservableProperty] private string _nowPlayingAlbum = "";
    [ObservableProperty] private string _audioTechnical = "";
    [ObservableProperty] private ImageSource? _albumArtwork;
    [ObservableProperty] private string _lyricLine = "";
    [ObservableProperty] private bool _isCurrentFavorite;
    public bool IsAudioMode => ActiveMode == PlayerMode.Audio;
    public string ModeLabel => IsAudioMode ? "Audio" : "Video";
    public ObservableCollection<ChapterInfo> Chapters { get; } = new();
    public ObservableCollection<AudioDevice> OutputDevices { get; } = new();
    private LyricsDocument? _lyrics;
    private long _lastMetadataRefresh;
    private int _artworkGeneration;
    private string _artworkKey = "";
    private string? _recordedSource;
    private LibraryService? _library;
    public LibraryService Library => _library ??= new LibraryService();
    public MediaService Engine => _mediaService;

    partial void OnModePreferenceChanged(PlayerModePreference value)
    {
        _settings.PlayerModePreference = value;
        ResolvePlayerMode(null);
    }

    partial void OnHasMediaChanged(bool value)
    {
        if (value) return;
        LoadedSubtitlePath = null;
        NowPlayingTitle = "Open your music"; NowPlayingArtist = "Local files and internet radio";
        NowPlayingAlbum = AudioTechnical = LyricLine = ""; AlbumArtwork = null; _lyrics = null;
        Interlocked.Increment(ref _artworkGeneration);
    }

    public void SetModePreference(PlayerModePreference preference)
    {
        ModePreference = preference;
        if (preference == PlayerModePreference.Automatic) RefreshNowPlaying();
    }

    private void ResolvePlayerMode(MediaMetadata? metadata)
    {
        ActiveMode = ModePreference switch
        {
            PlayerModePreference.Audio => PlayerMode.Audio,
            PlayerModePreference.Video => PlayerMode.Video,
            _ when metadata is { HasVideo: true } => PlayerMode.Video,
            _ when metadata is { HasAudio: true } => PlayerMode.Audio,
            _ => CurrentItem?.IsAudio == true ? PlayerMode.Audio : PlayerMode.Video
        };
    }

    private void ResetNowPlaying(MediaItem item)
    {
        LoadedSubtitlePath = null;
        NowPlayingTitle = item.Title;
        NowPlayingArtist = item.Artist;
        NowPlayingAlbum = item.Album;
        AudioTechnical = MediaFormats.Extension(item.FilePath).TrimStart('.').ToUpperInvariant();
        AlbumArtwork = null;
        _artworkKey = "";
        Interlocked.Increment(ref _artworkGeneration);
        _lastMetadataRefresh = 0;
        _recordedSource = null;
        LyricLine = "";
        _lyrics = null;
        Chapters.Clear();
        ResolvePlayerMode(null);
        _ = LoadLocalLyricsAsync(item.FilePath);
        _ = RefreshFavoriteAsync(item.FilePath);
    }

    public void RefreshNowPlaying()
    {
        if (_shuttingDown) return;
        var item = CurrentItem;
        if (item == null) return;
        try
        {
            var metadata = _mediaService.ReadCurrentMetadata();
            if (metadata == null) return;
            ResolvePlayerMode(metadata);
            if (!string.IsNullOrWhiteSpace(metadata.Title)) item.Title =
                metadata.Title == Path.GetFileName(item.FilePath) ? Path.GetFileNameWithoutExtension(item.FilePath) : metadata.Title;
            item.Artist = metadata.Artist; item.Album = metadata.Album; item.Genre = metadata.Genre;
            NowPlayingTitle = item.Title;
            NowPlayingArtist = string.IsNullOrWhiteSpace(item.Artist) ? (item.IsNetwork ? "Live stream" : "Unknown artist") : item.Artist;
            NowPlayingAlbum = item.Album;
            AudioTechnical = metadata.Technical;
            WindowTitle = $"FluxPlayer  —  {item.Title}";
            if (Chapters.Count == 0)
                foreach (var chapter in _mediaService.GetChapters()) Chapters.Add(chapter);
            string artworkKey = item.FilePath + "|" + metadata.Artwork;
            if (!string.Equals(_artworkKey, artworkKey, StringComparison.Ordinal))
            {
                _artworkKey = artworkKey;
                _ = LoadArtworkAsync(metadata.Artwork, item.FilePath);
            }
            if (_recordedSource != item.FilePath && (metadata.HasAudio || metadata.HasVideo))
            {
                _recordedSource = item.FilePath;
                _ = Library.RecordPlaybackAsync(item.FilePath, metadata);
            }
        }
        catch (ObjectDisposedException) { }
    }

    private void ApplyCurrentPlaybackOptions()
    {
        if (_shuttingDown) return;
        // Native state resets on every file even if the bound properties did not change.
        _settings.PlaybackRate = PlaybackRate;
        _settings.AudioDelay = (int)AudioDelayMs;
        _settings.SubtitleDelay = (int)SubtitleDelayMs;
        _mediaService.PlaybackSettings = _settings;
        _mediaService.ApplyPlaybackSettings();
        PushEqualizer();
    }

    private async Task LoadArtworkAsync(string artwork, string source)
    {
        int generation = Interlocked.Increment(ref _artworkGeneration);
        var bitmap = await Task.Run(() =>
        {
            try
            {
                string? path = null;
                if (Uri.TryCreate(artwork, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
                if (path == null && !MediaFormats.IsNetwork(source))
                {
                    var directory = Path.GetDirectoryName(source)!;
                    path = new[] { "cover.jpg", "folder.jpg", "front.jpg", "cover.png", "folder.png" }
                        .Select(n => Path.Combine(directory, n)).FirstOrDefault(File.Exists);
                }
                if (path == null || !File.Exists(path) || new FileInfo(path).Length > 20_000_000) return null;
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 256; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
                return image;
            }
            catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException) { return null; }
        });
        if (generation == _artworkGeneration) AlbumArtwork = bitmap;
    }

    private async Task LoadLocalLyricsAsync(string source)
    {
        if (MediaFormats.IsNetwork(source)) return;
        var lyrics = await Task.Run(() => LyricsDocument.LoadSidecar(source));
        if (CurrentItem?.FilePath != source) return;
        _lyrics = lyrics;
        LyricLine = lyrics?.LineAt(0) ?? "";
    }

    public void UpdateAudioPosition(long ms)
    {
        if (_lyrics != null) LyricLine = _lyrics.LineAt(ms);

        if (CurrentItem?.IsNetwork == true && Environment.TickCount64 - _lastMetadataRefresh > 5000)
        { _lastMetadataRefresh = Environment.TickCount64; RefreshNowPlaying(); }
    }

    private async Task RefreshFavoriteAsync(string path)
    {
        var favorite = await Library.IsFavoriteAsync(path);
        if (CurrentItem?.FilePath == path) IsCurrentFavorite = favorite;
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (CurrentItem == null) return;
        string path = CurrentItem.FilePath;
        bool value = await Library.ToggleFavoriteAsync(path);
        if (CurrentItem?.FilePath == path) IsCurrentFavorite = value;
    }

    public void RefreshOutputDevices()
    {
        OutputDevices.Clear();
        foreach (var device in _mediaService.GetAudioDevices()) OutputDevices.Add(device);
    }

    public void OpenAt(string path, long positionMs)
    {
        int index = Playlist.ToList().FindIndex(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) { AddFilesToPlaylist(new[] { path }); index = Playlist.Count - 1; }
        if (index < 0) return;
        PlayAt(index);
        _pendingResumeMs = Math.Max(0, positionMs);
        if (TotalDurationMs > positionMs && _mediaService.GetDuration() > positionMs)
        { _pendingResumeMs = -1; SeekToMs(positionMs); }
    }

    private string[] ExpandPlaylistInputs(string[] paths)
    {
        var expanded = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                if (MediaFormats.Playlists.Contains(Path.GetExtension(path)) && !MediaFormats.IsNetwork(path))
                {
                    if (Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
                        expanded.AddRange(CueSheet.Read(path).Select(t => t.Source).Distinct(StringComparer.OrdinalIgnoreCase));
                    else expanded.AddRange(PlaylistService.Read(path));
                }
                else if (MediaFormats.IsNetwork(path)) expanded.Add(path);
                else if (File.Exists(path)) expanded.Add(Path.GetFullPath(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            { ErrorOccurred?.Invoke(this, $"Unable to open {Path.GetFileName(path)}: {e.Message}"); }
        }
        return expanded.Take(PlaylistService.MaxEntries).ToArray();
    }

    public void RestorePlaylist()
    {
        if (!_settings.RememberPlaylist || Playlist.Count > 0) return;

        var paths = _settings.SavedPlaylist.Where(p => File.Exists(p) || MediaFormats.IsNetwork(p)).ToArray();
        if (paths.Length > 0)
        {
            string? selected = _settings.SavedPlaylist.ElementAtOrDefault(_settings.SavedPlaylistIndex);
            AddFilesToPlaylist(paths);
            int index = Array.FindIndex(paths, path => string.Equals(path, selected, StringComparison.OrdinalIgnoreCase));
            CurrentIndex = Math.Max(0, index);
        }
    }

    public async Task ShutdownAsync()
    {
        _shuttingDown = true;
        _mediaService.TracksChanged -= OnTracksChanged;
        _mediaService.VideoSurfaceChanged -= OnTracksChanged;
        Interlocked.Increment(ref _durationParseToken);
        Interlocked.Increment(ref _mediaChangeToken);
        Interlocked.Increment(ref _artworkGeneration);
        if (_library != null) await _library.DisposeAsync();
    }
}
