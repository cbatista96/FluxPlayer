using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxPlayer.Models;
using FluxPlayer.Services;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MediaService _mediaService;
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private int _mediaChangeToken;
    private int _durationParseToken;

    [ObservableProperty] private string _windowTitle = "FluxPlayer";
    [ObservableProperty] private bool _isPlaying = false;
    [ObservableProperty] private bool _isMuted = false;
    [ObservableProperty] private double _volume = 80;
    [ObservableProperty] private double _seekPosition = 0;
    [ObservableProperty] private string _currentTime = "0:00";
    [ObservableProperty] private string _totalTime = "0:00";
    [ObservableProperty] private long _totalDurationMs = 0;
    [ObservableProperty] private bool _hasMedia = false;
    [ObservableProperty] private bool _isBuffering = false;

    [ObservableProperty] private float _playbackRate = 1.0f;
    [ObservableProperty] private RepeatMode _repeatMode = RepeatMode.None;
    [ObservableProperty] private bool _shuffle = false;

    [ObservableProperty] private ObservableCollection<MediaItem> _playlist = new();
    [ObservableProperty] private MediaItem? _currentItem;
    [ObservableProperty] private int _currentIndex = -1;
    [ObservableProperty] private bool _isPlaylistVisible = true;

    [ObservableProperty] private ObservableCollection<TrackInfo> _subtitleTracks = new();
    [ObservableProperty] private ObservableCollection<TrackInfo> _audioTracks = new();
    [ObservableProperty] private TrackInfo? _selectedSubtitleTrack;
    [ObservableProperty] private TrackInfo? _selectedAudioTrack;

    [ObservableProperty] private bool _isEqualizerEnabled = false;
    [ObservableProperty] private bool _isEqualizerPanelOpen = false;
    [ObservableProperty] private float[] _equalizerBands = new float[10];
    [ObservableProperty] private string _equalizerPreset = EqualizerPresets.Flat;
    [ObservableProperty] private float _equalizerPreamp;
    [ObservableProperty] private bool _equalizerAntiClip = true;

    [ObservableProperty] private AppTheme _currentTheme = AppTheme.DarkMica;

    [ObservableProperty] private string _aspectRatio = "Auto";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AbLoopStage))] private long _loopA = -1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AbLoopStage))] private long _loopB = -1;

    public int AbLoopStage => LoopA < 0 ? 0 : LoopB < 0 ? 1 : 2;
    [ObservableProperty] private string _abLoopLabel = "A-B";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAvOffset))] private long _audioDelayMs = 0;

    public bool HasAvOffset => AudioDelayMs != 0 || SubtitleDelayMs != 0;

    [ObservableProperty] private ObservableCollection<string> _recentFilesList = new();

    public List<float> SpeedOptions { get; } = new() { 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 3.0f, 4.0f };

    public List<(AppTheme Value, string Label)> ThemeOptions { get; } = new()
    {
        (AppTheme.DarkMica,    "Dark Mica"),
        (AppTheme.LightMica,   "Light Mica"),
        (AppTheme.FullBlack,   "Full Black"),
        (AppTheme.AcrylicBlur, "Acrylic Blur"),
    };

    public IReadOnlyList<string> EqPresets => EqualizerPresets.Names;

    public List<string> AspectOptions { get; } = new()
    {
        "Auto", "16:9", "4:3", "21:9", "1:1", "Stretch"
    };

    public string VolumeIcon => IsMuted || Volume == 0 ? "\uE74F" :
                                Volume < 33 ? "\uE993" :
                                Volume < 66 ? "\uE994" : "\uE995";

    public bool IsSeeking { get; set; }

    public MediaPlayer? MediaPlayer => _mediaService.MediaPlayer;
    public LibVLCSharp.Shared.LibVLC? LibVLC => _mediaService?.LibVLC;

    internal AppSettings CurrentSettings => _settings;

    public double MaxVolume => _settings.BoostVolume
    ? Math.Clamp(_settings.VolumeBoostMaxPercent, 100, 200)
    : 100;

    [ObservableProperty] private bool _stopAfterCurrent;

    public ObservableCollection<long> Bookmarks { get; } = new();
    private readonly Dictionary<string, List<long>> _bookmarks = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<long>? IntroSkipped;
    public event EventHandler? OutroSkipped;
    private long _outroThresholdMs = -1;
    private int _outroTriggered;

    public MainViewModel(
        MediaService mediaService,
        SettingsService settingsService,
        AppSettings? initialSettings = null,
        LibraryService? library = null)
    {
        _mediaService = mediaService;
        _settingsService = settingsService;
        _settings = initialSettings ?? _settingsService.Load();
        _library = library;
        ModePreference = _settings.PlayerModePreference;
        _mediaService.PlaybackSettings = _settings;

        Volume = _settings.Volume;
        CurrentTheme = _settings.Theme;
        EqualizerBands = _settings.EqualizerBands;
        EqualizerPreamp = _settings.EqualizerPreamp;
        EqualizerAntiClip = _settings.EqualizerAntiClip;
        EqualizerPreset = EqualizerPresets.Normalize(_settings.EqualizerPreset);
        IsEqualizerEnabled = _settings.EqualizerEnabled;
        RepeatMode = _settings.RepeatMode;
        Shuffle = _settings.Shuffle;
        PlaybackRate = _settings.PlaybackRate;
        IsPlaylistVisible = _settings.ShowPlaylist;
        AudioDelayMs = _settings.AudioDelay;
        if (!string.IsNullOrEmpty(_settings.AspectRatio))
            AspectRatio = _settings.AspectRatio;
        if (_settings.ResumePositions != null)
            foreach (var kv in _settings.ResumePositions)
                _resumePositions[kv.Key] = kv.Value;

        foreach (var f in _settings.RecentFiles.Take(10))
            RecentFilesList.Add(f);
        if (_settings.Bookmarks != null)
            foreach (var kv in _settings.Bookmarks)
                if (kv.Value is { Count: > 0 })
                    _bookmarks[kv.Key] = kv.Value.Distinct().OrderBy(v => v).ToList();

        Volume = Math.Clamp(Volume, 0, MaxVolume);
        _mediaService.AudioFilterChain = BuildAudioFilterChain();
        _mediaService.SetVolume((int)Volume);

        _mediaService.TimeChanged += OnTimeChanged;
        _mediaService.MediaChanged += OnMediaChanged;
        _mediaService.LengthChanged += OnLengthChanged;
        _mediaService.EndReached += OnEndReached;
        _mediaService.EncounteredError += OnEncounteredError;
        _mediaService.VoutChanged += OnVoutChanged;
        _mediaService.Playing += (_, _) => RunOnUi(() =>
        {
            IsPlaying = true;
            IsBuffering = false;
            ApplyCurrentPlaybackOptions();
            RefreshNowPlaying();

            _mediaService.SetMute(IsMuted);
            _mediaService.SetVolume((int)Volume);
        });
        _mediaService.Paused += (_, _) => RunOnUi(() => IsPlaying = false);
        _mediaService.Stopped += (_, _) => RunOnUi(() =>
        {
            IsPlaying = false;
            IsBuffering = false;
            ResetTimeDisplay();
        });
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open media file",
            Filter = MediaFormats.FileDialogFilter,
            Multiselect = true,
            InitialDirectory = GetInitialOpenDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            _settings.LastDirectory = Path.GetDirectoryName(dialog.FileName) ?? "";

            var filesToAdd = dialog.FileNames;
            if (filesToAdd.Length == 1 &&
                string.Equals(_settings.FileOpenBehavior, "OpenSimilarTogether", StringComparison.OrdinalIgnoreCase))
            {
                var expanded = ExpandWithSiblingsInFolder(filesToAdd[0]);
                if (expanded.Length > 1) filesToAdd = expanded;
            }
            AddFilesToPlaylist(filesToAdd, playFirst: true);
        }
    }

    private static string[] ExpandWithSiblingsInFolder(string filePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return new[] { filePath };

            var siblings = Directory.GetFiles(folder)
                .Where(f => MediaFormats.IsMedia(f) && MediaFormats.IsAudio(f) == MediaFormats.IsAudio(filePath))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int idx = siblings.FindIndex(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
            if (idx > 0)
            {
                siblings.RemoveAt(idx);
                siblings.Insert(0, filePath);
            }
            else if (idx < 0)
            {
                siblings.Insert(0, filePath);
            }

            return siblings.ToArray();
        }
        catch
        {
            return new[] { filePath };
        }
    }

    [RelayCommand]
    private void OpenUrl()
    {
        OpenUrlRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenUrlRequested;

    public void AddNetworkUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!MediaFormats.IsNetwork(url)) { ErrorOccurred?.Invoke(this, "The address does not use a supported media protocol."); return; }
        AddFilesToPlaylist(new[] { url }, playFirst: true);
    }

    public void AddFilesToPlaylist(string[] paths, bool playFirst = false)
        => InsertFilesIntoPlaylist(paths, Playlist.Count, playFirst);

    public void InsertFilesIntoPlaylist(string[] paths, int index, bool playFirst = false)
    {
        paths = ExpandPlaylistInputs(paths);
        if (paths.Length == 0) return;
        index = Math.Clamp(index, 0, Playlist.Count);
        bool wasEmpty = Playlist.Count == 0;

        for (int i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            Playlist.Insert(index + i, new MediaItem
            {
                FilePath = path,
                Title = MediaFormats.IsNetwork(path)
                    ? path
                    : Path.GetFileNameWithoutExtension(path),
            });
        }
        for (int i = index; i < Playlist.Count; i++)
            Playlist[i].Index = i;
        if (CurrentItem != null)
            CurrentIndex = Playlist.IndexOf(CurrentItem);

        if (playFirst && _settings.AutoPlay)
            PlayAt(wasEmpty ? 0 : index);

        int token = ++_durationParseToken;
        _ = ParseMediaDurationsAsync(Math.Max(0, CurrentIndex - 20), skipIndex: CurrentIndex, token);
    }

    [RelayCommand]
    private void PlayItem(MediaItem? item)
    {
        if (item == null) return;
        PlayAt(Playlist.IndexOf(item));
    }

    [RelayCommand]
    private void RemoveFromPlaylist(MediaItem? item)
    {
        if (item == null) return;
        Dequeue(item);
        int idx = Playlist.IndexOf(item);
        bool wasPlaying = item == CurrentItem;
        Playlist.Remove(item);

        for (int i = 0; i < Playlist.Count; i++)
            Playlist[i].Index = i;

        if (wasPlaying)
        {
            if (Playlist.Count > 0)
                PlayAt(Math.Min(idx, Playlist.Count - 1));
            else
            {
                Stop();
                HasMedia = false;
                WindowTitle = "FluxPlayer";
                CurrentItem = null;
                CurrentIndex = -1;
            }
        }
    }

    [RelayCommand]
    private void ClearPlaylist()
    {
        foreach (var q in _upNext) q.QueuePosition = 0;
        _upNext.Clear();
        Playlist.Clear();
        Stop();
        HasMedia = false;
        WindowTitle = "FluxPlayer";
        CurrentItem = null;
        CurrentIndex = -1;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (!HasMedia) { OpenFile(); return; }
        _mediaService.TogglePlayPause();
    }

    [RelayCommand]
    private void Stop()
    {
        _mediaService.Stop();
        ResetTimeDisplay();
    }

    [RelayCommand]
    private void Previous()
    {
        if (!Playlist.Any()) return;
        if (_mediaService.GetTime() > 3000)
        {
            _mediaService.SeekTo(0);
            SyncTimeDisplay(0);
            return;
        }
        int next = Shuffle ? Random.Shared.Next(Playlist.Count) :
                   CurrentIndex > 0 ? CurrentIndex - 1 : Playlist.Count - 1;
        PlayAt(next);
    }

    [RelayCommand]
    private void Next()
    {
        if (!Playlist.Any()) return;
        if (TryPlayQueued()) return;
        int next = Shuffle ? Random.Shared.Next(Playlist.Count) :
                   CurrentIndex < Playlist.Count - 1 ? CurrentIndex + 1 : 0;
        PlayAt(next);
    }

    private readonly List<MediaItem> _upNext = new();

    public bool HasQueue => _upNext.Count > 0;

    [RelayCommand]
    private void ToggleQueue(MediaItem? item)
    {
        if (item == null) return;
        if (_upNext.Contains(item)) Dequeue(item);
        else if (item != CurrentItem && Playlist.Contains(item))
        {
            _upNext.Add(item);
            RenumberQueue();
        }
    }

    private void Dequeue(MediaItem item)
    {
        if (_upNext.Remove(item))
        {
            item.QueuePosition = 0;
            RenumberQueue();
        }
    }

    private void RenumberQueue()
    {
        for (int i = 0; i < _upNext.Count; i++)
            _upNext[i].QueuePosition = i + 1;
        OnPropertyChanged(nameof(HasQueue));
    }

    private bool TryPlayQueued()
    {
        while (_upNext.Count > 0)
        {
            var item = _upNext[0];
            Dequeue(item);
            int idx = Playlist.IndexOf(item);
            if (idx >= 0)
            {
                PlayAt(idx);
                return true;
            }
        }
        return false;
    }

    public int SeekStepSeconds => Math.Clamp(_settings.SeekStepSeconds, 1, 600);
    public int SeekLargeStepSeconds => Math.Clamp(_settings.SeekLargeStepSeconds, 5, 3600);
    public int VolumeStep => Math.Clamp(_settings.VolumeStep, 1, 25);

    [RelayCommand]
    private void SeekBackward() => SeekBySeconds(-SeekStepSeconds);

    [RelayCommand]
    private void SeekForward() => SeekBySeconds(SeekStepSeconds);

    public void SeekBySeconds(int seconds)
    {
        if (!HasMedia) return;
        long target = _mediaService.GetTime() + seconds * 1000L;
        _mediaService.SeekRelative(seconds * 1000L);
        SyncTimeDisplay(target);
    }

    private void SyncTimeDisplay(long targetMs)
    {
        if (TotalDurationMs <= 0) return;
        long t = Math.Clamp(targetMs, 0, TotalDurationMs);

        Interlocked.Exchange(ref _seekGuardTargetMs, t);
        Interlocked.Exchange(ref _seekGuardUntilTicks, DateTime.UtcNow.AddMilliseconds(1500).Ticks);
        Interlocked.Exchange(ref _latestTimeMs, t);
        CurrentTime = FormatTime(TimeSpan.FromMilliseconds(t));
        if (!IsSeeking)
            SeekPosition = (double)t / TotalDurationMs * 100.0;
    }

    public void ChangeVolumeBy(int delta)
        => Volume = Math.Clamp(Volume + delta, 0, MaxVolume);

    public void StepPlaybackRate(int direction)
    {
        int idx = SpeedOptions.FindIndex(s => Math.Abs(s - PlaybackRate) < 0.01f);
        if (idx < 0) idx = SpeedOptions.IndexOf(1.0f);
        idx = Math.Clamp(idx + Math.Sign(direction), 0, SpeedOptions.Count - 1);
        PlaybackRate = SpeedOptions[idx];
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _mediaService.SetMute(IsMuted);
        OnPropertyChanged(nameof(VolumeIcon));
    }

    [RelayCommand]
    private void ToggleShuffle() => Shuffle = !Shuffle;

    [RelayCommand]
    private void ToggleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.None
        };
    }

    [RelayCommand]
    private void TogglePlaylist() => IsPlaylistVisible = !IsPlaylistVisible;

    [RelayCommand]
    private void ToggleAbLoop()
    {
        if (!HasMedia) return;
        long now = _mediaService.GetTime();

        if (LoopA < 0)
        {

            LoopA = now;
            AbLoopLabel = $"A: {FormatTime(TimeSpan.FromMilliseconds(now))}";
        }
        else if (LoopB < 0)
        {

            LoopB = now;
            AbLoopLabel = "A-B OK";
        }
        else
        {

            LoopA = -1;
            LoopB = -1;
            AbLoopLabel = "A-B";
        }
    }

    [RelayCommand]
    private void FrameForward()
    {
        if (!HasMedia) return;
        _mediaService.NextFrame();
        IsPlaying = false;
    }

    [RelayCommand]
    private void IncreaseAudioDelay()
    {
        AudioDelayMs += Math.Clamp(_settings.AudioDelayStepMs, 10, 1000);
    }

    [RelayCommand]
    private void DecreaseAudioDelay()
    {
        AudioDelayMs -= Math.Clamp(_settings.AudioDelayStepMs, 10, 1000);
    }

    [RelayCommand]
    private void ResetAudioDelay() => AudioDelayMs = 0;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAvOffset))] private long _subtitleDelayMs = 0;

    [RelayCommand]
    private void IncreaseSubtitleDelay()
        => SubtitleDelayMs += Math.Clamp(_settings.SubtitleDelayStepMs, 10, 1000);

    [RelayCommand]
    private void DecreaseSubtitleDelay()
        => SubtitleDelayMs -= Math.Clamp(_settings.SubtitleDelayStepMs, 10, 1000);

    [RelayCommand]
    private void ResetSubtitleDelay() => SubtitleDelayMs = 0;

    partial void OnSubtitleDelayMsChanged(long value)
    {
        _mediaService.SetSubtitleDelay(value * 1000);
    }

    [RelayCommand]
    private void ToggleEqualizer() => IsEqualizerPanelOpen = !IsEqualizerPanelOpen;

    public void SeekToPercent(double percent)
    {
        _mediaService.SeekToPercent((float)(percent / 100.0));
        SyncTimeDisplay((long)(TotalDurationMs * Math.Clamp(percent, 0, 100) / 100.0));
    }

    partial void OnVolumeChanged(double value)
    {
        _mediaService.SetVolume((int)value);
        if (value > 0 && IsMuted) { IsMuted = false; _mediaService.SetMute(false); }
        OnPropertyChanged(nameof(VolumeIcon));
    }

    partial void OnPlaybackRateChanged(float value) => _mediaService.SetRate(value);

    partial void OnAspectRatioChanged(string value)
    {
        _mediaService.SetAspectRatio(value == "Auto" || value == "Stretch" ? null : value);
    }

    partial void OnAudioDelayMsChanged(long value)
    {
        _mediaService.SetAudioDelay(value * 1000);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackInfo? value)
    {
        if (value != null) _mediaService.SetSubtitleTrack(value.Id);
    }

    partial void OnSelectedAudioTrackChanged(TrackInfo? value)
    {
        if (value != null) _mediaService.SetAudioTrack(value.Id);
    }

    [RelayCommand]
    private void LoadSubtitleFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load external subtitle",
            Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
            _mediaService.LoadSubtitle(dialog.FileName);
    }

    partial void OnIsEqualizerEnabledChanged(bool value) => PushEqualizer();
    partial void OnEqualizerAntiClipChanged(bool value) => PushEqualizer();

    public float EffectiveEqualizerPreamp
    {
        get
        {
            float pre = EqualizerPreamp;
            if (EqualizerAntiClip)
            {
                float peak = Math.Max(0f, EqualizerBands.Max() + pre);
                pre -= peak;
            }
            return Math.Clamp(pre, -20f, 20f);
        }
    }

    private void PushEqualizer()
    {
        if (_mediaService == null) return;
        if (IsEqualizerEnabled) _mediaService.ApplyEqualizer(EqualizerBands, EffectiveEqualizerPreamp);
        else _mediaService.DisableEqualizer();
        OnPropertyChanged(nameof(EffectiveEqualizerPreamp));
    }

    public void SetEqualizerBand(int bandIndex, float value)
    {
        if (bandIndex < 0 || bandIndex >= EqualizerBands.Length) return;
        if (Math.Abs(EqualizerBands[bandIndex] - value) < 0.001f) return;
        EqualizerBands[bandIndex] = value;
        EqualizerPreset = EqualizerPresets.Custom;
        PushEqualizer();
    }

    public void SetEqualizerPreamp(float value)
    {
        value = Math.Clamp(value, -12f, 12f);
        if (Math.Abs(EqualizerPreamp - value) < 0.001f) return;
        EqualizerPreamp = value;
        EqualizerPreset = EqualizerPresets.Custom;
        PushEqualizer();
    }

    [RelayCommand]
    private void ApplyEqPreset(string preset)
    {
        preset = EqualizerPresets.Normalize(preset);
        EqualizerPreset = preset;
        var p = EqualizerPresets.Find(preset);
        if (p == null) return;
        EqualizerPreamp = p.Preamp;
        EqualizerBands = (float[])p.Bands.Clone();
        PushEqualizer();
    }

    [RelayCommand]
    private void ResetEqualizer() => ApplyEqPreset(EqualizerPresets.Flat);

    partial void OnCurrentThemeChanged(AppTheme value)
    {
        ThemeChangeRequested?.Invoke(this, value);

        if (_settings == null || _settingsService == null || _settings.Theme == value) return;
        _settings.Theme = value;
        try
        {
            var disk = _settingsService.Load();
            if (disk.Theme != value)
            {
                disk.Theme = value;
                _settingsService.Save(disk);
            }
        }
        catch { }
    }

    public event EventHandler<AppTheme>? ThemeChangeRequested;

    [RelayCommand]
    private void TakeSnapshot()
    {
        if (!HasMedia) return;
        var folder = string.IsNullOrEmpty(_settings.SnapshotFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : _settings.SnapshotFolder;
        var ext = _settings.SnapshotFormat?.ToLowerInvariant() ?? "png";
        var filename = $"FluxPlayer_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        var fullPath = Path.Combine(folder, filename);
        bool ok;
        try
        {
            Directory.CreateDirectory(folder);
            ok = _mediaService.TakeSnapshot(fullPath);
        }
        catch
        {
            ok = false;
        }
        SnapshotTaken?.Invoke(this, ok ? fullPath : null);
    }

    public event EventHandler<string?>? SnapshotTaken;

    public event EventHandler? PlaybackCloseRequested;

    public void PlayAt(int index)
    {
        if (index < 0 || index >= Playlist.Count) return;

        Dequeue(Playlist[index]);
        RememberResumePosition();

        foreach (var it in Playlist)
            if (it.IsPlaying) it.IsPlaying = false;

        LoopA = -1;
        LoopB = -1;
        AbLoopLabel = "A-B";

        CurrentIndex = index;
        CurrentItem = Playlist[index];
        ResetNowPlaying(CurrentItem);
        CurrentItem.IsPlaying = true;
        WindowTitle = $"FluxPlayer  —  {CurrentItem.Title}";
        HasMedia = true;
        IsPlaying = false;
        ResetTimeDisplay();
        AddToRecentFiles(CurrentItem.FilePath);
        IsBuffering = true;

        _pendingResumeMs = _settings.ResumeLastPosition &&
                           _resumePositions.TryGetValue(ResumeKey(CurrentItem.FilePath), out var saved)
            ? saved
            : -1;

        Interlocked.Exchange(ref _outroThresholdMs, -1);
        Volatile.Write(ref _outroTriggered, 0);
        LoadBookmarksForCurrent();

        _mediaService.PlayFile(CurrentItem.FilePath);
    }

    private readonly Dictionary<string, long> _resumePositions = new(StringComparer.OrdinalIgnoreCase);
    private long _pendingResumeMs = -1;

    public event EventHandler<long>? PlaybackResumed;

    private static string ResumeKey(string path) => path.Trim().ToLowerInvariant();

    private void RememberResumePosition()
    {
        if (CurrentItem == null || !HasMedia) return;
        string key = ResumeKey(CurrentItem.FilePath);
        long time = _mediaService.GetTime();
        long length = _mediaService.GetDuration();

        if (length > 120_000 && time > 15_000 && time < length - 30_000)
        {
            _resumePositions.Remove(key);
            _resumePositions[key] = time;
            while (_resumePositions.Count > 300)
                _resumePositions.Remove(_resumePositions.Keys.First());
        }
        else
        {
            _resumePositions.Remove(key);
        }
    }

    private long _latestTimeMs;
    private int _timeUpdatePending;
    private int _abSeekPending;
    private long _abSeekBlockedUntilTicks;
    private long _seekGuardTargetMs = -1;
    private long _seekGuardUntilTicks;

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        long guardTarget = Interlocked.Read(ref _seekGuardTargetMs);
        if (guardTarget >= 0)
        {
            bool expired = DateTime.UtcNow.Ticks > Interlocked.Read(ref _seekGuardUntilTicks);
            if (expired || Math.Abs(e.Time - guardTarget) <= 1200)
                Interlocked.Exchange(ref _seekGuardTargetMs, -1);
            else
                return;
        }

        if (LoopA >= 0 && LoopB >= 0 && e.Time >= LoopB)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks >= Interlocked.Read(ref _abSeekBlockedUntilTicks)
                && Interlocked.Exchange(ref _abSeekPending, 1) == 0)
            {
                long target = LoopA;
                Interlocked.Exchange(ref _abSeekBlockedUntilTicks, nowTicks + TimeSpan.TicksPerMillisecond * 400);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { _mediaService.SeekTo(target); }
                    catch { }
                    finally { Volatile.Write(ref _abSeekPending, 0); }
                });
            }
            return;
        }

        long outroAt = Interlocked.Read(ref _outroThresholdMs);
        if (outroAt > 0 && e.Time >= outroAt && Interlocked.Exchange(ref _outroTriggered, 1) == 0)
        {
            Application.Current?.Dispatcher.InvokeAsync(() => OutroSkipped?.Invoke(this, EventArgs.Empty));
            OnEndReached(this, EventArgs.Empty);
            return;
        }

        Interlocked.Exchange(ref _latestTimeMs, e.Time);
        if (Interlocked.Exchange(ref _timeUpdatePending, 1) != 0)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            Volatile.Write(ref _timeUpdatePending, 0);
            return;
        }

        dispatcher.InvokeAsync(() =>
        {
            Volatile.Write(ref _timeUpdatePending, 0);
            long time = Interlocked.Read(ref _latestTimeMs);
            CurrentTime = FormatTime(TimeSpan.FromMilliseconds(time));
            UpdateAudioPosition(time);

            if (!IsSeeking && TotalDurationMs > 0)
                SeekPosition = (double)time / TotalDurationMs * 100.0;
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TotalDurationMs = e.Length;
            TotalTime = FormatTime(TimeSpan.FromMilliseconds(e.Length));

            if (e.Length > 0 && CurrentItem != null)
            {
                CurrentItem.Duration = TotalTime;
                CurrentItem.DurationMs = e.Length;
            }

            if (_pendingResumeMs > 0 && e.Length > 0)
            {
                long resumeAt = _pendingResumeMs;
                _pendingResumeMs = -1;
                if (resumeAt < e.Length - 5_000)
                {
                    _mediaService.SeekTo(resumeAt);
                    PlaybackResumed?.Invoke(this, resumeAt);
                }
            }
            else if (e.Length > 0 && FeatureCatalog.IsEnabled(_settings, FeatureCatalog.AutoSkipIntroOutro))
            {
                long intro = _settings.IntroSkipSeconds * 1000L;
                long outro = _settings.OutroSkipSeconds * 1000L;

                if (intro > 0 && e.Length > intro + outro + 60_000 && _mediaService.GetTime() < intro)
                {
                    _mediaService.SeekTo(intro);
                    IntroSkipped?.Invoke(this, intro);
                }
                if (outro > 0 && e.Length > intro + outro + 60_000)
                    Interlocked.Exchange(ref _outroThresholdMs, e.Length - outro);
            }
        });
    }

    private void OnMediaChanged(object? sender, MediaPlayerMediaChangedEventArgs e)
    {
        int token = Interlocked.Increment(ref _mediaChangeToken);
        RunOnUi(() => IsBuffering = true);
        _ = RefreshTracksAfterMediaChangeAsync(token);
    }

    private async Task RefreshTracksAfterMediaChangeAsync(int token)
    {
        try
        {
            await Task.Delay(350);
            if (token != _mediaChangeToken)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            await dispatcher.InvokeAsync(RefreshTracks);
            await dispatcher.InvokeAsync(RefreshNowPlaying);
        }
        catch (InvalidOperationException)
        {

        }
    }

    private void OnVoutChanged(object? sender, MediaPlayerVoutEventArgs e)
    {
        if (e.Count > 0)
            RunOnUi(() =>
            {
                IsBuffering = false;
            });
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (StopAfterCurrent)
            {
                StopAfterCurrent = false;
                Stop();
                SleepTimerFired?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (RepeatMode == RepeatMode.One)
            {
                PlayAt(CurrentIndex);
                return;
            }

            if (TryPlayQueued()) return;

            if (RepeatMode == RepeatMode.All)
            {
                Next();
                return;
            }

            switch (_settings.DefaultPlaybackOnEnd?.Trim())
            {
                case "Repeat":
                    PlayAt(CurrentIndex);
                    break;
                case "Next":
                    if (CurrentIndex < Playlist.Count - 1) Next();
                    else Stop();
                    break;
                case "Close":
                    PlaybackCloseRequested?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    Stop();
                    break;
            }
        });
    }

    private void RefreshTracks()
    {
        var videoTracks = _mediaService.GetVideoTracks();
        var currentVideoTrack = _mediaService.GetVideoTrack();
        if (videoTracks.Length > 0 && currentVideoTrack < 0)
        {
            var firstEnabledVideoTrack = videoTracks.FirstOrDefault(t => t.Id >= 0);
            if (firstEnabledVideoTrack != null)
                _mediaService.SetVideoTrack(firstEnabledVideoTrack.Id);
        }

        SubtitleTracks.Clear();
        SubtitleTracks.Add(new TrackInfo(-1, "Disabled"));
        foreach (var t in _mediaService.GetSubtitleTracks())
            SubtitleTracks.Add(t);
        var selectedSpu = _mediaService.MediaPlayer.Spu;
        SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(t => t.Id == selectedSpu) ?? SubtitleTracks.FirstOrDefault();

        AudioTracks.Clear();
        foreach (var t in _mediaService.GetAudioTracks())
            AudioTracks.Add(t);
        var currentAudioTrack = _mediaService.GetAudioTrack();
        SelectedAudioTrack = AudioTracks.FirstOrDefault(t => t.Id == currentAudioTrack)
            ?? AudioTracks.FirstOrDefault(t => t.Id >= 0)
            ?? AudioTracks.FirstOrDefault();
    }

    private void ResetTimeDisplay()
    {
        CurrentTime = "0:00";
        TotalTime = "0:00";
        SeekPosition = 0;
        TotalDurationMs = 0;
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");

    private string GetInitialOpenDirectory()
    {
        if (Directory.Exists(_settings.OpenDialogStartFolder))
            return _settings.OpenDialogStartFolder;
        if (Directory.Exists(_settings.LastDirectory))
            return _settings.LastDirectory;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.InvokeAsync(action);
    }

    public void SaveSettings()
    {
        RememberResumePosition();

        _settings = _settingsService.Load();

        _settings.ResumePositions = new Dictionary<string, long>(_resumePositions);
        _settings.Bookmarks = _bookmarks.Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        _settings.Volume = Volume;
        _settings.Theme = CurrentTheme;
        _settings.EqualizerEnabled = IsEqualizerEnabled;
        _settings.EqualizerBands = EqualizerBands;
        _settings.EqualizerPreset = EqualizerPreset;
        _settings.EqualizerPreamp = EqualizerPreamp;
        _settings.EqualizerAntiClip = EqualizerAntiClip;
        _settings.RepeatMode = RepeatMode;
        _settings.Shuffle = Shuffle;
        _settings.PlaybackRate = PlaybackRate;
        _settings.AudioDelay = (int)AudioDelayMs;
        _settings.ShowPlaylist = IsPlaylistVisible;
        _settings.AspectRatio = AspectRatio;
        _settings.RecentFiles = RecentFilesList.Take(_settings.MaxRecentFiles).ToList();
        _settings.PlayerModePreference = ModePreference;
        _settings.SavedPlaylist = _settings.RememberPlaylist ? Playlist.Select(p => p.FilePath).Take(PlaylistService.MaxEntries).ToList() : new();
        _settings.SavedPlaylistIndex = CurrentIndex;
        _settingsService.Save(_settings);
    }

    public void ReloadSettingsFromDisk()
    {
        _settings = _settingsService.Load();
        _mediaService.PlaybackSettings = _settings;
        OnPropertyChanged(nameof(SeekStepSeconds));
        OnPropertyChanged(nameof(SeekLargeStepSeconds));
        OnPropertyChanged(nameof(VolumeStep));
        OnPropertyChanged(nameof(MaxVolume));
        if (Volume > MaxVolume) Volume = MaxVolume;
        _mediaService.AudioFilterChain = BuildAudioFilterChain();
    }

    private string? BuildAudioFilterChain()
    {
        var filters = new List<string>(2);
        if (_settings.NormalizeVolume || FeatureCatalog.IsEnabled(_settings, FeatureCatalog.VolumeNormalization))
            filters.Add("normvol");
        if (FeatureCatalog.IsEnabled(_settings, FeatureCatalog.DynamicCompressorLimiter))
            filters.Add("compressor");
        return filters.Count == 0 ? null : string.Join(":", filters);
    }

    public event EventHandler? SleepTimerFired;

    private void LoadBookmarksForCurrent()
    {
        Bookmarks.Clear();
        if (CurrentItem != null && _bookmarks.TryGetValue(ResumeKey(CurrentItem.FilePath), out var list))
            foreach (var ms in list) Bookmarks.Add(ms);
    }

    public long AddBookmarkAtCurrent()
    {
        if (!HasMedia || CurrentItem == null) return -1;
        long now = Math.Max(0, _mediaService.GetTime());

        if (Bookmarks.Any(b => Math.Abs(b - now) < 1000)) return -1;
        string key = ResumeKey(CurrentItem.FilePath);
        if (!_bookmarks.TryGetValue(key, out var list))
            _bookmarks[key] = list = new List<long>();
        list.Add(now);
        list.Sort();
        LoadBookmarksForCurrent();
        return now;
    }

    public long JumpToBookmark(int direction)
    {
        if (!HasMedia || Bookmarks.Count == 0) return -1;
        long now = _mediaService.GetTime();
        long target = direction > 0
            ? Bookmarks.Where(b => b > now + 1500).DefaultIfEmpty(-1).First()
            : Bookmarks.Where(b => b < now - 1500).DefaultIfEmpty(-1).Last();
        if (target >= 0) { _mediaService.SeekTo(target); SyncTimeDisplay(target); }
        return target;
    }

    public void SeekToMs(long ms)
    {
        if (!HasMedia) return;
        _mediaService.SeekTo(Math.Max(0, ms));
        SyncTimeDisplay(ms);
    }

    public void ClearBookmarks()
    {
        if (CurrentItem == null) return;
        _bookmarks.Remove(ResumeKey(CurrentItem.FilePath));
        Bookmarks.Clear();
    }

    public void RemoveBookmark(long ms)
    {
        if (CurrentItem == null) return;
        if (_bookmarks.TryGetValue(ResumeKey(CurrentItem.FilePath), out var list))
            list.Remove(ms);
        LoadBookmarksForCurrent();
    }

    private void AddToRecentFiles(string path)
    {
        RecentFilesList.Remove(path);
        RecentFilesList.Insert(0, path);
        while (RecentFilesList.Count > 10)
            RecentFilesList.RemoveAt(RecentFilesList.Count - 1);
    }

    [RelayCommand]
    private void OpenRecentFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path) && !path.StartsWith("http") && !path.StartsWith("rtsp")) return;
        AddFilesToPlaylist(new[] { path }, playFirst: true);
    }

    [RelayCommand]
    private void ClearRecentFiles() => RecentFilesList.Clear();

    [RelayCommand]
    private void OpenPlaylistFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open playlist",
            Filter = "Playlists|*.m3u;*.m3u8;*.pls;*.xspf|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
            LoadPlaylistFromFile(dialog.FileName);
    }

    public void LoadPlaylistFromFile(string path)
    {
        AddFilesToPlaylist(new[] { path }, playFirst: true);
    }

    public static string[] ReadPlaylistEntries(string path)
    {
        return PlaylistService.Read(path);
    }

    [RelayCommand]
    private void SavePlaylistFile()
    {
        if (!Playlist.Any()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save playlist",
            Filter = "UTF-8 playlist|*.m3u8|M3U playlist|*.m3u|PLS playlist|*.pls|XSPF playlist|*.xspf",
            FileName = "FluxPlayer.m3u8"
        };
        if (dialog.ShowDialog() == true)
        {
            try { PlaylistService.Write(dialog.FileName, Playlist); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { ErrorOccurred?.Invoke(this, $"Unable to save the playlist: {e.Message}"); }
        }
    }

    public void MovePlaylistItem(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Playlist.Count ||
            newIndex < 0 || newIndex >= Playlist.Count || oldIndex == newIndex)
            return;
        Playlist.Move(oldIndex, newIndex);
        for (int i = 0; i < Playlist.Count; i++)
            Playlist[i].Index = i;
        if (CurrentItem != null)
            CurrentIndex = Playlist.IndexOf(CurrentItem);
    }

    private async Task ParseMediaDurationsAsync(int startIndex, int skipIndex, int token)
    {

        // Bound background parsing and skip the active item to avoid competing native parses.
        // Remaining durations are populated on playback or a later explicit operation.

        int endIndex = Math.Min(Playlist.Count, startIndex + 200);
        for (int i = startIndex; i < endIndex && i < Playlist.Count; i++)
        {
            if (token != _durationParseToken)
                return;

            if (i == CurrentIndex)
                continue;

            var item = Playlist[i];
            if (item.Duration != "--:--") continue;
            try
            {
                var durationMs = await _mediaService.GetMediaDurationAsync(item.FilePath);
                if (token != _durationParseToken)
                    return;

                if (durationMs > 0)
                {
                    var ts = TimeSpan.FromMilliseconds(durationMs);
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                        item.Duration = FormatTime(ts));
                }
            }
            catch { }
        }
    }

    public bool TryRecoverVideoOutput() => _mediaService.TryRecoverVideoOutput();

    public event EventHandler<string>? ErrorOccurred;

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
            ErrorOccurred?.Invoke(this, "Unable to play the file"));
    }

    public string GetMediaInfo()
    {
        if (CurrentItem == null) return "No media loaded.";

        var lines = new List<string>();
        lines.Add($"File: {CurrentItem.Title}");

        if (TotalDurationMs > 0)
        {
            var ts = TimeSpan.FromMilliseconds(TotalDurationMs);
            lines.Add($"Duration: {(ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss"))}");
        }

        var info = _mediaService.GetMediaInfo();
        if (!string.IsNullOrEmpty(info))
            lines.Add(info);

        lines.Add($"Speed: {PlaybackRate}x");
        lines.Add($"Aspect ratio: {AspectRatio}");

        return string.Join("\n", lines);
    }
}
