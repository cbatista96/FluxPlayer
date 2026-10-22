using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class LibraryLabelsWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly string[] _paths;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy, _closed;

    public LibraryLabelsWindow(Window owner, MainViewModel vm, LibraryEntry[] selected)
    {
        InitializeComponent(); Owner = owner; _vm = vm; _paths = selected.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        SelectionText.Text = $"Edit labels for {_paths.Length:N0} selected library {(_paths.Length == 1 ? "file" : "files")}. Adding and removing apply to the full selection.";
        FilesGrid.ItemsSource = selected;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); if (!_busy) _lifetime.Dispose(); };
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await ApplyAsync(LibraryLabelEdit.Add);
    private async void Remove_Click(object sender, RoutedEventArgs e) => await ApplyAsync(LibraryLabelEdit.Remove);
    private async Task ApplyAsync(LibraryLabelEdit edit)
    {
        if (_busy) return;
        try
        {
            var labels = LibraryLabels.Parse(LabelsBox.Text);
            if (labels.Length == 0) { StatusText.Text = "Enter at least one label, one per line."; return; }
            _busy = true; AddButton.IsEnabled = RemoveButton.IsEnabled = LabelsBox.IsEnabled = false;
            StatusText.Text = "Saving personal labels… Close this window to cancel before the change is committed.";
            int changed = await _vm.Library.UpdateLabelsAsync(_paths, labels, edit, _lifetime.Token);
            if (_closed) return;
            var selected = _paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            FilesGrid.ItemsSource = (await _vm.Library.SnapshotAsync()).Where(entry => selected.Contains(entry.Path)).ToArray();
            if (!_closed) StatusText.Text = $"Saved labels for {changed:N0} {(changed == 1 ? "file" : "files")}. Other selected files were unchanged or are no longer in the library.";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Label edit canceled before commit."; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { if (!_closed) StatusText.Text = ex.Message; }
        finally
        {
            _busy = false;
            if (!_closed) AddButton.IsEnabled = RemoveButton.IsEnabled = LabelsBox.IsEnabled = true;
            else _lifetime.Dispose();
        }
    }
}
