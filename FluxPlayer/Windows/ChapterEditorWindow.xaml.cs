using CommunityToolkit.Mvvm.ComponentModel;
using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class ChapterEditorRow : ObservableObject, IDataErrorInfo
{
    [ObservableProperty] private string _time = "00:00:00.000";
    [ObservableProperty] private string _title = "Chapter";
    public string Error => this[nameof(Time)] + this[nameof(Title)];
    public string this[string columnName] => columnName switch
    {
        nameof(Time) when !ChapterDocument.TryParseTime(Time, out _) => "Use hours:minutes:seconds.milliseconds, within 999 hours.",
        nameof(Title) when string.IsNullOrWhiteSpace(Title) || Title.Length > 512 || Title.Any(char.IsControl) => "Use a title of 1–512 printable characters.",
        _ => ""
    };
}

public partial class ChapterEditorWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<ChapterEditorRow> _rows = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy, _closed;
    private string? _sourcePath;

    public ChapterEditorWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm; DataContext = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        ChaptersGrid.ItemsSource = _rows; CopyPlayerChapters();
        CollectionViewSource.GetDefaultView(_rows).Filter = value => value is ChapterEditorRow row
            && (row.Title.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) || row.Time.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); if (!_busy) _lifetime.Dispose(); };
    }
    private void ShowChapters(IEnumerable<ChapterInfo> chapters)
    {
        _rows.Clear(); foreach (var chapter in chapters) _rows.Add(new() { Time = ChapterDocument.FormatTime(chapter.TimeMs), Title = chapter.Name });
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    { if (ChaptersGrid?.ItemsSource != null) CollectionViewSource.GetDefaultView(_rows).Refresh(); }
    private void CopyPlayerChapters()
    {
        ShowChapters(_vm.Chapters); _sourcePath = _vm.ExternalChapterPath;
    }
    private ChapterInfo[] ReadRows(bool sort = false)
    {
        ChaptersGrid.CommitEdit(DataGridEditingUnit.Cell, true); ChaptersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var values = _rows.Select((row, index) =>
        {
            if (!ChapterDocument.TryParseTime(row.Time, out long ms)) throw new InvalidDataException($"Chapter {index + 1} has an invalid start time.");
            return new ChapterInfo(index, row.Title, ms);
        });
        return ChapterDocument.Validate(sort ? values.OrderBy(chapter => chapter.TimeMs) : values);
    }
    private void Add(long ms)
    {
        if (_busy) return;
        if (_rows.Count >= ChapterDocument.MaximumChapters) { StatusText.Text = "The chapter limit has been reached."; return; }
        var row = new ChapterEditorRow { Time = ChapterDocument.FormatTime(Math.Clamp(ms, 0, ChapterDocument.MaximumTimeMs)), Title = $"Chapter {_rows.Count + 1}" };
        _rows.Add(row); ChaptersGrid.SelectedItem = row; ChaptersGrid.ScrollIntoView(row);
    }
    private void AddRow_Click(object sender, RoutedEventArgs e) => Add(0);
    private void AddCurrent_Click(object sender, RoutedEventArgs e)
    { if (_vm.HasMedia) Add(Math.Max(0, _vm.Engine.GetTime())); else StatusText.Text = "Open audio or video in the player first."; }
    private void Remove_Click(object sender, RoutedEventArgs e)
    { if (!_busy && ChaptersGrid.SelectedItem is ChapterEditorRow row) _rows.Remove(row); }
    private void Copy_Click(object sender, RoutedEventArgs e) { if (!_busy) CopyPlayerChapters(); }
    private void Sort_Click(object sender, RoutedEventArgs e)
    { try { ShowChapters(ReadRows(true)); } catch (InvalidDataException ex) { StatusText.Text = ex.Message; } }
    private void Seek_Click(object sender, RoutedEventArgs e)
    {
        if (ChaptersGrid.SelectedItem is not ChapterEditorRow row || !ChapterDocument.TryParseTime(row.Time, out long ms)) return;
        if (!_vm.HasMedia) { StatusText.Text = "Open audio or video first."; return; }
        _vm.SeekToMs(ms);
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try { StatusText.Text = _vm.ApplyExternalChapters(ReadRows(), _sourcePath) ? "Chapters applied to the current media. Use the Chapters menu to navigate." : "Open audio or video first."; }
        catch (InvalidDataException ex) { StatusText.Text = ex.Message; }
    }
    private void Restore_Click(object sender, RoutedEventArgs e)
    { _vm.RestoreNativeChapters(); StatusText.Text = "Player navigation restored to the source's native chapters. The editor draft is retained."; }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Open external chapters", Filter = "Chapter files|*.txt;*.ogm;*.xml|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => ChapterDocument.Read(dialog.FileName, _lifetime.Token), _lifetime.Token);
            if (_closed) return;
            ShowChapters(result.Chapters); _sourcePath = dialog.FileName; StatusText.Text = result.Note + " Choose Apply to current media to use this draft.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (!_closed) StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var chapters = ReadRows();
            string name = _vm.CurrentItem is { IsNetwork: false } item ? Path.GetFileNameWithoutExtension(item.FilePath) : "My chapters";
            var dialog = new SaveFileDialog { Title = "Save a separate chapter copy", Filter = "OGM simple chapters|*.txt|Matroska chapters XML|*.xml", FileName = name + ".chapters.txt" };
            if (dialog.ShowDialog(this) != true) return;
            if (_sourcePath != null && string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a different path to preserve the imported chapter file.");
            SetBusy(true);
            await Task.Run(() => ChapterDocument.Write(dialog.FileName, chapters, dialog.FilterIndex == 2, _lifetime.Token), _lifetime.Token);
            if (!_closed) StatusText.Text = "Chapter copy saved. The imported file and media container are unchanged.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (!_closed) StatusText.Text = ex.Message; }
        finally { if (_busy) SetBusy(false); }
    }
    private void SetBusy(bool value)
    {
        _busy = value;
        if (_closed) { if (!value) _lifetime.Dispose(); return; }
        OpenButton.IsEnabled = ChaptersGrid.IsEnabled = ActionsPanel.IsEnabled = !value;
    }
}
