using System.IO;

namespace FluxPlayer.Models;

/// <summary>Startup-only data location selection. Profile switches never become media paths.</summary>
public sealed record AppProfile(string DataDirectory, string CacheDirectory, string[] Sources)
{
    public static AppProfile Current { get; internal set; } = Parse(Array.Empty<string>(), AppContext.BaseDirectory);

    public static AppProfile Parse(IReadOnlyList<string> args, string applicationDirectory,
        string? roamingDirectory = null, string? localDirectory = null, bool portableMarker = false)
    {
        string normalData = Path.Combine(roamingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluxPlayer");
        string normalCache = Path.Combine(localDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxPlayer");
        string? selected = null;
        var sources = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            string value = args[i];
            if (value == "--") { sources.AddRange(args.Skip(i + 1)); break; }
            if (value is not ("--portable" or "--profile" or "--data-directory")) { sources.Add(value); continue; }
            if (selected != null) throw new ArgumentException("Choose only one of --portable, --profile or --data-directory.");
            if (value == "--portable") selected = Path.Combine(Path.GetFullPath(applicationDirectory), "Data");
            else
            {
                if (++i >= args.Count || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{value} requires a value.");
                string argument = args[i];
                if (value == "--profile")
                {
                    if (argument.Length > 64 || argument is "." or ".."
                        || argument.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                        || IsReservedName(argument))
                        throw new ArgumentException("Profile names must use 1–64 ASCII letters, digits, hyphens or underscores.");
                    selected = Path.Combine(normalData, "Profiles", argument);
                }
                else selected = Path.GetFullPath(argument);
            }
        }
        if (selected == null && portableMarker) selected = Path.Combine(Path.GetFullPath(applicationDirectory), "Data");
        return new AppProfile(selected ?? normalData, selected == null ? normalCache : Path.Combine(selected, "Cache"), sources.ToArray());
    }

    private static bool IsReservedName(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL"
            || (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal))
                && upper[3] is >= '1' and <= '9');
    }
}
