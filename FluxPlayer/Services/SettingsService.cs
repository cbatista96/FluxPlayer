using FluxPlayer.Models;
using System.IO;
using System.Text.Json;

namespace FluxPlayer.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluxPlayer", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                Normalize(settings);
                return settings;
            }
        }
        catch
        {

            try
            {
                var backupPath = $"{SettingsPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                if (File.Exists(SettingsPath))
                    File.Move(SettingsPath, backupPath);
            }
            catch { }
        }
        var defaults = new AppSettings();
        Normalize(defaults);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Normalize(settings);
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temporaryPath = SettingsPath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(SettingsPath))
                    File.Replace(temporaryPath, SettingsPath, null);
                else
                    File.Move(temporaryPath, SettingsPath);
            }
            catch
            {

                if (File.Exists(temporaryPath))
                    File.Move(temporaryPath, SettingsPath, true);
                else
                    throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void Normalize(AppSettings settings)
    {
        // The public source currently ships a single English interface.
        settings.Language = "English";
        settings.EqualizerPreset = EqualizerPresets.Normalize(settings.EqualizerPreset);
        var featureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (settings.FeatureFlags != null)
        {
            foreach (var pair in settings.FeatureFlags)
                featureFlags[pair.Key] = pair.Value;
        }

        foreach (var feature in FeatureCatalog.All)
        {
            if (!featureFlags.ContainsKey(feature.Key))
                featureFlags[feature.Key] = feature.DefaultEnabled;
        }

        if (settings.FeatureCatalogVersion < FeatureCatalog.CatalogVersion)
        {
            foreach (var key in FeatureCatalog.NewlyImplementedKeys)
            {
                var def = FeatureCatalog.All.FirstOrDefault(f => f.Key == key);
                if (def != null) featureFlags[key] = def.DefaultEnabled;
            }
            settings.FeatureCatalogVersion = FeatureCatalog.CatalogVersion;
        }
        settings.FeatureFlags = featureFlags;

        if (settings.SettingsSchemaVersion < 1)
        {
            if (string.Equals(settings.DefaultPlaybackOnEnd?.Trim(), "Stop", StringComparison.OrdinalIgnoreCase))
                settings.DefaultPlaybackOnEnd = "Next";
            settings.SettingsSchemaVersion = 1;
        }
        if (settings.DefaultPlaybackOnEnd is not ("Stop" or "Repeat" or "Next" or "Close"))
            settings.DefaultPlaybackOnEnd = "Next";

        settings.RecentFiles ??= new List<string>();
        settings.MaxRecentFiles = Math.Clamp(settings.MaxRecentFiles, 1, 100);
        settings.RecentFiles = settings.RecentFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(settings.MaxRecentFiles)
            .ToList();

        settings.Keybindings = KeybindingRules.Sanitize(settings.Keybindings, new AppSettings().Keybindings);

        var normalizedBands = new float[10];
        if (settings.EqualizerBands != null)
            Array.Copy(settings.EqualizerBands, normalizedBands,
                Math.Min(settings.EqualizerBands.Length, normalizedBands.Length));
        settings.EqualizerBands = normalizedBands;

        settings.Volume = Math.Clamp(settings.Volume, 0, 200);
        settings.PlaybackRate = Math.Clamp(settings.PlaybackRate, 0.25f, 4f);
        settings.VolumeStep = Math.Clamp(settings.VolumeStep, 1, 50);
        settings.SeekStepSeconds = Math.Clamp(settings.SeekStepSeconds, 1, 120);
        settings.SeekLargeStepSeconds = Math.Clamp(settings.SeekLargeStepSeconds, 5, 600);

        settings.TimelineThumbnailWidth = Math.Clamp(settings.TimelineThumbnailWidth, 80, 420);
        settings.SnapshotIntervalSeconds = Math.Clamp(settings.SnapshotIntervalSeconds, 1, 3600);
        settings.CrossfeedLevel = Math.Clamp(settings.CrossfeedLevel, 0, 100);
        settings.RemoteControlPort = Math.Clamp(settings.RemoteControlPort, 1024, 65535);
        settings.IntroSkipSeconds = Math.Clamp(settings.IntroSkipSeconds, 0, 1800);
        settings.AmbientLightIntensity = Math.Clamp(settings.AmbientLightIntensity, 10, 100);
        settings.VolumeBoostMaxPercent = Math.Clamp(settings.VolumeBoostMaxPercent, 100, 200);
        settings.Bookmarks ??= new Dictionary<string, List<long>>();
        settings.SavedPlaylist ??= new List<string>();
        settings.SavedPlaylist = settings.SavedPlaylist.Where(p => !string.IsNullOrWhiteSpace(p)).Take(PlaylistService.MaxEntries).ToList();
        settings.AudioWindowWidth = double.IsFinite(settings.AudioWindowWidth) ? Math.Clamp(settings.AudioWindowWidth, 760, 3840) : 820;
        settings.AudioWindowHeight = double.IsFinite(settings.AudioWindowHeight) ? Math.Clamp(settings.AudioWindowHeight, 500, 2160) : 660;
        if (!Enum.IsDefined(settings.PlayerModePreference)) settings.PlayerModePreference = PlayerModePreference.Automatic;
        if (settings.ReplayGainMode is not ("none" or "track" or "album")) settings.ReplayGainMode = "none";
        settings.ReplayGainPreamp = float.IsFinite(settings.ReplayGainPreamp) ? Math.Clamp(settings.ReplayGainPreamp, -20, 20) : 0;
        settings.OutroSkipSeconds = Math.Clamp(settings.OutroSkipSeconds, 0, 1800);
    }
}
