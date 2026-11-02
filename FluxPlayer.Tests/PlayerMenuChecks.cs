using FluxPlayer.Controls;
using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.Windows;
using System.IO;
using System.Xml.Linq;

/// <summary>The player menu follows the PotPlayer layout and every area ends in an entry that opens its Settings page.</summary>
internal static class PlayerMenuChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static Task Managed(string root)
    {
        string repo = Directory.GetParent(root)!.Parent!.FullName;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var main = XDocument.Load(Path.Combine(repo, "FluxPlayer", "MainWindow.xaml"));
        var menu = main.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MainContextMenu");
        string Header(XElement element) => (string?)element.Attribute("Header") ?? "";
        var top = menu.Elements().Where(element => element.Name.LocalName == "MenuItem").Select(Header).ToArray();

        string[] order = ["Open file...", "Open", "Broadcast", "Library and favorites", "Close playback", "Play", "Subtitles", "Video", "Audio", "Filters", "Themes", "Miscellaneous",
            "Frame size", "Aspect ratio", "Window size", "Fullscreen", "Preferences...", "Playlist", "Playback and system information", "About...", "Exit"];
        Check(top.SequenceEqual(order), "Top-level menu keeps the PotPlayer order: " + string.Join(" | ", top));

        // Every area has a "Configure …" entry as its last item, and its target is a real category (and a section that exists when given).
        var settingsPage = XDocument.Load(Path.Combine(repo, "FluxPlayer", "Controls", "SettingsPanel.xaml"));
        var tools = main.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MediaToolsMenu").Elements().Where(element => element.Name.LocalName == "MenuItem").ToArray();
        XNamespace controls = "clr-namespace:FluxPlayer.Controls";
        var sections = tools.Select(item => ((string?)item.Attribute(controls + "SettingsAction.Category") ?? "", (string?)item.Attribute(controls + "SettingsAction.Section") ?? "")).ToHashSet();
        string[] areas = ["Open", "Broadcast", "Library and favorites", "Play", "Subtitles", "Video", "Audio", "Filters", "Themes", "Miscellaneous", "Frame size", "Aspect ratio", "Window size"];
        foreach (string area in areas)
        {
            var root1 = menu.Elements().Single(element => Header(element) == area);
            var last = root1.Elements().Where(element => element.Name.LocalName == "MenuItem").Last();
            Check(Header(last).StartsWith("Configure ", StringComparison.Ordinal), area + " ends in a Configure entry");
            string[] target = ((string?)last.Attribute("Tag") ?? "").Split('|', 2);
            Check(Enum.TryParse(target[0], out SettingsCategory category) && category != SettingsCategory.Unassigned, area + " Configure targets a real category");
            Check(target.Length == 1 || sections.Contains((target[0], target[1])) || target[1] is "Capture" or "Appearance" or "Player presentation" or "Picture orientation" or "Picture"
                or "Chapters and navigation" or "Subtitle appearance" or "Spatial processing", area + " Configure targets a known section: " + string.Join("|", target));
        }

        // Every checkable entry bound to a setting names a real, writable property of the right type.
        foreach (var item in menu.Descendants().Where(element => ((string?)element.Attribute("Tag"))?.StartsWith("set:", StringComparison.Ordinal) == true))
        {
            string spec = ((string)item.Attribute("Tag")!)[4..]; int split = spec.IndexOf('=');
            var property = typeof(AppSettings).GetProperty(split < 0 ? spec : spec[..split]);
            Check(property is { CanWrite: true } && (split < 0 ? property.PropertyType == typeof(bool) : property.PropertyType == typeof(string)), "Menu setting exists: " + spec);
            Check(split < 0 || (string?)item.Attribute("IsCheckable") == "True", "A choice is checkable: " + spec);
        }

        // Spanish is complete for the menu and the language setting round-trips.
        string previous = Localizer.Current;
        try
        {
            Localizer.Use("Spanish");
            var missing = menu.Descendants().Where(element => element.Name.LocalName == "MenuItem").Select(Header)
                .Where(header => header.Length > 1 && char.IsLetter(header[0]) && Localizer.T(header) == header && !IsInvariant(header)).Distinct().ToArray();
            Check(missing.Length == 0, "Every menu entry has a Spanish translation. Missing: " + string.Join(" | ", missing));
            Check(Localizer.T("Seek forward (+10 s)") == "Avanzar (+10 s)" && Localizer.T("Unknown text") == "Unknown text", "Dynamic suffixes are kept and unknown text falls back to English");
            Localizer.Use("English");
            Check(Localizer.T("Play") == "Play" && Localizer.Normalize("spanish") == "Spanish" && Localizer.Normalize("Klingon") == "English", "Language ids normalize and English is the fallback");
        }
        finally { Localizer.Use(previous); }

        var saved = new SettingsService(Path.Combine(root, "language-" + Guid.NewGuid().ToString("N")));
        Check(saved.Save(new AppSettings { Language = "Spanish" }) && saved.Load().Language == "Spanish", "Spanish survives a save and load");
        Check(saved.Save(new AppSettings { Language = "Unknown" }) && saved.Load().Language == "English", "An unknown language is repaired to English");

        foreach ((string text, long expected) in new[] { ("90", 90_000L), ("1:30", 90_000L), ("1:02:03", 3_723_000L), ("0:00:00.5", 500L), ("12.25", 12_250L) })
            Check(GoToTimeWindow.TryParse(text, out long ms) && ms == expected, $"Go to time parses {text}");
        foreach (string bad in new[] { "", "abc", "-5", "1:2:3:4", "1.5:30", "99999999" })
            Check(!GoToTimeWindow.TryParse(bad, out _), $"Go to time rejects '{bad}'");
        return Task.CompletedTask;
    }


    public static async Task Integration(string root)
    {
        // Action catalog: unique keys, none steals a default binding, every menu action is catalogued and handled.
        var keys = PotActionCatalog.All.Select(a => a.Key).ToArray();
        var defaults = new AppSettings().Keybindings;
        Check(keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == keys.Length && keys.All(k => !defaults.ContainsKey(k)), "Action keys are unique and do not change default shortcuts");
        string repo = Directory.GetParent(root)!.Parent!.FullName;
        string handlers = File.ReadAllText(Path.Combine(repo, "FluxPlayer", "MainWindow.PotActions.cs"));
        Check(keys.All(k => handlers.Contains($"case \"{k}\":")), "Every catalogued action has a handler: " + string.Join(", ", keys.Where(k => !handlers.Contains($"case \"{k}\":"))));
        var main = XDocument.Load(Path.Combine(repo, "FluxPlayer", "MainWindow.xaml"));
        var used = main.Descendants().Select(e => (string?)e.Attribute("Tag")).Where(t => t?.StartsWith("act:") == true).Select(t => t![4..]).ToArray();
        Check(used.Length >= 18 && used.All(k => keys.Contains(k)), "Every menu action is catalogued: " + used.Length + " found, missing " + string.Join(",", used.Where(k => !keys.Contains(k))));
        Check(PotActionCatalog.AspectCycle.Distinct().Count() == PotActionCatalog.AspectCycle.Length, "Aspect cycle has no duplicates");

        // File associations: registered only inside a throwaway key, complete, and removed again.
        string sandbox = @"Software\FluxPlayerTests\" + Guid.NewGuid().ToString("N") + @"\";
        var hkcu = Microsoft.Win32.Registry.CurrentUser;
        string sandboxRoot = sandbox[..^1];
        try
        {
            string exe = Path.Combine(root, "assoc-" + Guid.NewGuid().ToString("N") + ".exe"); File.WriteAllText(exe, "x");
            Check(FileAssociations.Register(hkcu, exe, new[] { ".mkv", ".MP3", ".mkv", "bad", ".a/b", ".toolongextension1" }, sandbox) == 2, "Only valid, distinct extensions are registered");
            Check(FileAssociations.IsRegistered(hkcu, sandbox), "Registration is detectable");
            using (var command = hkcu.OpenSubKey(sandbox + @"Software\Classes\" + FileAssociations.ProgId + @"\shell\open\command"))
                Check(((string?)command?.GetValue(""))?.Contains(exe) == true && ((string?)command.GetValue(""))!.EndsWith("\"%1\""), "Open command passes the file");
            using (var open = hkcu.OpenSubKey(sandbox + @"Software\Classes\.mkv\OpenWithProgids")) Check(open?.GetValueNames().Contains(FileAssociations.ProgId) == true, "Extension lists FluxPlayer in Open with");
            FileAssociations.Unregister(hkcu, new[] { ".mkv", ".MP3" }, sandbox);
            Check(!FileAssociations.IsRegistered(hkcu, sandbox), "Unregistering removes the player");
            bool refused = false; try { FileAssociations.Register(hkcu, Path.Combine(root, "missing.exe"), new[] { ".mkv" }, sandbox); } catch (ArgumentException) { refused = true; }
            Check(refused, "A missing executable is refused");
        }
        finally { hkcu.DeleteSubKeyTree(sandboxRoot, false); }

        // Single instance: the second launch forwards its files to the first and does not own the profile.
        string data = Path.Combine(root, "single-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        using var first = SingleInstance.TryOwn(data, Array.Empty<string>());
        Check(first != null, "The first launch owns the profile");
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        first!.Listen(paths => received.TrySetResult(paths));
        await Task.Delay(200);
        string file = Path.Combine(data, "ñ clip.mkv");
        using (var second = SingleInstance.TryOwn(data, new[] { file, "https://example.com/a.mp4" })) Check(second == null, "The second launch hands over and exits");
        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(got.SequenceEqual(new[] { file, "https://example.com/a.mp4" }), "Files, including Unicode names and addresses, reach the first instance");
        using var otherProfile = SingleInstance.TryOwn(Path.Combine(root, "single-" + Guid.NewGuid().ToString("N")), Array.Empty<string>());
        Check(otherProfile != null, "A different profile is independent");
    }

    // Percentages, aspect ratios and speeds read the same in every language.
    private static readonly HashSet<string> SameInSpanish = new(StringComparer.Ordinal) { "Audio", "Auto", "Mono", "Original", "ReplayGain" };
    private static bool IsInvariant(string header) => SameInSpanish.Contains(header) || header.Any(char.IsDigit) && header.Replace(" ", "").All(c => char.IsDigit(c) || ":%x.".Contains(c));
}
