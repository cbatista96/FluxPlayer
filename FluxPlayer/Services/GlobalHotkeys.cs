using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace FluxPlayer.Services;

public sealed class GlobalHotkeys : IDisposable
{
    public static readonly string[] Actions = ["PlayPause", "Stop", "PreviousTrack", "NextTrack", "VolumeUp", "VolumeDown"];
    private readonly HwndSource _source;
    private readonly Action<string> _invoke;
    private Dictionary<int, string> _registered = new();
    private Dictionary<string, string> _settings = new();
    private int _generation;
    public GlobalHotkeys(HwndSource source, Action<string> invoke) { _source = source; _invoke = invoke; source.AddHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    public static (uint Modifiers, uint Key) Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 80 || new KeyGestureConverter().ConvertFromInvariantString(text) is not KeyGesture gesture || gesture.Key is Key.None or Key.F12 || (gesture.Modifiers & ModifierKeys.Windows) != 0 || (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0) throw new ArgumentException("Global shortcuts require Ctrl or Alt, may include Shift, and cannot use Windows or F12.");
        uint modifiers = 0x4000; if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1; if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2; if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key));
    }
    public void Configure(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count > 6 || values.Keys.Any(key => !Actions.Contains(key))) throw new ArgumentException("Choose known transport actions.");
        var parsed = values.ToDictionary(pair => pair.Key, pair => Parse(pair.Value)); if (parsed.Values.Distinct().Count() != parsed.Count) throw new ArgumentException("Each global shortcut must be unique.");
        if (_settings.Count == values.Count && _settings.All(pair => values.TryGetValue(pair.Key, out string? value) && value == pair.Value)) return;
        var old = _settings.ToDictionary(pair => pair.Key, pair => pair.Value); Clear();
        try { Register(parsed); _settings = values.ToDictionary(pair => pair.Key, pair => pair.Value); }
        catch { Clear(); try { Register(old.ToDictionary(pair => pair.Key, pair => Parse(pair.Value))); _settings = old; } catch { Clear(); _settings.Clear(); } throw; }
    }
    private void Register(Dictionary<string, (uint Modifiers, uint Key)> parsed)
    {
        foreach (var pair in parsed) { int id = 0x2000 + (++_generation % 0x1FFF); if (!RegisterHotKey(_source.Handle, id, pair.Value.Modifiers, pair.Value.Key)) throw new InvalidOperationException("Global shortcut is already in use or unavailable for " + pair.Key + ". Choose another combination."); _registered[id] = pair.Key; }
    }
    private IntPtr Hook(IntPtr handle, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    { if (message == 0x0312 && _registered.TryGetValue(wparam.ToInt32(), out var action)) { handled = true; _invoke(action); } return IntPtr.Zero; }
    private void Clear() { foreach (int id in _registered.Keys) UnregisterHotKey(_source.Handle, id); _registered.Clear(); }
    public void Dispose() { Clear(); _source.RemoveHook(Hook); }
}
public static class MouseGestures
{
    public static string? Resolve(double x, double y) => Math.Max(Math.Abs(x), Math.Abs(y)) < 48 ? null : Math.Abs(x) >= Math.Abs(y) ? x < 0 ? "PreviousTrack" : "NextTrack" : y < 0 ? "VolumeUp" : "VolumeDown";
}
