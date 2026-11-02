using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluxPlayer;

public partial class MainWindow
{
    private LibraryCollectionsWindow? _collectionsWindow;
    private PlaybackHistoryWindow? _historyWindow;
    private void PlaybackHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_historyWindow != null) { _historyWindow.Activate(); return; }
        _historyWindow = new(this, _vm); _historyWindow.Closed += (_, _) => _historyWindow = null; _historyWindow.Show();
    }
    private AdvancedCueWindow? _advancedCueWindow;
    private void AdvancedCue_Click(object sender, RoutedEventArgs e)
    {
        if (_advancedCueWindow != null) { _advancedCueWindow.Activate(); return; }
        _advancedCueWindow = new(this, _vm); _advancedCueWindow.Closed += (_, _) => _advancedCueWindow = null; _advancedCueWindow.Show();
    }
    private void LibraryCollections_Click(object sender, RoutedEventArgs e)
    {
        if (_collectionsWindow != null) { _collectionsWindow.Activate(); return; }
        _collectionsWindow = new(this, _vm); _collectionsWindow.Closed += (_, _) => _collectionsWindow = null; _collectionsWindow.Show();
    }
    private LibraryWindow? _libraryWindow;
    private CueSheetEditorWindow? _cueEditorWindow;
    private SnapshotScheduleWindow? _snapshotScheduleWindow;
    private BatchTagEditorWindow? _batchTagWindow;
    private SubtitleTimingWindow? _subtitleTimingWindow;
    private SubtitleStyleWindow? _subtitleStyleWindow;
    private AudioOutputProfilesWindow? _audioOutputProfilesWindow;
    private ChapterEditorWindow? _chapterEditorWindow;
    private bool _audioLayoutApplied;
    private Size _videoModeSize = new(1280, 760);
    private int _videoRotation;
    private bool _videoMirrored;
    private bool _videoFlipped;

    private void InitializeAudioWorkspace()
    {
        _vm.PropertyChanged += OnAudioWorkspacePropertyChanged;
        ApplyPlayerMode();
    }

    private void OnAudioWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveMode)) ApplyPlayerMode();
        else if (e.PropertyName == nameof(MainViewModel.HasMedia)) UpdateVideoSurface();
    }

    private void UpdateVideoSurface()
    {
        VideoHostContainer.Visibility = _vm.HasMedia && !_vm.IsAudioMode ? Visibility.Visible : Visibility.Collapsed;
        EmptyMediaState.Visibility = !_vm.HasMedia && !_vm.IsAudioMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPlayerMode()
    {
        if (_videoHealthTimer == null) return;
        bool audio = _vm.IsAudioMode;
        if (audio && _isFullscreen) ToggleFullscreen();
        if (audio != _audioLayoutApplied)
        {
            if (audio)
            {
                if (WindowState == WindowState.Normal) _videoModeSize = new(Width, Height);
                MediaWorkspace.RowDefinitions[0].Height = GridLength.Auto;
                MediaWorkspace.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                MediaWorkspace.ColumnDefinitions[1].Width = new GridLength(0);
                Grid.SetRow(PlaylistPanel, 1); Grid.SetColumn(PlaylistPanel, 0);
                VideoGrid.Height = 202;
                PlaylistPanel.Width = double.NaN;
                if (WindowState == WindowState.Normal)
                {
                    Width = Math.Clamp(_vm.CurrentSettings.AudioWindowWidth, MinWidth, SystemParameters.WorkArea.Width);
                    Height = Math.Clamp(_vm.CurrentSettings.AudioWindowHeight, MinHeight, SystemParameters.WorkArea.Height);
                }
            }
            else
            {
                RememberAudioSize();
                MediaWorkspace.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                MediaWorkspace.RowDefinitions[1].Height = new GridLength(0);
                MediaWorkspace.ColumnDefinitions[1].Width = GridLength.Auto;
                Grid.SetRow(PlaylistPanel, 0); Grid.SetColumn(PlaylistPanel, 1);
                VideoGrid.Height = double.NaN;
                if (WindowState == WindowState.Normal)
                {
                    Width = Math.Clamp(_videoModeSize.Width, MinWidth, SystemParameters.WorkArea.Width);
                    Height = Math.Clamp(_videoModeSize.Height, MinHeight, SystemParameters.WorkArea.Height);
                }
            }
            _audioLayoutApplied = audio;
        }
        AudioSurface.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
        VideoBackdrop.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        VideoMouseOverlay.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        FullscreenBtn.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        SnapshotButton.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        UpdateVideoSurface();
        if (audio) { _videoHealthTimer.Stop(); ResetVideoHealthState(); }
        else if (IsLoaded) _videoHealthTimer.Start();
        UpdateAmbientState();
        UpdateResponsiveLayout();
    }

    private void RememberAudioSize()
    {
        if (WindowState != WindowState.Normal) return;
        var service = new SettingsService(); var settings = service.Load();
        settings.AudioWindowWidth = Width; settings.AudioWindowHeight = Height;
        service.Save(settings);
        _vm.CurrentSettings.AudioWindowWidth = Width; _vm.CurrentSettings.AudioWindowHeight = Height;
    }

    private void PlayerModeButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (MenuItem item in PlayerModeMenu.Items)
            item.IsChecked = item.Tag?.ToString() == _vm.ModePreference.ToString();
        PlayerModeMenu.PlacementTarget = PlayerModeButton; PlayerModeMenu.IsOpen = true;
    }
    private void PlayerModeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<PlayerModePreference>(tag, out var mode))
            _vm.SetModePreference(mode);
    }
    private void LibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryWindow != null) { _libraryWindow.Activate(); return; }
        _libraryWindow = new LibraryWindow(_vm) { Owner = this };
        _libraryWindow.Closed += (_, _) => _libraryWindow = null;
        _libraryWindow.Show();
    }
    private void MediaToolsButton_Click(object sender, RoutedEventArgs e)
    { MediaToolsMenu.PlacementTarget = MediaToolsButton; MediaToolsMenu.IsOpen = true; }

    private void MediaToolsMenu_Opened(object sender, RoutedEventArgs e)
    {
        foreach (var item in ReplayGainMenu.Items.OfType<MenuItem>()) { item.IsCheckable = true; item.IsChecked = item.Tag?.ToString() == _vm.CurrentSettings.ReplayGainMode; }
        foreach (var item in ChannelRoutingMenu.Items.OfType<MenuItem>()) { item.IsCheckable = true; item.IsChecked = item.Tag?.ToString() == _vm.CurrentSettings.AudioChannels; }
        _vm.RefreshOutputDevices(); OutputDevicesMenu.Items.Clear();
        foreach (var device in _vm.OutputDevices)
        {
            var entry = new MenuItem
            {
                Header = device.Name,
                IsCheckable = true,
                IsChecked = device.Id == _vm.CurrentSettings.AudioOutputDevice
            };
            entry.Click += (_, _) =>
            {
                if (!_vm.SelectOutputDevice(device))
                    ShowActionOsd("\uE7BA", "The output change could not be saved: " + _vm.AudioOutputProfileSaveError);
            };
            OutputDevicesMenu.Items.Add(entry);
        }
        ChaptersMenu.Items.Clear();
        foreach (var chapter in _vm.Chapters.Take(200))
        {
            var entry = new MenuItem
            {
                Header = new TextBlock { Text = chapter.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 },
                ToolTip = ChapterDocument.FormatTime(Math.Clamp(chapter.TimeMs, 0, ChapterDocument.MaximumTimeMs)) + " · " + chapter.Name,
                IsEnabled = _vm.TotalDurationMs <= 0 || chapter.TimeMs < _vm.TotalDurationMs
            };
            entry.Click += (_, _) => _vm.SeekToMs(chapter.TimeMs);
            ChaptersMenu.Items.Add(entry);
        }
        if (_vm.Chapters.Count > 200)
        {
            var more = new MenuItem { Header = $"View all {_vm.Chapters.Count:N0} chapters in the editor…" };
            more.Click += ChapterEditor_Click; ChaptersMenu.Items.Add(new Separator()); ChaptersMenu.Items.Add(more);
        }
        ChaptersMenu.IsEnabled = _vm.Chapters.Count > 0;
    }

    private void ReplayGainItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string mode }) return;
        if (!_vm.SetQuickAudioPreference("replaygain", mode)) return;
        _runtimeSettings.ReplayGainMode = mode;
        ShowActionOsd("\uE9E9", "ReplayGain will apply when opening the next track");
    }

    private void AudioOutputProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (_audioOutputProfilesWindow != null) { _audioOutputProfilesWindow.Activate(); return; }
        _audioOutputProfilesWindow = new AudioOutputProfilesWindow(this, _vm);
        _audioOutputProfilesWindow.Closed += (_, _) => _audioOutputProfilesWindow = null;
        _audioOutputProfilesWindow.Show();
    }

    private void EditTagsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CurrentItem != null) MediaToolsWindows.ShowTagEditor(this, _vm, _vm.CurrentItem.FilePath);
    }
    private void BatchTagsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_batchTagWindow != null) { _batchTagWindow.Activate(); return; }
        _batchTagWindow = new BatchTagEditorWindow(this, _vm, _vm.Playlist.Select(item => item.FilePath));
        _batchTagWindow.Closed += (_, _) => _batchTagWindow = null;
        _batchTagWindow.Show();
    }
    private void ConverterButton_Click(object sender, RoutedEventArgs e) =>
        new ConverterWindow(this, _vm, _vm.Playlist.Select(p => p.FilePath).ToArray()).Show();
    private void SearchSubtitlesButton_Click(object sender, RoutedEventArgs e) => MediaToolsWindows.ShowSubtitleSearch(this, _vm);
    private void SubtitleTiming_Click(object sender, RoutedEventArgs e)
    {
        if (_subtitleTimingWindow != null) { _subtitleTimingWindow.Activate(); return; }
        _subtitleTimingWindow = new SubtitleTimingWindow(this, _vm);
        _subtitleTimingWindow.Closed += (_, _) => _subtitleTimingWindow = null;
        _subtitleTimingWindow.Show();
    }
    private void SubtitleStyles_Click(object sender, RoutedEventArgs e)
    {
        if (_subtitleStyleWindow != null) { _subtitleStyleWindow.Activate(); return; }
        _subtitleStyleWindow = new SubtitleStyleWindow(this, _vm);
        _subtitleStyleWindow.Closed += (_, _) => _subtitleStyleWindow = null;
        _subtitleStyleWindow.Show();
    }
    private void OpenCueButton_Click(object sender, RoutedEventArgs e) => MediaToolsWindows.ShowCueSheet(this, _vm);
    private void ChapterEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_chapterEditorWindow != null) { _chapterEditorWindow.Activate(); return; }
        _chapterEditorWindow = new ChapterEditorWindow(this, _vm);
        _chapterEditorWindow.Closed += (_, _) => _chapterEditorWindow = null;
        _chapterEditorWindow.Show();
    }
    private LyricsEditorWindow? _lyricsEditorWindow;
    private FileRenameWindow? _fileRenameWindow;
    private void FileRename_Click(object sender, RoutedEventArgs e)
    {
        if (_fileRenameWindow != null) { _fileRenameWindow.Activate(); return; }
        _fileRenameWindow = new FileRenameWindow(this, _vm);
        _fileRenameWindow.Closed += (_, _) => _fileRenameWindow = null;
        _fileRenameWindow.Show();
    }
    private FileRenameWindow? _organizationWindow;
    private void OrganizeFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_organizationWindow != null) { _organizationWindow.Activate(); return; }
        _organizationWindow = new FileRenameWindow(this, _vm, organize: true);
        _organizationWindow.Closed += (_, _) => _organizationWindow = null; _organizationWindow.Show();
    }
    private FolderBrowserWindow? _folderBrowserWindow;
    private void BrowseMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_folderBrowserWindow != null) { _folderBrowserWindow.Activate(); return; }
        _folderBrowserWindow = new FolderBrowserWindow(this, _vm);
        _folderBrowserWindow.Closed += (_, _) => _folderBrowserWindow = null;
        _folderBrowserWindow.Show();
    }
    private void LyricsEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_lyricsEditorWindow != null) { _lyricsEditorWindow.Activate(); return; }
        _lyricsEditorWindow = new LyricsEditorWindow(this, _vm);
        _lyricsEditorWindow.Closed += (_, _) => _lyricsEditorWindow = null;
        _lyricsEditorWindow.Show();
    }
    private void EditCueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cueEditorWindow != null) { _cueEditorWindow.Activate(); return; }
        _cueEditorWindow = new CueSheetEditorWindow(this, _vm);
        _cueEditorWindow.Closed += (_, _) => _cueEditorWindow = null;
        _cueEditorWindow.Show();
    }
    private void ScheduledSnapshots_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshotScheduleWindow != null) { _snapshotScheduleWindow.Activate(); return; }
        _snapshotScheduleWindow = new SnapshotScheduleWindow(this, _vm);
        _snapshotScheduleWindow.Closed += (_, _) => _snapshotScheduleWindow = null;
        _snapshotScheduleWindow.Show();
    }
    private void RotateVideo_Click(object sender, RoutedEventArgs e) { _videoRotation = (_videoRotation + 90) % 360; ApplyImageOrientation(); }
    private void MirrorVideo_Click(object sender, RoutedEventArgs e) { _videoMirrored = !_videoMirrored; ApplyImageOrientation(); }
    private void ResetOrientation_Click(object sender, RoutedEventArgs e) { _videoRotation = 0; _videoMirrored = _videoFlipped = false; ApplyImageOrientation(); }
    private void FlipVideo_Click(object sender, RoutedEventArgs e) { _videoFlipped = !_videoFlipped; ApplyImageOrientation(); }
    private void ApplyImageOrientation()
    {
        _vm.Engine.SetOrientation(_videoRotation, _videoMirrored, _videoFlipped);
    }
    private void SignalPath_Click(object sender, RoutedEventArgs e)
    {
        if (_audioPathWindow != null) { _audioPathWindow.Activate(); return; }
        _audioPathWindow = new(this, _vm); _audioPathWindow.Closed += (_, _) => _audioPathWindow = null; _audioPathWindow.Show();
    }
}
