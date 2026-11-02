using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FluxPlayer.Windows;

public partial class AdvancedCueWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private string? _sourcePath;
    private CueDocumentData? _validated;
    private CancellationTokenSource? _operation;
    private bool _closed;
    public AdvancedCueWindow(Window owner, MainViewModel vm, string? sourcePath = null)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        Loaded += async (_, _) => { if (sourcePath != null) await LoadAsync(sourcePath); };
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }
    private void Document_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { _validated = null; if (Tracks != null) Tracks.ItemsSource = null; if (SaveButton != null) SaveButton.IsEnabled = false; if (PlayButton != null) PlayButton.IsEnabled = false; }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open advanced CUE document", Filter = "CUE sheet|*.cue" };
        if (dialog.ShowDialog(this) == true) await LoadAsync(dialog.FileName);
    }
    private async Task LoadAsync(string path)
    {
        bool legacy = EncodingChoice.SelectedIndex == 1;
        await Run(async token =>
        {
            string text = await CueDocument.ReadAsync(path, legacy, token); if (_closed) return;
            _sourcePath = Path.GetFullPath(path); DocumentText.Text = text;
            Status.Text = "Loaded " + _sourcePath + ". Validate the draft before navigation or export.";
        });
    }
    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePath == null) { Status.Text = "Open a CUE document first so relative references have a base folder."; return; }
        string text = DocumentText.Text, source = _sourcePath;
        await Run(async token =>
        {
            var parsed = await Task.Run(() => CueDocument.Parse(text, source, token), token); if (_closed) return;
            _validated = parsed; Tracks.ItemsSource = parsed.Tracks; Tracks.SelectedIndex = 0;
            Status.Text = $"Validated {parsed.Tracks.Length} track(s). Unknown directives are retained verbatim; referenced files are not modified.";
        });
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (Tracks.SelectedItem is not CueDocumentTrack track || track.Mode != "AUDIO" || !File.Exists(track.Source))
        { Status.Text = "Choose an AUDIO track with an available media source."; return; }
        _vm.OpenAt(track.Source, track.StartMs);
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var document = _validated; if (document == null) return;
        var dialog = new SaveFileDialog { Title = "Save a separate CUE copy", Filter = "UTF-8 CUE sheet|*.cue", FileName = Path.GetFileNameWithoutExtension(document.SourcePath) + "_edited.cue" };
        if (dialog.ShowDialog(this) != true) return;
        await Run(async token => { await Task.Run(() => CueDocument.WriteCopyAsync(dialog.FileName, document, token), token); if (!_closed) Status.Text = "Saved UTF-8 copy: " + dialog.FileName; });
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_operation != null) return; using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        Actions.IsEnabled = DocumentText.IsEnabled = false;
        try { await action(cancellation.Token); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or DecoderFallbackException or OperationCanceledException)
        { if (!_closed) Status.Text = e.Message; }
        finally { _operation = null; if (!_closed) { Actions.IsEnabled = DocumentText.IsEnabled = true; SaveButton.IsEnabled = PlayButton.IsEnabled = _validated != null; } }
    }
}
