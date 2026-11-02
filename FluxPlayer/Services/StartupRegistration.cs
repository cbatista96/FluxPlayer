using Microsoft.Win32;
using System.IO;

namespace FluxPlayer.Services;

/// <summary>Starts FluxPlayer when the user signs in to Windows, using the current user's Run key only (no elevation, no service).</summary>
public static class StartupRegistration
{
    public const string ValueName = "FluxPlayer";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(RegistryKey root, string sandbox = "")
    {
        using var key = root.OpenSubKey(sandbox + RunKey);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public static void Set(RegistryKey root, bool enabled, string executable, string sandbox = "")
    {
        if (enabled)
        {
            executable = Path.GetFullPath(executable);
            if (!File.Exists(executable) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The player executable was not found.");
            using var key = root.CreateSubKey(sandbox + RunKey);
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else
        {
            using var key = root.OpenSubKey(sandbox + RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }
}
