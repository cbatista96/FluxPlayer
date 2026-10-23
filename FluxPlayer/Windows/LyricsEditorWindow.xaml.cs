using FluxPlayer.Services;
using FluxPlayer.Models;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class LyricsEditorWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private bool _busy, _closed;
    public LyricsEditorWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm; DataContext = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Closed += (_, _) => _closed = true;
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true; Actions.IsEnabled = Editor.IsEnabled = false;
        try { await operation(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        { if (!_closed) Status.Text = e.Message; }
        finally { _busy = false; if (!_closed) Actions.IsEnabled = Editor.IsEnabled = true; }
    }
    private Task LoadAsync(string path) => RunAsync(async () =>
    {
        string text = await LyricFile.ReadAsync(path);
        if (_closed) return;
        Editor.Text = text; Status.Text = "Loaded " + path;
    });
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Synchronized lyrics|*.lrc", Title = "Open LRC lyrics" };
        if (dialog.ShowDialog(this) == true) await LoadAsync(dialog.FileName);
    }
    private async void Sidecar_Click(object sender, RoutedEventArgs e)
    {
        string? source = _vm.CurrentItem?.FilePath;
        if (source == null || MediaFormats.IsNetwork(source)) { Status.Text = "Open a local media file first."; return; }
        await LoadAsync(Path.ChangeExtension(source, ".lrc"));
    }
    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasMedia) { Status.Text = "Open media before inserting its current position."; return; }
        try
        {
            int caret = Editor.CaretIndex; string stamp = LyricFile.Timestamp(Math.Max(0, _vm.Engine.GetTime()));
            Editor.Text = Editor.Text.Insert(caret, stamp); Editor.CaretIndex = caret + stamp.Length; Editor.Focus();
        }
        catch (ArgumentOutOfRangeException e2) { Status.Text = e2.Message; }
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try { _vm.ApplySessionLyrics(Editor.Text); Status.Text = "Session lyrics applied. Opening another source restores its own sidecar."; }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { Status.Text = ex.Message; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Synchronized lyrics|*.lrc", DefaultExt = ".lrc", FileName = "lyrics-edited.lrc", Title = "Save a new LRC copy" };
        if (dialog.ShowDialog(this) != true) return;
        string text = Editor.Text;
        await RunAsync(async () => { await LyricFile.WriteCopyAsync(dialog.FileName, text); if (!_closed) Status.Text = "Saved " + dialog.FileName; });
    }
}
