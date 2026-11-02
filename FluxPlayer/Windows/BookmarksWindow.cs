using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace FluxPlayer.Windows;

/// <summary>Lists the bookmarks of the current video: filter, jump, move several at once, delete, and save to / load from a text file.</summary>
public sealed class BookmarksWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, MinHeight = 240, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _filter = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _shift = new() { Text = "1000", Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { MinHeight = 18, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Action _changed;

    public BookmarksWindow(Window owner, MainViewModel vm, Action changed)
    {
        _vm = vm; _changed = changed;
        Owner = owner; Title = Localizer.T("Bookmarks");
        Width = 540; Height = 560; MinWidth = 460; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendsContentIntoTitleBar = false; ShowInTaskbar = false;

        _filter.TextChanged += (_, _) => Refresh();
        _list.MouseDoubleClick += (_, _) => Go();
        Button Make(string text, Action action) { var button = new Button { Content = Localizer.T(text), Margin = new Thickness(0, 0, 6, 6) }; button.Click += (_, _) => Guard(action); return button; }
        var buttons = new WrapPanel
        {
            Children =
            {
                Make("Go to", Go), Make("Remove selected", RemoveSelected), Make("Save to a file…", Save), Make("Load from a file…", Load),
            }
        };
        var shiftRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        shiftRow.Children.Add(new TextBlock { Text = Localizer.T("Move by (ms, negative goes back)"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        shiftRow.Children.Add(_shift);
        shiftRow.Children.Add(MakeSpaced(Make("Move selected", () => ShiftBy(false))));
        shiftRow.Children.Add(MakeSpaced(Make("Move all", () => ShiftBy(true))));

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Localizer.T("Filter by time"), FontWeight = FontWeights.SemiBold });
        top.Children.Add(_filter);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel();
        bottom.Children.Add(buttons); bottom.Children.Add(shiftRow); bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(_list);
        Content = root;
        Refresh();
        Loaded += (_, _) => _filter.Focus();
    }

    private static Button MakeSpaced(Button button) { button.Margin = new Thickness(8, 0, 0, 0); return button; }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { _status.Text = e.Message; }
    }

    private void Refresh()
    {
        var items = BookmarkTools.Filter(_vm.Bookmarks, _filter.Text);
        _list.ItemsSource = items.Select(ms => new ListBoxItem { Content = TimeText.Format(ms, "ClockMilliseconds"), Tag = ms }).ToList();
        _status.Text = string.Format(Localizer.T("{0} of {1} bookmarks"), items.Count, _vm.Bookmarks.Count);
    }

    private HashSet<long> Selected() => _list.SelectedItems.OfType<ListBoxItem>().Select(item => (long)item.Tag).ToHashSet();

    private void Go()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: long ms }) _vm.SeekToMs(ms);
    }

    private void RemoveSelected()
    {
        var chosen = Selected();
        if (chosen.Count == 0) return;
        _vm.ReplaceBookmarks(BookmarkTools.Remove(_vm.Bookmarks, chosen));
        _changed(); Refresh();
    }

    private void ShiftBy(bool all)
    {
        if (!long.TryParse(_shift.Text.Trim(), out long delta) || Math.Abs(delta) > 3_600_000) throw new ArgumentException(Localizer.T("Enter a number of milliseconds."));
        var chosen = all ? null : Selected();
        if (chosen is { Count: 0 }) { _status.Text = Localizer.T("Select bookmarks to move first."); return; }
        _vm.ReplaceBookmarks(BookmarkTools.Shift(_vm.Bookmarks, delta, _vm.TotalDurationMs, chosen));
        _changed(); Refresh();
    }

    private void Save()
    {
        if (_vm.Bookmarks.Count == 0) { _status.Text = Localizer.T("There are no bookmarks to save."); return; }
        var dialog = new SaveFileDialog { Title = Localizer.T("Save bookmarks"), Filter = "Text|*.txt|All files|*.*", FileName = Path.GetFileNameWithoutExtension(_vm.CurrentItem?.FilePath ?? "bookmarks") + ".bookmarks.txt", AddExtension = true, DefaultExt = ".txt" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, BookmarkTools.Export(_vm.Bookmarks, _vm.CurrentItem?.Title), new System.Text.UTF8Encoding(false));
        _status.Text = Localizer.T("Saved:") + " " + dialog.FileName;
    }

    private void Load()
    {
        var dialog = new OpenFileDialog { Title = Localizer.T("Load bookmarks"), Filter = "Text|*.txt|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > BookmarkTools.MaximumFileBytes) throw new InvalidDataException(Localizer.T("The bookmark file is too large."));
        var loaded = BookmarkTools.Import(File.ReadAllText(dialog.FileName), _vm.TotalDurationMs);
        if (loaded.Count == 0) { _status.Text = Localizer.T("No bookmarks were found in that file."); return; }
        _vm.ReplaceBookmarks(BookmarkTools.Normalize(_vm.Bookmarks.Concat(loaded)));
        _changed(); Refresh();
        _status.Text = string.Format(Localizer.T("Loaded {0} bookmarks."), loaded.Count);
    }
}
