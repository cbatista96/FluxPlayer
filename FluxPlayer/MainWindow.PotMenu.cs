using FluxPlayer.Controls;
using FluxPlayer.Models;
using FluxPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer;

/// <summary>
/// Player context menu in the PotPlayer layout. Every root menu ends in a "Configure …" entry that opens the
/// matching Settings page, where all the options of that area live; checkable entries are bound to AppSettings
/// through <c>Tag="set:Property[=Value]"</c> so a menu entry and its Settings control always agree.
/// </summary>
public partial class MainWindow
{
    // Routes tool entries (declared with a category and section in MediaToolsMenu) into the Filters menu.
    private static readonly (SettingsCategory Category, string Section)[] FilterSections =
    [
        (SettingsCategory.Audio, "Equalization and tone"), (SettingsCategory.Audio, "Level and dynamics"),
        (SettingsCategory.Audio, "Spatial processing"), (SettingsCategory.Audio, "Time effects"),
        (SettingsCategory.Audio, "Restoration and transitions"), (SettingsCategory.Audio, "Offline processing"),
        (SettingsCategory.Audio, "Offline effect recipes"), (SettingsCategory.Video, "Finishing and color"),
        (SettingsCategory.Video, "Offline processing"), (SettingsCategory.Video, "Offline filter recipes"),
        (SettingsCategory.General, "Extensions")
    ];

    private readonly List<MenuItem> _filterEntries = new();

    private MenuItem? ConfigItemFor(MenuItem parent) =>
        ReferenceEquals(parent, CtxPlaybackRoot) ? CtxPlaybackConfigItem :
        ReferenceEquals(parent, CtxSubtitlesRoot) ? CtxSubtitlesConfigItem :
        ReferenceEquals(parent, CtxVideoRoot) ? CtxVideoConfigItem :
        ReferenceEquals(parent, CtxAudioRoot) ? CtxAudioConfigItem :
        ReferenceEquals(parent, CtxLibraryActions) ? CtxLibraryConfigItem : null;

    private void AddQuickAction(MenuItem parent, MenuItem clone)
    {
        int at = ConfigItemFor(parent) is { } config ? parent.Items.IndexOf(config) : -1;
        if (at < 0) parent.Items.Add(clone); else parent.Items.Insert(at, clone);
    }

    private void RefreshFilterMenu()
    {
        foreach (var entry in _filterEntries) CtxFiltersRoot.Items.Remove(entry);
        _filterEntries.Clear();
        int at = CtxFiltersRoot.Items.IndexOf(CtxFiltersConfigSep);
        var entries = SettingsAction.Collect(MediaToolsMenu);
        foreach (var route in FilterSections)
        {
            var section = entries.Where(entry => entry.Category == route.Category && entry.Section == route.Section).ToList();
            if (section.Count == 0) continue;
            var group = new MenuItem { Header = $"{Localizer.T(route.Category.ToString())}: {Localizer.T(route.Section)}" };
            foreach (var entry in section)
                group.Items.Add(SettingsAction.Clone(entry.Item, item => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item))));
            CtxFiltersRoot.Items.Insert(at++, group);
            _filterEntries.Add(group);
        }
    }

    /// <summary>Opens Settings on the page for <paramref name="category"/>, optionally scrolled to one section.</summary>
    private void ShowSettings(SettingsCategory? category, string? section = null)
    {
        OpenSettingsWindow();
        if (category is { } page) _settingsWindow?.ShowPage(page, section);
    }

    private void CtxConfigure_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        string[] parts = tag.Split('|', 2);
        if (!Enum.TryParse(parts[0], out SettingsCategory category) || category == SettingsCategory.Unassigned) return;
        ShowSettings(category, parts.Length > 1 ? parts[1] : null);
    }

    private void ApplyQuickSetting(Action<AppSettings> change)
    {
        var service = App.Services.GetRequiredService<SettingsService>();
        _vm.SaveSettings();
        var saved = service.Load();
        change(saved);
        if (!service.Save(saved)) { ShowActionOsd("", "Unable to save: " + service.LastError); return; }
        OnSettingsSaved(this, EventArgs.Empty);
    }

    private void CtxSetting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } item || !tag.StartsWith("set:", StringComparison.Ordinal)) return;
        string spec = tag[4..];
        int split = spec.IndexOf('=');
        string name = split < 0 ? spec : spec[..split];
        string? value = split < 0 ? null : spec[(split + 1)..];
        if (typeof(AppSettings).GetProperty(name) is not { CanWrite: true } property) return;
        bool on = item.IsChecked;
        ApplyQuickSetting(settings =>
        {
            if (property.PropertyType == typeof(bool)) property.SetValue(settings, on);
            else if (property.PropertyType == typeof(string) && value != null) property.SetValue(settings, value);
        });
    }

    private void RefreshSettingChecks(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string { } tag && tag.StartsWith("set:", StringComparison.Ordinal))
            {
                string spec = tag[4..];
                int split = spec.IndexOf('=');
                string name = split < 0 ? spec : spec[..split];
                if (typeof(AppSettings).GetProperty(name)?.GetValue(_runtimeSettings) is { } current)
                    item.IsChecked = split < 0 ? current is true : string.Equals(current as string, spec[(split + 1)..], StringComparison.OrdinalIgnoreCase);
            }
            if (item.HasItems) RefreshSettingChecks(item);
        }
    }

    private void RefreshChoiceChecks()
    {
        foreach (var item in CtxThemesMenu.Items.OfType<MenuItem>().Where(item => item.Tag is string))
            item.IsChecked = string.Equals(item.Tag as string, _vm.CurrentTheme.ToString(), StringComparison.Ordinal);
        foreach (var item in CtxAspectMenu.Items.OfType<MenuItem>().Where(item => item.IsCheckable))
            item.IsChecked = string.Equals(item.Tag as string, _vm.AspectRatio, StringComparison.OrdinalIgnoreCase);
        CtxPlaylistItem.IsChecked = _vm.IsPlaylistVisible;
        CtxLanguageMenu.Items.Clear();
        foreach (var language in Localizer.Available)
        {
            var entry = new MenuItem { Header = language.NativeName, IsCheckable = true, IsChecked = language.Id == Localizer.Current, Tag = language.Id };
            entry.Click += CtxLanguage_Click;
            CtxLanguageMenu.Items.Add(entry);
        }
        CtxLanguageMenu.Items.Add(new Separator());
        var more = new MenuItem { Header = "Configure language...", Tag = "General" };
        more.Click += CtxConfigure_Click;
        CtxLanguageMenu.Items.Add(more);
    }

    private void CtxLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string id }) ApplyQuickSetting(settings => settings.Language = Localizer.Normalize(id));
    }

    /// <summary>Keeps every live menu in the selected language.</summary>
    private void ApplyInterfaceLanguage()
    {
        Localizer.Apply(this);
        Localizer.Apply(MainContextMenu);
        Localizer.Apply(MediaToolsMenu);
        Localizer.Apply(ThemeMenu);
        Localizer.Apply(PlayerModeMenu);
        _settingsWindow?.ApplyLanguage();
    }

    private void CtxOpenClipboard_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = Clipboard.ContainsFileDropList() ? string.Join('\n', Clipboard.GetFileDropList().Cast<string>()) : Clipboard.GetText(); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowActionOsd("", "The clipboard is busy."); return; }
        var entries = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim('"')).Where(line => MediaFormats.IsNetwork(line) || File.Exists(line) || Directory.Exists(line)).Take(500).ToArray();
        if (entries.Length == 0) { ShowActionOsd("", "The clipboard has no media file or address."); return; }
        _vm.AddFilesToPlaylist(entries, playFirst: true);
    }

    private void CtxReopenLast_Click(object sender, RoutedEventArgs e)
    {
        string? last = _vm.RecentFilesList.FirstOrDefault();
        if (last == null) { ShowActionOsd("", "There is no recent file to reopen."); return; }
        _vm.OpenRecentFileCommand.Execute(last);
    }

    private void CtxSeekStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out int seconds)) SeekWithOsd(seconds);
    }

    private void CtxSeekPoint_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasMedia || sender is not MenuItem { Tag: string point }) return;
        long total = _vm.TotalDurationMs;
        long target = point switch { "start" => 0, "middle" => total / 2, _ => Math.Max(0, total - 30_000) };
        if (point != "start" && total <= 0) return;
        _vm.SeekToMs(target);
        ShowActionOsd("", FormatTimeHMS(target));
    }

    private void CtxGoToTime_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasMedia) { ShowActionOsd("", "Open media first."); return; }
        var dialog = new Windows.GoToTimeWindow(_vm.CurrentTime, _vm.TotalTime) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.TargetMs is not { } target) return;
        _vm.SeekToMs(target);
        ShowActionOsd("", FormatTimeHMS(Math.Clamp(target, 0, Math.Max(0, _vm.TotalDurationMs))));
    }

    private void CtxSpeedReset_Click(object sender, RoutedEventArgs e) => StepSpeedWithOsd(0);
    private void CtxSpeedDown_Click(object sender, RoutedEventArgs e) => StepSpeedWithOsd(-1);
    private void CtxSpeedUp_Click(object sender, RoutedEventArgs e) => StepSpeedWithOsd(+1);

    private void CtxCopyInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string kind }) CopyInfo(kind);
    }

    private void CtxWindowDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen || sender is not MenuItem { Tag: string tag }) return;
        if (!double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double share)) return;
        var work = GetCurrentMonitorWorkArea();
        WindowState = WindowState.Normal;
        ApplyAnchoredSize(Math.Clamp(work.Width * share, MinWidth, work.Width), Math.Clamp(work.Height * share, MinHeight, work.Height), work);
    }
}
