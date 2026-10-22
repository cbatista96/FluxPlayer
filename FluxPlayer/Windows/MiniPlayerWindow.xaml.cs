using FluxPlayer.Services;
using LibVLCSharp.Shared;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FluxPlayer.Windows;

public partial class MiniPlayerWindow : Wpf.Ui.Controls.FluentWindow
{

    private DateTime _playStateLockedUntil;

    private MediaPlayer? _mediaPlayer;
    private VlcWpfRenderer? _renderer;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _progressTimer;
    private bool _controlsVisible;
    private Point _lastMouse;

    public Func<string?>? TitleProvider { get; set; }

    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;

    public MiniPlayerWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (DataContext is FluxPlayer.ViewModels.MainViewModel { IsAudioMode: true }) return;
            if (ProgressHost.IsMouseOver || PlayBtn.IsMouseOver || PrevBtn.IsMouseOver || NextBtn.IsMouseOver)
            {
                _hideTimer.Start();
                return;
            }
            SetControlsVisible(false);
        };

        _progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => RefreshState();

        Loaded += (_, _) =>
        {
            RefreshState();
            _progressTimer.Start();
            SetControlsVisible(true);
            _hideTimer.Start();
        };

        Closed += (_, _) =>
        {
            _progressTimer.Stop();
            _hideTimer.Stop();

            try { _renderer?.RemoveMirror(MiniVideoImage); } catch { }
        };
    }

    public void SetMediaPlayer(MediaPlayer player) => _mediaPlayer = player;

    public void AttachRenderer(VlcWpfRenderer renderer)
    {
        _renderer = renderer;
        renderer.AddMirror(MiniVideoImage);
        if (renderer.CurrentBitmap != null)
            MiniVideoImage.Source = renderer.CurrentBitmap;
    }

    private void RefreshState()
    {
        string? title = TitleProvider?.Invoke();
        if (TitleText.Text != title) TitleText.Text = title ?? "";

        var mp = _mediaPlayer;
        if (mp == null) return;

        if (DateTime.UtcNow >= _playStateLockedUntil)
            PlayIcon.IsPlaying = mp.IsPlaying;

        if (!_controlsVisible) return;

        long len = mp.Length;
        long time = Math.Max(0, mp.Time);
        double pos = len > 0 ? Math.Clamp((double)time / len, 0, 1) : 0;
        ProgressFill.Width = ProgressTrack.ActualWidth * pos;
        TimeText.Text = FormatTime(time);
        DurationText.Text = len > 0 ? FormatTime(len) : "";
    }

    private static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private void SetControlsVisible(bool visible)
    {
        if (_controlsVisible == visible) return;
        _controlsVisible = visible;
        if (visible) RefreshState();
        var anim = new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 180 : 320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ControlsOverlay.BeginAnimation(OpacityProperty, anim);
        ControlsOverlay.IsHitTestVisible = visible;
        Cursor = visible ? null : Cursors.None;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _lastMouse.X) < 1 && Math.Abs(p.Y - _lastMouse.Y) < 1) return;
        _lastMouse = p;
        SetControlsVisible(true);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(600);
        _hideTimer.Start();
        Dispatcher.BeginInvoke(() => _hideTimer.Interval = TimeSpan.FromSeconds(2.2), DispatcherPriority.ApplicationIdle);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Close();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mp = _mediaPlayer;
        switch (e.Key)
        {
            case Key.Space:
                if (!e.IsRepeat)
                {
                    TogglePlay();
                    FluxPlayer.Controls.ButtonFx.Pulse(PlayBtn);
                }
                break;
            case Key.Escape:
                Close();
                break;
            case Key.Left when mp != null && mp.IsSeekable:
                mp.Time = Math.Max(0, mp.Time - 5000);
                break;
            case Key.Right when mp != null && mp.IsSeekable:
                mp.Time = Math.Min(Math.Max(0, mp.Length - 500), mp.Time + 5000);
                break;
            default:
                return;
        }
        e.Handled = true;
        SetControlsVisible(true);
        _hideTimer.Stop();
        _hideTimer.Start();
        RefreshState();
    }

    private void TogglePlay()
    {
        var mp = _mediaPlayer;
        if (mp == null) return;
        bool wasPlaying = mp.IsPlaying;
        if (wasPlaying) mp.Pause(); else mp.Play();

        PlayIcon.IsPlaying = !wasPlaying;
        _playStateLockedUntil = DateTime.UtcNow.AddMilliseconds(700);
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void PrevBtn_Click(object sender, RoutedEventArgs e)
        => PreviousRequested?.Invoke(this, EventArgs.Empty);

    private void NextBtn_Click(object sender, RoutedEventArgs e)
        => NextRequested?.Invoke(this, EventArgs.Empty);

    private void RestoreBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseMiniBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Progress_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var mp = _mediaPlayer;
        if (mp == null || !mp.IsSeekable || ProgressHost.ActualWidth <= 0) return;
        double f = Math.Clamp(e.GetPosition(ProgressHost).X / ProgressHost.ActualWidth, 0, 1);
        mp.Position = (float)f;
        ProgressFill.Width = ProgressTrack.ActualWidth * f;
    }

    private void Progress_MouseEnter(object sender, MouseEventArgs e) => AnimateProgressHeight(5);
    private void Progress_MouseLeave(object sender, MouseEventArgs e) => AnimateProgressHeight(3);

    private void AnimateProgressHeight(double h)
    {
        var anim = new DoubleAnimation(h, TimeSpan.FromMilliseconds(140));
        ProgressTrack.BeginAnimation(HeightProperty, anim);
        ProgressFill.BeginAnimation(HeightProperty, anim);
    }

    private void ProgressHost_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshState();

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, MaxHeight);
    }
}
