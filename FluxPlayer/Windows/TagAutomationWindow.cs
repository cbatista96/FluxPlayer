using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

public sealed class TagAutomationWindow : FeatureWorkbench
{
    public TagAutomationWindow(Window owner, MainViewModel vm, int initial) : base(owner, vm, "Tag automation", "Preview per-file metadata changes before applying them to original files. Unselected fields are preserved. Loaded files and stale previews are rejected. Completed writes remain if a later file fails or you cancel.")
    {
        var mode = Choice("Operation", new[] { "Filename pattern", "Track numbering", "Change case", "Latin transliteration" }, Math.Clamp(initial, 0, 3)); var pattern = Text("Filename pattern: {artist}, {title}, {album}, {genre}, {track}", "{artist} - {title}"); var start = Text("First track number", "1"); var step = Text("Numbering step", "1"); var casing = Choice("Case transformation", new[] { "Title", "Sentence", "Upper", "Lower" });
        var title = Toggle("Transform title", true); var artist = Toggle("Transform artist"); var album = Toggle("Transform album"); var genre = Toggle("Transform genre");
        var table = Table(("File", "File", 1.5), ("Current metadata", "Current", 2), ("Proposed changes", "Proposed", 2)); string[] files = []; TagAutomationRow[] preview = [];
        table.RowHeight = double.NaN;
        table.MinRowHeight = 44;
        var cellText = new Style(typeof(TextBlock));
        cellText.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        cellText.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(6)));
        foreach (var column in table.Columns.OfType<DataGridTextColumn>()) column.ElementStyle = cellText;
        void Invalidate() { preview = []; table.ItemsSource = null; Status.Text = "Options changed. Preview again before applying."; }
        void Fields() { ((FrameworkElement)pattern.Parent).Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)start.Parent).Visibility = ((FrameworkElement)step.Parent).Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; ((FrameworkElement)casing.Parent).Visibility = mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; title.Visibility = artist.Visibility = album.Visibility = genre.Visibility = mode.SelectedIndex >= 2 ? Visibility.Visible : Visibility.Collapsed; }
        mode.SelectionChanged += (_, _) => { Fields(); Invalidate(); }; Fields();
        foreach (var text in new[] { pattern, start, step }) text.TextChanged += (_, _) => Invalidate(); casing.SelectionChanged += (_, _) => Invalidate(); foreach (var check in new[] { title, artist, album, genre }) { check.Checked += (_, _) => Invalidate(); check.Unchecked += (_, _) => Invalidate(); }
        Action("Choose files…", () => { var dialog = new OpenFileDialog { Filter = FluxPlayer.Models.MediaFormats.FileDialogFilter, Multiselect = true }; if (dialog.ShowDialog(this) == true) { files = dialog.FileNames; Invalidate(); Status.Text = $"Selected files: {files.Length}. Numbering follows dialog order."; } return Task.CompletedTask; });
        Action("Preview changes", async () => { if (!int.TryParse(start.Text, out int first) || !int.TryParse(step.Text, out int increment)) throw new ArgumentException("Use whole numbers for numbering."); var options = new TagAutomationOptions((TagAutomationMode)mode.SelectedIndex, pattern.Text, first, increment, casing.SelectedItem?.ToString() ?? "Title", title.IsChecked == true, artist.IsChecked == true, album.IsChecked == true, genre.IsChecked == true); preview = await TagAutomation.Preview(files, options, OperationToken); table.ItemsSource = preview; Status.Text = "Review the proposed values. Apply selected rows, or all preview rows when no selection is made."; });
        Action("Apply reviewed preview", async () => { var selected = table.SelectedItems.Cast<TagAutomationRow>().ToArray(); var plan = selected.Length > 0 ? selected : preview; int count = await TagAutomation.Apply(plan, vm.Engine, vm.Library, new Progress<string>(file => Status.Text = "Writing " + file), OperationToken); Invalidate(); Status.Text = $"Saved and verified {count} files. Preview again before another write."; });
        var cancel = CancelAction(); ((StackPanel)Actions.Parent).Children.Add(cancel);
        Status.Text = "Choose up to 500 local files. Transliteration normalizes Latin diacritics and ligatures; other scripts are preserved.";
    }
}
