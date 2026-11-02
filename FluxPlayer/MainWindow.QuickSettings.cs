using FluxPlayer.Controls;
using FluxPlayer.Models;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer;

public partial class MainWindow
{
    private readonly List<(MenuItem Parent, MenuItem Item)> _quickActions = new();
    private void RefreshQuickSettings()
    {
        MediaToolsMenu_Opened(this, new RoutedEventArgs());
        foreach (var (parent, item) in _quickActions) parent.Items.Remove(item); _quickActions.Clear();
        foreach (var entry in SettingsAction.Collect(MediaToolsMenu).Where(entry => entry.QuickAccess))
        {
            var parent = entry.Category switch
            {
                SettingsCategory.Audio => CtxAudioRoot,
                SettingsCategory.Video => CtxVideoRoot,
                SettingsCategory.Subtitles => CtxSubtitlesRoot,
                SettingsCategory.Playback => CtxPlaybackRoot,
                _ => CtxLibraryActions
            };
            var clone = (MenuItem)SettingsAction.Clone(entry.Item, item => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)));
            AddQuickAction(parent, clone); _quickActions.Add((parent, clone));
        }
        RefreshFilterMenu();
        CtxPlaybackModeMenu.Items.Clear();
        foreach (MenuItem mode in PlayerModeMenu.Items.OfType<MenuItem>())
        {
            mode.IsCheckable = true; mode.IsChecked = string.Equals(mode.Tag?.ToString(), _vm.ModePreference.ToString(), StringComparison.Ordinal);
            CtxPlaybackModeMenu.Items.Add(SettingsAction.Clone(mode, item => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item))));
        }
        FillTrackChoices(CtxAudioTracks, false); FillTrackChoices(CtxSubtitleTracks, true);
        CtxMuteItem.IsChecked = _vm.IsMuted; CtxShuffleItem.IsChecked = _vm.Shuffle;
        CtxRepeatOffItem.IsChecked = _vm.RepeatMode == RepeatMode.None; CtxRepeatOneItem.IsChecked = _vm.RepeatMode == RepeatMode.One; CtxRepeatAllItem.IsChecked = _vm.RepeatMode == RepeatMode.All;
    }
    private void FillTrackChoices(MenuItem menu, bool subtitles)
    {
        menu.Items.Clear(); var tracks = subtitles ? _vm.SubtitleTracks : _vm.AudioTracks;
        string? source = _vm.CurrentItem?.FilePath;
        foreach (var track in tracks.Take(256))
        {
            var item = new MenuItem
            {
                Header = new TextBlock { Text = track.Name, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis },
                IsCheckable = true,
                IsChecked = track.Id == (subtitles ? _vm.SelectedSubtitleTrack?.Id : _vm.SelectedAudioTrack?.Id)
            };
            item.Click += (_, _) =>
            {
                if (source != _vm.CurrentItem?.FilePath || !tracks.Contains(track)) return;
                if (subtitles) _vm.SelectedSubtitleTrack = track; else _vm.SelectedAudioTrack = track;
            };
            menu.Items.Add(item);
        }
        menu.IsEnabled = _vm.HasMedia && menu.Items.Count > 0;
    }
    private void CtxRepeatOff_Click(object sender, RoutedEventArgs e) => _vm.RepeatMode = RepeatMode.None;
    private void QuickRouting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string mode } && _vm.SetQuickAudioPreference("routing", mode))
        { _runtimeSettings.AudioChannels = mode; ShowActionOsd("\uE9E9", "Channel routing: " + mode); }
    }
}
