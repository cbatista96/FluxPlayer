using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FluxPlayer.Windows;

public sealed class NativeVideoSurface(MediaService engine) : HwndHost
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint ex, string name, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        IntPtr handle = CreateWindowExW(0, "STATIC", "FluxPlayer native video", 0x56000000, 0, 0, 320, 180, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error()); engine.MediaPlayer.Hwnd = handle; return new(this, handle);
    }
    protected override void DestroyWindowCore(HandleRef window)
    { if (!engine.IsDisposed) { engine.Stop(); engine.MediaPlayer.Hwnd = IntPtr.Zero; } DestroyWindow(window.Handle); }
}

public sealed class NativeGpuVideoWindow : Wpf.Ui.Controls.FluentWindow
{
    public MediaService Engine { get; }
    private readonly DispatcherTimer _timer;
    public NativeGpuVideoWindow(Window owner, MainViewModel vm)
    {
        Owner = owner; Title = "Native GPU video and HDR display state · FluxPlayer"; Width = 1100; Height = 760; MinWidth = 760; MinHeight = 520;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/FluxPlayer;component/Themes/WorkbenchStyles.xaml") });
        Engine = new MediaService(hardwareDecoding: true, videoOutputModule: "direct3d11", enableDiagnosticLogging: false, subtitleStyle: vm.CurrentSubtitleStyle);
        bool ownAction = false;
        void MainTransportChanged() { if (!ownAction) Engine.Stop(); }
        void StopMain() { ownAction = true; try { vm.StopCommand.Execute(null); } finally { ownAction = false; } }
        vm.AuxiliaryAudioStopRequested += MainTransportChanged;
        Engine.PlaybackSettings = new AppSettings { Volume = vm.Volume, AutoLoadSubtitles = vm.CurrentSettings.AutoLoadSubtitles, PreferredSubtitleLanguage = vm.CurrentSettings.PreferredSubtitleLanguage };
        var root = new Grid { Margin = new Thickness(16) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var source = new TextBox { Text = vm.CurrentItem?.FilePath ?? "", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8), MaxLength = 32767 };
        root.Children.Add(source); var surface = new NativeVideoSurface(Engine); Grid.SetRow(surface, 1); root.Children.Add(surface);
        var footer = new StackPanel(); Grid.SetRow(footer, 2); root.Children.Add(footer); var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; footer.Children.Add(actions);
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Text = "Separate native Direct3D11 session. Video bypasses the 8-bit WPF memory presenter; native subtitles remain in the GPU output. HDR transfer depends on source metadata, VLC/driver and display state. This is not a calibrated HDR or ICC acceptance result. Main playback stops when this session starts. Closing stops this session." }; footer.Children.Add(state);
        void Button(string text, Action action) { var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 6, 10, 6) }; button.Click += (_, _) => { try { action(); } catch (Exception ex) when (ex is not OutOfMemoryException) { state.Text = ex.Message; } }; actions.Children.Add(button); }
        Button("Choose local file…", () => { var picker = new Microsoft.Win32.OpenFileDialog { Filter = MediaFormats.FileDialogFilter }; if (picker.ShowDialog(this) == true) source.Text = picker.FileName; });
        Button("Play source", () => { string path = source.Text.Trim(); if (!MediaFormats.IsNetwork(path)) path = FfmpegJob.Source(path); StopMain(); Engine.PlayFile(path); });
        Button("Pause / resume", () => { if (!Engine.IsPlaying) StopMain(); Engine.TogglePlayPause(); }); Button("Stop", Engine.Stop);
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(vm.Volume, 0, 100), Width = 130, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Native session volume" };
        volume.ValueChanged += (_, _) => { Engine.PlaybackSettings.Volume = volume.Value; Engine.SetVolume((int)volume.Value); }; actions.Children.Add(volume);
        var subtitles = new ComboBox { DisplayMemberPath = "Name", MinWidth = 150, MaxWidth = 240, ToolTip = "Native subtitle track" }; bool syncingTracks = false; string trackIds = "";
        subtitles.SelectionChanged += (_, _) => { if (!syncingTracks && subtitles.SelectedItem is TrackInfo track) Engine.SetSubtitleTrack(track.Id); }; actions.Children.Add(subtitles);
        Button("Inspect display color state…", () =>
        {
            var displays = DisplayColorInspector.Read(); state.Text = string.Join("\n", displays.Select(d => $"{d.Name}: {d.ColorMode}, {d.BitsPerChannel} bits/channel, {d.ColorEncoding}. {d.Detail}"));
            var save = new Microsoft.Win32.SaveFileDialog { Filter = "JSON report|*.json", FileName = "FluxPlayer-display-color.json" };
            if (save.ShowDialog(this) == true) System.IO.File.WriteAllText(save.FileName, JsonSerializer.Serialize(displays, new JsonSerializerOptions { WriteIndented = true }));
        });
        var seek = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) }; footer.Children.Insert(0, seek); bool scrubbing = false;
        seek.PreviewMouseLeftButtonDown += (_, _) => scrubbing = true; seek.PreviewMouseLeftButtonUp += (_, _) => { scrubbing = false; Engine.SeekToPercent((float)(seek.Value / 100)); };
        seek.PreviewKeyUp += (_, args) => { if (args.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown) Engine.SeekToPercent((float)(seek.Value / 100)); };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; _timer.Tick += (_, _) =>
        {
            if (!scrubbing && !seek.IsKeyboardFocusWithin && Engine.GetDuration() > 0) seek.Value = Math.Clamp(Engine.GetTime() * 100d / Engine.GetDuration(), 0, 100);
            var tracks = Engine.GetSubtitleTracks().Take(128).ToArray(); string signature = string.Join(';', tracks.Select(t => t.Id + ":" + t.Name)); syncingTracks = true;
            try { if (signature != trackIds) { trackIds = signature; subtitles.ItemsSource = tracks; } subtitles.SelectedItem = (subtitles.ItemsSource as TrackInfo[])?.FirstOrDefault(t => t.Id == Engine.MediaPlayer.Spu); }
            finally { syncingTracks = false; }
        }; _timer.Start();
        Content = root; SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Closed += (_, _) => { _timer.Stop(); vm.AuxiliaryAudioStopRequested -= MainTransportChanged; Engine.Stop(); Engine.Dispose(); surface.Dispose(); };
    }
}
