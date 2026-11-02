using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TreeView = System.Windows.Controls.TreeView;
using TreeViewItem = System.Windows.Controls.TreeViewItem;

namespace FluxPlayer.Windows;

public sealed record MenuNode(string Key, string Text, bool Protected, List<MenuNode> Children);

/// <summary>A tree of the right-click menu with a check box per entry: unchecked entries are hidden. Entries that lead to Settings cannot be unchecked.</summary>
public sealed class MenuCustomizationWindow : FluentWindow
{
    private readonly Dictionary<CheckBox, MenuNode> _boxes = new();

    public List<string> Hidden { get; private set; } = [];

    public MenuCustomizationWindow(Window owner, List<MenuNode> tree, IEnumerable<string> hidden)
    {
        Owner = owner; Title = Localizer.T("Customize this menu"); Width = 480; Height = 620; MinWidth = 400; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ExtendsContentIntoTitleBar = false; ShowInTaskbar = false;
        var hiddenSet = hidden.ToHashSet(StringComparer.Ordinal);
        var view = new TreeView { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var node in tree) view.Items.Add(Build(node, hiddenSet, false));
        var ok = new Button { Content = Localizer.T("Save"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Localizer.T("Cancel"), IsCancel = true, MinWidth = 90 };
        var reset = new Button { Content = Localizer.T("Show everything"), Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) => { foreach (var box in _boxes.Keys) box.IsChecked = true; };
        ok.Click += (_, _) =>
        {
            Hidden = MenuCustomization.Sanitize(_boxes.Where(pair => pair.Key.IsChecked != true && !pair.Value.Protected && IsTopmostUnchecked(pair.Key)).Select(pair => pair.Value.Key));
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { reset, ok, cancel } };
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var intro = new TextBlock { Text = Localizer.T("Untick the entries you do not want in the right-click menu. Entries that open Settings always stay."), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(intro, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(intro); root.Children.Add(buttons); root.Children.Add(view);
        Content = root;
    }

    // Only the topmost unchecked entry of a branch is stored; its children are hidden with it.
    private static bool IsTopmostUnchecked(CheckBox box)
    {
        for (var parent = (box.Parent as TreeViewItem)?.Parent as TreeViewItem; parent != null; parent = parent.Parent as TreeViewItem)
            if (parent.Header is CheckBox { IsChecked: not true }) return false;
        return true;
    }

    private TreeViewItem Build(MenuNode node, HashSet<string> hidden, bool parentHidden)
    {
        bool isHidden = parentHidden || MenuCustomization.IsHidden(node.Key, hidden);
        var box = new CheckBox { Content = node.Text, IsChecked = !isHidden, IsEnabled = !node.Protected };
        var item = new TreeViewItem { Header = box };
        box.Checked += (_, _) => SetChildren(item, true);
        box.Unchecked += (_, _) => SetChildren(item, false);
        _boxes[box] = node;
        foreach (var child in node.Children) item.Items.Add(Build(child, hidden, isHidden));
        return item;
    }

    private static void SetChildren(TreeViewItem item, bool visible)
    {
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            if (child.Header is CheckBox { IsEnabled: true } box) box.IsChecked = visible;
    }
}
