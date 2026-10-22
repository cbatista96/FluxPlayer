using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Smtc = global::Windows.Media.SystemMediaTransportControls;
using SmtcButton = global::Windows.Media.SystemMediaTransportControlsButton;
using SmtcButtonArgs = global::Windows.Media.SystemMediaTransportControlsButtonPressedEventArgs;
using SmtcInterop = global::Windows.Media.SystemMediaTransportControlsInterop;
using SmtcStatus = global::Windows.Media.MediaPlaybackStatus;
using SmtcTimeline = global::Windows.Media.SystemMediaTransportControlsTimelineProperties;
using MediaPlaybackType = global::Windows.Media.MediaPlaybackType;
using StreamRef = global::Windows.Storage.Streams.RandomAccessStreamReference;

namespace FluxPlayer.Services;

public sealed class SystemMediaControls : IDisposable
{
    public enum Command { Play, Pause, Stop, Next, Previous }

    private Smtc? _smtc;
    private readonly Action<Command> _onCommand;
    private readonly Action<long>? _onSeek;
    private string _lastTitle = string.Empty;
    private long _lastTimelineUpdateTicks;

    private SystemMediaControls(Action<Command> onCommand, Action<long>? onSeek)
    {
        _onCommand = onCommand;
        _onSeek = onSeek;
    }

    public bool IsActive => _smtc != null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static SystemMediaControls? TryCreate(IntPtr hwnd, Action<Command> onCommand, Action<long>? onSeek)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return null;
        try
        {
            var controls = new SystemMediaControls(onCommand, onSeek);
            controls.Initialize(hwnd);
            return controls;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMTC] unavailable: {ex.Message}");
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Initialize(IntPtr hwnd)
    {
        var smtc = SmtcInterop.GetForWindow(hwnd);
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.IsStopEnabled = true;
        smtc.IsNextEnabled = true;
        smtc.IsPreviousEnabled = true;
        smtc.PlaybackStatus = SmtcStatus.Closed;
        smtc.ButtonPressed += OnButtonPressed;
        smtc.PlaybackPositionChangeRequested += (_, args) =>
            _onSeek?.Invoke((long)args.RequestedPlaybackPosition.TotalMilliseconds);
        _smtc = smtc;
    }

    private void OnButtonPressed(Smtc sender, SmtcButtonArgs args)
    {
        Command? cmd = args.Button switch
        {
            SmtcButton.Play => Command.Play,
            SmtcButton.Pause => Command.Pause,
            SmtcButton.Stop => Command.Stop,
            SmtcButton.Next => Command.Next,
            SmtcButton.Previous => Command.Previous,
            _ => null
        };
        if (cmd != null) _onCommand(cmd.Value);
    }

    public void UpdateStatus(bool hasMedia, bool isPlaying)
    {
        if (_smtc == null) return;
        try
        {
            _smtc.PlaybackStatus = !hasMedia ? SmtcStatus.Closed
                : isPlaying ? SmtcStatus.Playing : SmtcStatus.Paused;
        }
        catch { }
    }

    public void UpdateMetadata(string title, BitmapSource? thumbnail = null)
    {
        if (_smtc == null) return;
        try
        {
            var du = _smtc.DisplayUpdater;
            du.Type = MediaPlaybackType.Video;
            du.VideoProperties.Title = title;
            du.VideoProperties.Subtitle = "FluxPlayer";
            if (thumbnail != null)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                du.Thumbnail = StreamRef.CreateFromStream(ms.AsRandomAccessStream());
            }
            else if (!string.Equals(title, _lastTitle, StringComparison.Ordinal))
            {
                du.Thumbnail = null;
            }
            du.Update();
            _lastTitle = title;
        }
        catch { }
    }

    public void UpdateTimeline(long positionMs, long lengthMs, bool force = false)
    {
        if (_smtc == null || lengthMs <= 0) return;
        long now = Environment.TickCount64;
        if (!force && now - _lastTimelineUpdateTicks < 1000) return;
        _lastTimelineUpdateTicks = now;
        try
        {
            var tl = new SmtcTimeline
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = TimeSpan.FromMilliseconds(Math.Clamp(positionMs, 0, lengthMs)),
                MaxSeekTime = TimeSpan.FromMilliseconds(lengthMs),
                EndTime = TimeSpan.FromMilliseconds(lengthMs)
            };
            _smtc.UpdateTimelineProperties(tl);
        }
        catch { }
    }

    public void Clear()
    {
        if (_smtc == null) return;
        try
        {
            _smtc.PlaybackStatus = SmtcStatus.Closed;
            _smtc.DisplayUpdater.ClearAll();
            _smtc.DisplayUpdater.Update();
            _lastTitle = string.Empty;
        }
        catch { }
    }

    public void Dispose()
    {
        if (_smtc == null) return;
        try
        {
            _smtc.ButtonPressed -= OnButtonPressed;
            _smtc.DisplayUpdater.ClearAll();
            _smtc.IsEnabled = false;
        }
        catch { }
        _smtc = null;
    }
}
