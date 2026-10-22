using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public sealed record SubtitleTimingRow(SubtitleCue Original, SubtitleTiming Timing)
{
    public string OriginalStart => SubtitleEditingService.FormatTime(Original.StartMs);
    public string AdjustedStart => Timing.Map(Original.EndMs) <= 0 ? "Before video" : SubtitleEditingService.FormatTime(Math.Max(0, Timing.Map(Original.StartMs)));
    public string AdjustedEnd => Timing.Map(Original.EndMs) <= 0 ? "Omitted" : SubtitleEditingService.FormatTime(Math.Max(Math.Max(0, Timing.Map(Original.StartMs)) + 1, Timing.Map(Original.EndMs)));
    public string Text => Original.Text;
}

public partial class SubtitleTimingWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly CancellationTokenSource _lifetime = new();
    private SubtitleCue[] _cues = Array.Empty<SubtitleCue>();
    private SubtitleAnchor? _firstAnchor;
    private string? _sourcePath, _savedPath;
    private bool _busy, _closed;

    public SubtitleTimingWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm; DataContext = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        LiveDelayBox.Text = vm.SubtitleDelayMs.ToString(CultureInfo.InvariantCulture);
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); };
        if (vm.LoadedSubtitlePath is { } path) Loaded += async (_, _) => await LoadPathAsync(path);
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        if (_closed) return;
        OpenButton.IsEnabled = CorrectionPanel.IsEnabled = !value;
        LoadOriginalButton.IsEnabled = !value && _sourcePath != null;
        SaveButton.IsEnabled = !value && _cues.Length > 0;
        LoadSavedButton.IsEnabled = !value && _savedPath != null;
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Open subtitles for timing correction", Filter = "Text subtitles|*.srt;*.vtt;*.ass;*.ssa" };
        if (dialog.ShowDialog(this) == true) await LoadPathAsync(dialog.FileName);
    }

    private async Task LoadPathAsync(string path)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var cues = await Task.Run(() => SubtitleDocument.Read(path), _lifetime.Token);
            var rows = await Task.Run(() => cues.Select(cue => new SubtitleTimingRow(cue, new())).ToArray(), _lifetime.Token);
            if (_closed) return;
            if (cues.Length == 0) throw new InvalidDataException("No supported text subtitle cues were found.");
            _cues = cues; _sourcePath = Path.GetFullPath(path); _savedPath = null; _firstAnchor = null;
            SourceText.Text = Path.GetFileName(path); SourceText.ToolTip = path;
            OffsetBox.Text = "0"; ScaleBox.Text = "100";
            AnchorText.Text = "Select a cue and capture its matching video time. Two anchors calculate drift and offset.";
            CuesGrid.ItemsSource = rows;
            CuesGrid.SelectedIndex = 0;
            StatusText.Text = $"{cues.Length:N0} cues loaded. The original file will be preserved.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private SubtitleTiming ReadTiming()
    {
        if (!long.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long offset)
            || !decimal.TryParse(ScaleBox.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal percent))
            throw new ArgumentException("Enter a whole millisecond offset and a numeric percentage using a decimal point.");
        var timing = new SubtitleTiming(percent / 100m, offset); timing.Validate(); return timing;
    }

    private async Task UpdateTableAsync()
    {
        if (_busy || _cues.Length == 0) return;
        SetBusy(true);
        try
        {
            var timing = ReadTiming(); var cues = _cues;
            var preview = await Task.Run(() => SubtitleEditingService.Retime(cues, timing, _lifetime.Token));
            var rows = await Task.Run(() => cues.Select(cue => new SubtitleTimingRow(cue, timing)).ToArray(), _lifetime.Token);
            if (_closed) return;
            CuesGrid.ItemsSource = rows; CuesGrid.SelectedIndex = 0;
            StatusText.Text = $"Export preview: {preview.Cues.Length:N0} cues; {preview.RemovedBeforeStart:N0} end before zero and will be omitted. Apply live delay separately, or save and load the corrected copy.";
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException ex) { if (!_closed) StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await UpdateTableAsync();
    private async void ResetCorrection_Click(object sender, RoutedEventArgs e)
    {
        _firstAnchor = null; OffsetBox.Text = "0"; ScaleBox.Text = "100";
        AnchorText.Text = "Select a cue and capture its matching video time. Two anchors calculate drift and offset.";
        await UpdateTableAsync();
    }

    private void SetLiveDelay(long delay)
    {
        if (delay is < -86_400_000 or > 86_400_000) { StatusText.Text = "Live delay must be within 24 hours."; return; }
        _vm.SubtitleDelayMs = delay; LiveDelayBox.Text = delay.ToString(CultureInfo.InvariantCulture);
        StatusText.Text = $"Current subtitle delay: {delay:+0;-0;0} ms. This affects the loaded track and is separate from export correction.";
    }
    private void LiveDelay_Click(object sender, RoutedEventArgs e)
    {
        if (long.TryParse(LiveDelayBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long delay)) SetLiveDelay(delay);
        else StatusText.Text = "Enter a whole millisecond delay.";
    }
    private void Earlier_Click(object sender, RoutedEventArgs e) => SetLiveDelay(_vm.SubtitleDelayMs - 100);
    private void Later_Click(object sender, RoutedEventArgs e) => SetLiveDelay(_vm.SubtitleDelayMs + 100);
    private void ResetLive_Click(object sender, RoutedEventArgs e) => SetLiveDelay(0);

    private SubtitleAnchor ReadAnchor()
    {
        if (CuesGrid.SelectedItem is not SubtitleTimingRow row || !_vm.HasMedia)
            throw new ArgumentException("Start video playback, select a subtitle cue, then pause at its matching video time.");
        return new(row.Original.StartMs, Math.Max(0, _vm.Engine.GetTime()));
    }
    private void FirstAnchor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _firstAnchor = ReadAnchor();
            AnchorText.Text = $"First anchor: {SubtitleEditingService.FormatTime(_firstAnchor.SubtitleMs)} → {SubtitleEditingService.FormatTime(_firstAnchor.VideoMs)}. Select a later cue and capture the second match.";
        }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; }
    }
    private async void SecondAnchor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_firstAnchor == null) throw new ArgumentException("Capture the first anchor before the second.");
            var second = ReadAnchor(); var timing = SubtitleTiming.FromAnchors(_firstAnchor, second);
            OffsetBox.Text = timing.OffsetMs.ToString(CultureInfo.InvariantCulture);
            ScaleBox.Text = (timing.Scale * 100m).ToString("0.##########################", CultureInfo.InvariantCulture);
            AnchorText.Text = $"Two anchors: time scale {timing.Scale * 100m:0.######}%, offset {timing.OffsetMs:+0;-0;0} ms. Save a corrected copy to apply drift correction in the player.";
            await UpdateTableAsync();
        }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; }
    }

    private void Seek_Click(object sender, RoutedEventArgs e)
    {
        if (CuesGrid.SelectedItem is SubtitleTimingRow row && _vm.HasMedia)
            _vm.SeekToMs(Math.Max(0, row.Original.StartMs + _vm.SubtitleDelayMs));
    }
    private void LoadOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePath != null) StatusText.Text = _vm.LoadExternalSubtitle(_sourcePath)
            ? "Original subtitles loaded. Use the live delay controls while watching."
            : "Start video playback before loading the subtitle file.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _sourcePath == null) return;
        SubtitleTiming timing;
        try { timing = ReadTiming(); } catch (ArgumentException ex) { StatusText.Text = ex.Message; return; }
        var dialog = new SaveFileDialog
        {
            Title = "Save corrected SRT copy",
            Filter = "SubRip subtitles|*.srt",
            DefaultExt = ".srt",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_sourcePath) + ".corrected.srt",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), _sourcePath, StringComparison.OrdinalIgnoreCase))
        { StatusText.Text = "Choose a different path to preserve the original subtitle file."; return; }
        var cues = _cues; SetBusy(true);
        try
        {
            var result = await Task.Run(() =>
            {
                var edited = SubtitleEditingService.Retime(cues, timing, _lifetime.Token);
                SubtitleEditingService.WriteSrt(dialog.FileName, edited.Cues, true, _lifetime.Token); return edited;
            });
            if (_closed) return;
            _savedPath = dialog.FileName;
            StatusText.Text = $"Saved {result.Cues.Length:N0} cues to {dialog.FileName}. Original preserved; {result.RemovedBeforeStart:N0} cues before zero omitted.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private void LoadSaved_Click(object sender, RoutedEventArgs e)
    {
        if (_savedPath == null) return;
        if (!_vm.LoadExternalSubtitle(_savedPath)) { StatusText.Text = "Start video playback before loading the saved copy."; return; }
        SetLiveDelay(0); StatusText.Text = "Corrected copy loaded; live delay reset to zero to avoid applying it twice.";
    }
}
