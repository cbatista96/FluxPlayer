using FluxPlayer.Controls;
using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

internal static class SettingsNavigationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static Task Managed(string root)
    {
        string repo = Directory.GetParent(root)!.Parent!.FullName;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml", controls = "clr-namespace:FluxPlayer.Controls";
        var main = XDocument.Load(Path.Combine(repo, "FluxPlayer", "MainWindow.xaml"));
        var registry = main.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MediaToolsMenu");
        var actions = registry.Elements().Where(element => element.Name.LocalName == "MenuItem").ToArray();
        Check(actions.Length >= 25 && actions.All(element => Enum.TryParse<SettingsCategory>((string?)element.Attribute(controls + "SettingsAction.Category"), out var category)
            && category != SettingsCategory.Unassigned && !string.IsNullOrWhiteSpace((string?)element.Attribute(controls + "SettingsAction.Section"))), "Every production action has an explicit category and section");
        Check(actions.Where(element => ((string?)element.Attribute("Header"))?.Contains("subtitle", StringComparison.OrdinalIgnoreCase) == true)
            .All(element => (string?)element.Attribute(controls + "SettingsAction.Category") == "Subtitles"), "Subtitle actions live in Subtitles");
        var settings = XDocument.Load(Path.Combine(repo, "FluxPlayer", "Controls", "SettingsPanel.xaml"));
        var names = settings.Descendants().Select(element => (string?)element.Attribute(x + "Name")).ToHashSet();
        Check(!names.Contains("PageTools") && !settings.Descendants().Any(element => (string?)element.Attribute("Content") == "Tools"), "No catch-all Tools page");
        var categories = settings.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "CategoryList").Elements()
            .Where(element => element.Name.LocalName == "ListBoxItem").Select(element => (string?)element.Attribute("Content")).ToArray();
        Check(categories.SequenceEqual(new[] { "General", "Playback", "Video", "Audio", "Subtitles", "Library", "Files and tags", "Keyboard shortcuts", "Mouse", "Locations", "Accessibility", "Features", "About" }),
            "Sidebar entries are unique and follow the actual settings page order, including About");
        foreach (string host in new[] { "AudioActions", "VideoActions", "SubtitleActions", "PlaybackActions", "LibraryActions", "FileActions" }) Check(names.Contains(host), "Categorized action host: " + host);
        var menu = new ContextMenu(); var source = new MenuItem { Header = "Output" }; menu.Items.Add(source);
        bool refused = false; try { SettingsAction.Collect(menu); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "New unclassified actions cannot silently enter settings");
        SettingsAction.SetCategory(source, SettingsCategory.Audio); SettingsAction.SetSection(source, "Output"); SettingsAction.SetQuickAccess(source, true);
        var child = new MenuItem { Header = new TextBlock { Text = "Device ñ" }, Tag = "device-id", IsCheckable = true, IsChecked = false }; source.Items.Add(child);
        int calls = 0; child.Click += (_, _) => { calls++; Check(child.IsChecked && (string)child.Tag == "device-id", "Forwarded checked state and action identity"); };
        var clone = (MenuItem)SettingsAction.Clone(source, item => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)));
        var clonedChild = (MenuItem)clone.Items[0]; clonedChild.IsChecked = true; clonedChild.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(calls == 1 && source.Items.Contains(child) && !ReferenceEquals(child.Header, clonedChild.Header), "Clones invoke exactly once without reparenting original UI elements");
        Check(SettingsAction.Collect(menu).Single() is { Category: SettingsCategory.Audio, Section: "Output", QuickAccess: true }, "Shared metadata drives settings and curated shortcuts");
        return Task.CompletedTask;
    }
    public static async Task Native(string root, string wav)
    {
        string folder = Path.Combine(root, "quick-audio-" + Guid.NewGuid().ToString("N")); var store = new SettingsService(folder);
        Check(store.Save(new AppSettings { Volume = 0 }), "Isolated quick preference settings");
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
        await using var library = new LibraryService(Path.Combine(folder, "library")); var vm = new MainViewModel(engine, store, library: library);
        try
        {
            vm.AddFilesToPlaylist(new[] { wav }, true); var watch = Stopwatch.StartNew();
            while (!vm.IsPlaying || engine.GetTime() <= 0) { if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Waiting for decoded playback before checking audio continuity."); await Task.Delay(50); }
            vm.Volume = 17; vm.ToggleMuteCommand.Execute(null);
            watch.Restart();
            while (!engine.MediaPlayer.Mute || Math.Abs(engine.GetVolume() - 17) > 1)
            { if (watch.ElapsedMilliseconds > 2000) throw new TimeoutException("Native mute/volume baseline was not acknowledged."); await Task.Delay(25); }
            Check(vm.Volume == 17 && vm.IsMuted && engine.MediaPlayer.Mute && engine.IsPlaying && Math.Abs(engine.GetVolume() - 17) <= 1,
                $"Establish a playing, muted baseline before routing (UI volume={vm.Volume}, UI muted={vm.IsMuted}, native volume={engine.GetVolume()}, native muted={engine.MediaPlayer.Mute}, native state={engine.MediaPlayer.State})");
            Check(vm.SetQuickAudioPreference("routing", "Mono"), "Routing saves and applies");
            watch.Restart(); while ((int)engine.MediaPlayer.Channel != AudioChannelRouting.NativeValue("Mono")) { if (watch.ElapsedMilliseconds > 3000) throw new TimeoutException(); await Task.Delay(60); }
            Check(vm.Volume == 17 && vm.IsMuted && engine.MediaPlayer.Mute && engine.IsPlaying && Math.Abs(engine.GetVolume() - 17) <= 1,
                $"Quick changes preserve volume, mute and transport (UI volume={vm.Volume}, UI muted={vm.IsMuted}, native volume={engine.GetVolume()}, native muted={engine.MediaPlayer.Mute}, native state={engine.MediaPlayer.State})");
            Check(vm.SetQuickAudioPreference("replaygain", "album") && engine.PlaybackSettings.ReplayGainMode == "album" && store.Load().ReplayGainMode == "album", "ReplayGain choice persists for later openings");
            using (var locked = new FileStream(Path.Combine(folder, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!vm.SetQuickAudioPreference("routing", "Right"), "Failed quick-save reported");
            Check(engine.PlaybackSettings.AudioChannels == "Mono" && store.Load().AudioChannels == "Mono", "Failed save leaves native and persisted routing unchanged");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
}
