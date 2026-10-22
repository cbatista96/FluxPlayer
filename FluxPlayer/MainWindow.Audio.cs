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
    private LibraryWindow? _libraryWindow;
    private CueSheetEditorWindow? _cueEditorWindow;
    private SnapshotScheduleWindow? _snapshotScheduleWindow;
    private bool _audioLayoutApplied;
    private Size _videoModeSize = new(1280, 760);
    private int _videoRotation;
    private bool _videoMirrored;

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
        _vm.RefreshOutputDevices(); OutputDevicesMenu.Items.Clear();
        foreach (var device in _vm.OutputDevices)
        {
            var entry = new MenuItem
            {
                Header = device.Name,
                IsCheckable = true,
                IsChecked = device.Id == _vm.CurrentSettings.AudioOutputDevice
            };
            entry.Click += (_, _) => _vm.SelectOutputDevice(device);
            OutputDevicesMenu.Items.Add(entry);
        }
        ChaptersMenu.Items.Clear();
        foreach (var chapter in _vm.Chapters)
        {
            var entry = new MenuItem { Header = chapter.Name };
            entry.Click += (_, _) => _vm.SeekToMs(chapter.TimeMs);
            ChaptersMenu.Items.Add(entry);
        }
        ChaptersMenu.IsEnabled = _vm.Chapters.Count > 0;
    }

    private void ReplayGainItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string mode }) return;
        _vm.CurrentSettings.ReplayGainMode = mode;
        var service = new SettingsService(); var settings = service.Load();
        settings.ReplayGainMode = mode; service.Save(settings);
        ShowActionOsd("\uE9E9", "ReplayGain will apply when opening the next track");
    }

    private void EditTagsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CurrentItem != null) MediaToolsWindows.ShowTagEditor(this, _vm, _vm.CurrentItem.FilePath);
    }
    private void ConverterButton_Click(object sender, RoutedEventArgs e) =>
        new ConverterWindow(this, _vm, _vm.Playlist.Select(p => p.FilePath).ToArray()).Show();
    private void SearchSubtitlesButton_Click(object sender, RoutedEventArgs e) => MediaToolsWindows.ShowSubtitleSearch(this, _vm);
    private void OpenCueButton_Click(object sender, RoutedEventArgs e) => MediaToolsWindows.ShowCueSheet(this, _vm);
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
    private void ResetOrientation_Click(object sender, RoutedEventArgs e) { _videoRotation = 0; _videoMirrored = false; ApplyImageOrientation(); }
    private void ApplyImageOrientation()
    {
        VideoImage.RenderTransformOrigin = new Point(0.5, 0.5);
        var transforms = new TransformGroup(); transforms.Children.Add(new ScaleTransform(_videoMirrored ? -1 : 1, 1));
        transforms.Children.Add(new RotateTransform(_videoRotation)); VideoImage.LayoutTransform = transforms;
    }
    private void SignalPath_Click(object sender, RoutedEventArgs e)
    {
        string dsp = _vm.IsEqualizerEnabled ? $"Equalizer (10 bands), preamp {_vm.EffectiveEqualizerPreamp:0.0} dB" : "Equalizer disabled";
        MediaToolsWindows.ShowText(this, "Signal path", _vm.GetMediaInfo() +
            $"\n\nDecoder: LibVLC {_vm.LibVLC?.Version}\n{dsp}\nReplayGain: {_vm.CurrentSettings.ReplayGainMode}" +
            $"\nOutput: {_vm.CurrentSettings.AudioOutputDevice}\nVolume: {_vm.Volume:0}% · Speed: {_vm.PlaybackRate:0.##}×" +
            "\n\nWindows shared output may resample the signal. This path does not guarantee bit-perfect playback or native DSD.");
    }
}
