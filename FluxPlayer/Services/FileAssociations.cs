using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>
/// Registers FluxPlayer for media file types for the current user only (HKCU, no elevation). Windows 10/11 do not let an app
/// take over a default silently, so registration makes FluxPlayer selectable in "Open with" and in Default apps.
/// <paramref name="sandbox"/> prefixes every key path so tests never touch the real registrations.
/// </summary>
public static class FileAssociations
{
    public const string ProgId = "FluxPlayer.Media";
    private const string CapabilitiesPath = @"Software\FluxPlayer\Capabilities";

    private static bool ValidExtension(string extension) => extension.Length is > 1 and < 16 && extension[0] == '.' && extension.Skip(1).All(char.IsLetterOrDigit);

    public static int Register(RegistryKey root, string executable, IEnumerable<string> extensions, string sandbox = "")
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The player executable was not found.");
        string classes = sandbox + @"Software\Classes\";
        var list = extensions.Where(ValidExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using (var progId = root.CreateSubKey(classes + ProgId))
        {
            progId.SetValue("", "FluxPlayer media file");
            using (var icon = progId.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{executable}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command"); command.SetValue("", $"\"{executable}\" \"%1\"");
        }
        using (var app = root.CreateSubKey(classes + @"Applications\FluxPlayer.exe"))
        {
            app.SetValue("FriendlyAppName", "FluxPlayer");
            using var command = app.CreateSubKey(@"shell\open\command"); command.SetValue("", $"\"{executable}\" \"%1\"");
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (string extension in list) types.SetValue(extension, "");
        }
        using (var capabilities = root.CreateSubKey(sandbox + CapabilitiesPath))
        {
            capabilities.SetValue("ApplicationName", "FluxPlayer");
            capabilities.SetValue("ApplicationDescription", "Media player for audio and video");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (string extension in list) associations.SetValue(extension, ProgId);
        }
        using (var registered = root.CreateSubKey(sandbox + @"Software\RegisteredApplications")) registered.SetValue("FluxPlayer", CapabilitiesPath);
        foreach (string extension in list)
        {
            using var key = root.CreateSubKey(classes + extension + @"\OpenWithProgids");
            key.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        return list.Length;
    }

    public static void Unregister(RegistryKey root, IEnumerable<string> extensions, string sandbox = "")
    {
        string classes = sandbox + @"Software\Classes\";
        foreach (string extension in extensions.Where(ValidExtension))
        {
            using var key = root.OpenSubKey(classes + extension + @"\OpenWithProgids", true);
            key?.DeleteValue(ProgId, false);
        }
        root.DeleteSubKeyTree(classes + ProgId, false);
        root.DeleteSubKeyTree(classes + @"Applications\FluxPlayer.exe", false);
        root.DeleteSubKeyTree(sandbox + CapabilitiesPath, false);
        using var registered = root.OpenSubKey(sandbox + @"Software\RegisteredApplications", true);
        registered?.DeleteValue("FluxPlayer", false);
    }

    public static bool IsRegistered(RegistryKey root, string sandbox = "")
    {
        using var key = root.OpenSubKey(sandbox + @"Software\Classes\" + ProgId + @"\shell\open\command");
        return key?.GetValue("") is string command && command.Length > 0;
    }

    public static void OpenDefaultApps() => Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
}
