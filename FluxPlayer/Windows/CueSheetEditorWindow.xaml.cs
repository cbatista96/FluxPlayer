using CommunityToolkit.Mvvm.ComponentModel;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class CueEditorRow : ObservableObject, IDataErrorInfo
{
    [ObservableProperty] private int _number;
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _timestamp = "00:00:00";
    public string Error => string.Join(" ", new[] { this[nameof(Source)], this[nameof(Timestamp)], this[nameof(Title)], this[nameof(Artist)] }.Where(s => s.Length > 0));
    public string this[string columnName] => columnName switch
    {
        nameof(Timestamp) when !CueSheet.TryParseTimestamp(Timestamp, out _) => "Use minutes:seconds:frames, with seconds 0–59 and frames 0–74.",
        nameof(Source) when string.IsNullOrWhiteSpace(Source) || !Path.IsPathFullyQualified(Source) => "Choose an absolute local source path.",
        nameof(Title) when InvalidText(Title) => "The title cannot contain quotes or control characters.",
        nameof(Artist) when InvalidText(Artist) => "The artist cannot contain quotes or control characters.",
        _ => ""
    };
    private static bool InvalidText(string text) => text == null || text.Length > 4096 || text.Any(c => c == '"' || char.IsControl(c));
}

public partial class CueSheetEditorWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<CueEditorRow> _tracks = new();
    private string _suggestedName = "My tracks.cue";
    private bool _busy;

    public CueSheetEditorWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        TracksGrid.ItemsSource = _tracks;
    }

    private void Renumber()
    { for (int i = 0; i < _tracks.Count; i++) _tracks[i].Number = i + 1; }

    private void AddTrack(string source, long position, string? title = null, string artist = "")
    {
        if (_tracks.Count >= 99) { StatusText.Text = "A CUE sheet supports up to 99 tracks."; return; }
        if (!File.Exists(source)) { StatusText.Text = "Choose an available local media file."; return; }
        var row = new CueEditorRow
        {
            Source = Path.GetFullPath(source),
            Title = title ?? Path.GetFileNameWithoutExtension(source),
            Artist = artist,
            Timestamp = CueSheet.FormatTimestamp(Math.Max(0, position))
        };
        _tracks.Add(row); Renumber(); TracksGrid.SelectedItem = row; TracksGrid.ScrollIntoView(row);
        StatusText.Text = $"{_tracks.Count} tracks. Edit titles, artists and start times directly in the table.";
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Add CUE source files", Filter = "Audio files|*.wav;*.flac;*.mp3;*.aif;*.aiff;*.ape;*.wv;*.m4a|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            foreach (string source in dialog.FileNames)
            {
                if (_tracks.Count >= 99) { StatusText.Text = "Only the first 99 tracks can be added to a CUE sheet."; break; }
                AddTrack(source, 0);
            }
        }
    }

    private void AddCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_vm.CurrentItem == null || !File.Exists(_vm.CurrentItem.FilePath))
        { StatusText.Text = "Open a local media file before adding its current position."; return; }
        AddTrack(_vm.CurrentItem.FilePath, _vm.Engine.GetTime(), $"Track {_tracks.Count + 1}", _vm.NowPlayingArtist);
    }

    private void Move(int offset)
    {
        if (_busy || TracksGrid.SelectedItem is not CueEditorRow row) return;
        int index = _tracks.IndexOf(row), next = index + offset;
        if (next >= 0 && next < _tracks.Count) { TracksGrid.CommitEdit(); _tracks.Move(index, next); Renumber(); TracksGrid.SelectedItem = row; }
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && TracksGrid.SelectedItem is CueEditorRow row) { _tracks.Remove(row); Renumber(); StatusText.Text = $"{_tracks.Count} tracks."; }
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Edit CUE sheet", Filter = "CUE sheet|*.cue" };
        if (dialog.ShowDialog(this) != true) return;
        _busy = true;
        try
        {
            var tracks = await Task.Run(() => CueSheet.Read(dialog.FileName, requireExistingSources: false));
            if (tracks.Length is < 1 or > 99) throw new InvalidDataException("The editor supports CUE sheets containing 1–99 audio tracks.");
            _tracks.Clear();
            foreach (var track in tracks) _tracks.Add(new CueEditorRow
            { Source = track.Source, Title = track.Title, Artist = track.Artist, Timestamp = CueSheet.FormatTimestamp(track.StartMs) });
            Renumber(); _suggestedName = Path.GetFileNameWithoutExtension(dialog.FileName) + "_edited.cue";
            StatusText.Text = $"Loaded {tracks.Length} tracks. Unavailable source paths can be repaired in the table.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { StatusText.Text = ex.Message; }
        finally { _busy = false; }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (TracksGrid.SelectedItem is CueEditorRow row && CueSheet.TryParseTimestamp(row.Timestamp, out long position) && File.Exists(row.Source))
            _vm.OpenAt(row.Source, position);
        else StatusText.Text = "Select a track with a valid time and an available source file.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!TracksGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !TracksGrid.CommitEdit(DataGridEditingUnit.Row, true)
            || _tracks.Any(row => row.Error.Length > 0))
        { StatusText.Text = "Correct the highlighted fields before saving."; return; }
        if (_tracks.Count == 0) { StatusText.Text = "Add at least one track before saving."; return; }
        var dialog = new SaveFileDialog { Title = "Save CUE sheet", Filter = "CUE sheet|*.cue", DefaultExt = ".cue", FileName = _suggestedName, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        var tracks = _tracks.Select(row =>
        { CueSheet.TryParseTimestamp(row.Timestamp, out long position); return new CueTrack(row.Source, row.Number, row.Title, row.Artist, position); }).ToArray();
        bool overwrite = File.Exists(dialog.FileName);
        _busy = true; TracksGrid.IsEnabled = false;
        try
        {
            await Task.Run(() => CueSheet.Write(dialog.FileName, tracks, overwrite));
            StatusText.Text = $"Saved {tracks.Length} tracks to {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { StatusText.Text = ex.Message; }
        finally { _busy = false; TracksGrid.IsEnabled = true; }
    }
}
