using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class SnapshotScheduleWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private CancellationTokenSource? _operation;
    private bool _closed;

    public SnapshotScheduleWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        IntervalBox.Text = vm.CurrentSettings.SnapshotIntervalSeconds.ToString();
        FormatCombo.SelectedIndex = vm.CurrentSettings.SnapshotFormat?.ToUpperInvariant() switch
        { "JPG" or "JPEG" => 1, "BMP" => 2, _ => 0 };
        FolderBox.Text = string.IsNullOrWhiteSpace(vm.CurrentSettings.SnapshotFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : vm.CurrentSettings.SnapshotFolder;
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Save scheduled snapshots" };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        if (!int.TryParse(IntervalBox.Text, out int seconds) || seconds is < 1 or > 3600
            || !int.TryParse(MaximumBox.Text, out int maximum) || maximum is < 1 or > 10_000)
        { StatusText.Text = "Use an interval of 1–3600 seconds and a limit of 1–10,000 images."; return; }
        string folder;
        try
        {
            if (string.IsNullOrWhiteSpace(FolderBox.Text)) throw new ArgumentException("Choose a save folder.");
            folder = Path.GetFullPath(FolderBox.Text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { StatusText.Text = ex.Message; return; }
        string extension = ((ComboBoxItem)FormatCombo.SelectedItem).Tag.ToString()!;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        OptionsPanel.IsEnabled = false; StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        CaptureProgress.Maximum = maximum; CaptureProgress.Value = 0;
        StatusText.Text = "Waiting for video playback and the first interval…";
        var settings = new SettingsService(); var saved = settings.Load();
        saved.SnapshotIntervalSeconds = seconds; settings.Save(saved);
        _vm.CurrentSettings.SnapshotIntervalSeconds = seconds;
        try
        {
            var progress = new Progress<SnapshotScheduleProgress>(p =>
            {
                if (_closed || _operation != cancellation) return;
                CaptureProgress.Value = p.Captured;
                StatusText.Text = $"{p.Captured:N0}/{p.Maximum:N0} images · {p.Skipped:N0} intervals skipped while paused, audio-only or busy.";
            });
            int count = await SnapshotSchedule.RunAsync(TimeSpan.FromSeconds(seconds), maximum, async token =>
            {
                if (_vm.IsAudioMode || !_vm.Engine.IsPlaying || _vm.Engine.MediaPlayer.VoutCount == 0 || _vm.Engine.IsSnapshotBusy) return null;
                string path = Path.Combine(folder, $"FluxPlayer_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.{extension}");
                try { return await _vm.Engine.CaptureSnapshotAsync(path, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            }, progress, cancellation.Token);
            if (!_closed) StatusText.Text = $"Completed: {count:N0} images saved to {folder}";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Capture job stopped. Completed images have been retained."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { if (!_closed) StatusText.Text = "Capture stopped: " + ex.Message; }
        finally
        {
            _operation = null;
            if (!_closed) { OptionsPanel.IsEnabled = true; StartButton.IsEnabled = true; StopButton.IsEnabled = false; }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
}
