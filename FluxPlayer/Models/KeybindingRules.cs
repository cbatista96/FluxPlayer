namespace FluxPlayer.Models;

public static class KeybindingRules
{
    public const string UnassignedText = "(unassigned)";

    public static string? GetReservedReason(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return null;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        string key = parts[^1];
        var mods = new HashSet<string>(parts[..^1], StringComparer.OrdinalIgnoreCase);
        bool noMods = mods.Count == 0;

        if (mods.Contains("Win"))
            return "Windows key combinations are reserved by the system.";
        if (key.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            return "Esc is reserved for closing panels and leaving fullscreen.";
        if (key.Equals("F11", StringComparison.OrdinalIgnoreCase) && noMods)
            return "F11 is reserved for fullscreen.";
        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            return "Tab is reserved for keyboard navigation.";
        if (mods.Contains("Alt") && mods.Count == 1 &&
            (key.Equals("F4", StringComparison.OrdinalIgnoreCase) ||
             key.Equals("Space", StringComparison.OrdinalIgnoreCase)))
            return $"Alt+{(key.Equals("F4", StringComparison.OrdinalIgnoreCase) ? "F4" : "Space")} is reserved by Windows.";
        if (noMods && IsDigitKey(key))
            return "Keys 0–9 are reserved for seeking to 0%–90% of the video.";
        if (key.StartsWith("Media", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("Volume", StringComparison.OrdinalIgnoreCase))
            return "Media keys already control playback.";
        if (key is "System" or "None" or "ImeProcessed" or "DeadCharProcessed")
            return "This key cannot be assigned.";
        return null;
    }

    private static bool IsDigitKey(string key)
        => (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) ||
           (key.Length == 7 && key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && char.IsDigit(key[6]));

    public static Dictionary<string, string> Sanitize(
    IReadOnlyDictionary<string, string>? bindings,
    IReadOnlyDictionary<string, string> defaults)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (bindings != null)
        {
            foreach (var pair in bindings)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                string gesture = NormalizeGesture(pair.Value);
                if (gesture.Length == 0 || GetReservedReason(gesture) != null)
                {
                    result[pair.Key] = string.Empty;
                    continue;
                }

                if (owners.TryGetValue(gesture, out var owner))
                {
                    bool ownerIsDefault = defaults.TryGetValue(owner, out var od) &&
                                          string.Equals(od, gesture, StringComparison.OrdinalIgnoreCase);
                    bool thisIsDefault = defaults.TryGetValue(pair.Key, out var td) &&
                                         string.Equals(td, gesture, StringComparison.OrdinalIgnoreCase);
                    if (thisIsDefault && !ownerIsDefault)
                    {
                        result[owner] = string.Empty;
                        owners[gesture] = pair.Key;
                        result[pair.Key] = gesture;
                    }
                    else
                    {
                        result[pair.Key] = string.Empty;
                    }
                    continue;
                }

                owners[gesture] = pair.Key;
                result[pair.Key] = gesture;
            }
        }

        foreach (var pair in defaults)
        {
            if (result.ContainsKey(pair.Key)) continue;
            string gesture = NormalizeGesture(pair.Value);
            if (gesture.Length > 0 && !owners.ContainsKey(gesture) && GetReservedReason(gesture) == null)
            {
                owners[gesture] = pair.Key;
                result[pair.Key] = gesture;
            }
            else
            {
                result[pair.Key] = string.Empty;
            }
        }
        return result;
    }

    private static string NormalizeGesture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        if (value.StartsWith('(')) return string.Empty;
        return value;
    }

    public static string ToDisplay(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;
        return string.Join("+", gesture.Split('+').Select(p => p switch
        {
            "Right" => "Right",
            "Left" => "Left",
            "Up" => "Top",
            "Down" => "Bottom",
            "Space" => "Space",
            "Back" => "Backspace",
            "Return" or "Enter" => "Enter",
            "Delete" => "Del",
            "Insert" => "Ins",
            "PageUp" or "Prior" => "PgUp",
            "PageDown" or "Next" => "PgDn",
            "Home" => "Home",
            "End" => "End",
            "OemPeriod" => ".",
            "OemComma" => ",",
            "OemMinus" => "-",
            "OemPlus" => "+",
            "Subtract" => "Num -",
            "Add" => "Num +",
            "Multiply" => "Num *",
            "Divide" => "Num /",
            "OemOpenBrackets" => "[",
            "OemCloseBrackets" => "]",
            "OemQuestion" => "/",
            "OemSemicolon" or "Oem1" => ";",
            "OemQuotes" => "'",
            "OemPipe" or "Oem5" => "\\",
            "OemTilde" or "Oem3" => "`",
            _ when p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) => p[1].ToString(),
            _ when p.StartsWith("NumPad") => "Num " + p[6..],
            _ => p
        }));
    }
}
