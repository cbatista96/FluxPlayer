using FluxPlayer.Services;
using FluxPlayer.Windows;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluxPlayer;

public partial class MainWindow
{
    private readonly Dictionary<string, Window> _expansionWindows = new(StringComparer.Ordinal);
    private void ShowExpansion(string key, Func<Window> create)
    {
        if (_expansionWindows.TryGetValue(key, out var window)) { window.Activate(); return; }
        window = create(); _expansionWindows.Add(key, window); window.Closed += (_, _) => _expansionWindows.Remove(key); window.Show();
    }
    private void JellyfinLibrary_Click(object sender, RoutedEventArgs e) => ShowExpansion("jellyfin", () => new PersonalServerWindow(this, _vm, PersonalServerKind.Jellyfin));
    private void EmbyLibrary_Click(object sender, RoutedEventArgs e) => ShowExpansion("emby", () => new PersonalServerWindow(this, _vm, PersonalServerKind.Emby));
    private void PlexLibrary_Click(object sender, RoutedEventArgs e) => ShowExpansion("plex", () => new PersonalServerWindow(this, _vm, PersonalServerKind.Plex));
    private void UpnpMedia_Click(object sender, RoutedEventArgs e) => ShowExpansion("upnp", () => new UpnpWindow(this, _vm));
    private void ListenBrainz_Click(object sender, RoutedEventArgs e) => ShowExpansion("listenbrainz", () => new ListeningConnectionWindow(this, _vm, false));
    private void LastFm_Click(object sender, RoutedEventArgs e) => ShowExpansion("lastfm", () => new ListeningConnectionWindow(this, _vm, true));
    private void SqlIndex_Click(object sender, RoutedEventArgs e) => ShowExpansion("sql-index", () => new SqlIndexWindow(this, _vm));
    private void AudioZones_Click(object sender, RoutedEventArgs e) => ShowExpansion("audio-zones", () => new AudioZonesWindow(this, _vm));
    private void AdvancedMatroska_Click(object sender, RoutedEventArgs e) => ShowExpansion("matroska", () => new MatroskaChapterWindow(this, _vm));
    private void AudioPalette_Click(object sender, RoutedEventArgs e) => ShowExpansion("audio-palette", () => new ProcessingPaletteWindow(this, _vm, false));
    private void VideoPalette_Click(object sender, RoutedEventArgs e) => ShowExpansion("video-palette", () => new ProcessingPaletteWindow(this, _vm, true));
    private void AudioDiagnostics_Click(object sender, RoutedEventArgs e) => ShowExpansion("audio-diagnostics", () => new MediaDiagnosticsWindow(this, _vm, true));
    private void VideoDiagnostics_Click(object sender, RoutedEventArgs e) => ShowExpansion("video-diagnostics", () => new MediaDiagnosticsWindow(this, _vm, false));
    private void SubtitleWorkshop_Click(object sender, RoutedEventArgs e) => ShowExpansion("subtitle-workshop", () => new SubtitleWorkshopWindow(this, _vm));
    private void SubsonicActions_Click(object sender, RoutedEventArgs e) => ShowExpansion("subsonic-actions", () => new ServerActionsWindow(this, _vm, "Subsonic"));
    private void JellyfinActions_Click(object sender, RoutedEventArgs e) => ShowExpansion("jellyfin-actions", () => new ServerActionsWindow(this, _vm, "Jellyfin"));
    private void EmbyActions_Click(object sender, RoutedEventArgs e) => ShowExpansion("emby-actions", () => new ServerActionsWindow(this, _vm, "Emby"));
    private void PlexActions_Click(object sender, RoutedEventArgs e) => ShowExpansion("plex-actions", () => new ServerActionsWindow(this, _vm, "Plex"));
    private void UpnpActions_Click(object sender, RoutedEventArgs e) => ShowExpansion("upnp-actions", () => new ServerActionsWindow(this, _vm, "UPnP"));
    private void MpvSession_Click(object sender, RoutedEventArgs e) => ShowExpansion("mpv-session", () => new MpvSessionWindow(this, _vm));
    private void KodiIntegration_Click(object sender, RoutedEventArgs e) => ShowExpansion("kodi-integration", () => new InteropWindow(this, _vm, true));
    private void MpdIntegration_Click(object sender, RoutedEventArgs e) => ShowExpansion("mpd-integration", () => new InteropWindow(this, _vm, false));
    private void OnlineLyrics_Click(object sender, RoutedEventArgs e) => ShowExpansion("online-lyrics", () => new OnlineLyricsWindow(this, _vm));
    private void SubtitleOcr_Click(object sender, RoutedEventArgs e) => ShowExpansion("subtitle-ocr", () => new SubtitleOcrWindow(this, _vm));
    private void MobileRemote_Click(object sender, RoutedEventArgs e) => ShowExpansion("mobile-remote", () => new MobileRemoteWindow(this, _vm));
    private void DesktopIntegration_Click(object sender, RoutedEventArgs e) => ShowExpansion("desktop", () => new DesktopMediaWindow(this, _vm, EnterExpansionFullscreen, MoveExpansionMonitor, ResetExpansionFullscreen, ExpansionNotification));
    private void ResetExpansionFullscreen() { if (_isFullscreen) ToggleFullscreen(); }
    private void MoveExpansionMonitor(string name)
    {
        var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? throw new ArgumentException("Choose an available monitor.");
        ResetExpansionFullscreen(); WindowState = WindowState.Normal; var work = screen.WorkingArea;
        if (!ExpansionSetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, work.Left + 16, work.Top + 16, Math.Max(640, Math.Min(1200, work.Width - 32)), Math.Max(400, Math.Min(800, work.Height - 32)), 0x14)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private void EnterExpansionFullscreen(string monitor)
    { if (_vm.IsAudioMode) throw new ArgumentException("Open video or select Video mode before entering fullscreen."); MoveExpansionMonitor(monitor); Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded && !_isFullscreen) ToggleFullscreen(); })); }
    private bool ExpansionNotification(string text)
    { if (!_runtimeSettings.ShowSystemTrayIcon) return false; EnsureTrayIcon(); if (_trayIcon == null) return false; _trayIcon.ShowBalloonTip(3500, "FluxPlayer", text[..Math.Min(text.Length, 250)], System.Windows.Forms.ToolTipIcon.Info); return true; }
    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExpansionSetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
