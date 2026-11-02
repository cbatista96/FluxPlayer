using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.Windows;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer;

/// <summary>Lets the user hide entries of the right-click menu. Entries that lead to Settings, and this customization itself, always stay.</summary>
public partial class MainWindow
{
    private readonly HashSet<UIElement> _hiddenByCustomization = new();

    private static string HeaderOf(MenuItem item) => item.Header as string ?? "";

    private void ApplyMenuCustomization()
    {
        foreach (var element in _hiddenByCustomization) element.Visibility = Visibility.Visible;
        _hiddenByCustomization.Clear();
        var hidden = MenuCustomization.Sanitize(_runtimeSettings.HiddenMenuItems).ToHashSet(StringComparer.Ordinal);
        if (hidden.Count > 0) Hide(MainContextMenu, [], hidden);
        TidySeparators(MainContextMenu);

        void Hide(ItemsControl menu, List<string> path, HashSet<string> set)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                string header = HeaderOf(item);
                if (header.Length == 0) continue;
                var here = new List<string>(path) { Localizer.SourceOf(item, header) };
                if (item.Visibility == Visibility.Visible && MenuCustomization.IsHidden(MenuCustomization.Key(here), set)) { item.Visibility = Visibility.Collapsed; _hiddenByCustomization.Add(item); }
                else Hide(item, here, set);
            }
        }
    }

    /// <summary>Collapses separators that would sit first, last or twice in a row once entries are hidden.</summary>
    private void TidySeparators(ItemsControl menu)
    {
        Separator? previous = null; bool itemSince = false, any = false;
        foreach (var element in menu.Items.OfType<UIElement>())
        {
            if (element is Separator separator)
            {
                if (separator.Visibility != Visibility.Visible) continue;
                if (!any || !itemSince) { separator.Visibility = Visibility.Collapsed; _hiddenByCustomization.Add(separator); }
                else { previous = separator; itemSince = false; }
            }
            else if (element.Visibility == Visibility.Visible)
            {
                any = true; itemSince = true;
                if (element is MenuItem sub && sub.HasItems) TidySeparators(sub);
            }
        }
        if (previous != null && !itemSince) { previous.Visibility = Visibility.Collapsed; _hiddenByCustomization.Add(previous); }
    }

    private List<MenuNode> BuildMenuTree(ItemsControl menu, List<string> path)
    {
        var nodes = new List<MenuNode>();
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            string header = HeaderOf(item);
            if (header.Length == 0) continue;
            var here = new List<string>(path) { Localizer.SourceOf(item, header) };
            string key = MenuCustomization.Key(here);
            nodes.Add(new MenuNode(key, Localizer.T(MenuCustomization.Stable(Localizer.SourceOf(item, header))), MenuCustomization.IsProtected(key), BuildMenuTree(item, here)));
        }
        return nodes;
    }

    /// <summary>Opens the window where entries of the right-click menu can be switched on and off.</summary>
    public void CustomizeMenu()
    {
        // Restore everything first so the tree lists every entry, hidden or not.
        foreach (var element in _hiddenByCustomization) element.Visibility = Visibility.Visible;
        _hiddenByCustomization.Clear();
        var window = new MenuCustomizationWindow(this, BuildMenuTree(MainContextMenu, []), MenuCustomization.Sanitize(_runtimeSettings.HiddenMenuItems));
        if (window.ShowDialog() == true)
        {
            var chosen = window.Hidden;
            ApplyQuickSetting(settings => settings.HiddenMenuItems = chosen);
        }
        ApplyMenuCustomization();
    }

    private void CtxCustomizeMenu_Click(object sender, RoutedEventArgs e) => CustomizeMenu();
}
