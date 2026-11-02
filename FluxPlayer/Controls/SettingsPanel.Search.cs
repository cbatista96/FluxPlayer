using FluxPlayer.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FluxPlayer.Controls;

/// <summary>Search across every Settings page: finds options by their title or description and jumps to them.</summary>
public partial class SettingsPanel
{
    private sealed record SearchEntry(int Page, TextBlock Title, string Text);

    private List<SearchEntry>? _searchIndex;
    private string? _searchLanguage;

    /// <summary>All option titles of all pages with their descriptions; rebuilt when the interface language changes.</summary>
    private List<SearchEntry> SearchIndex()
    {
        if (_searchIndex != null && _searchLanguage == Localizer.Current) return _searchIndex;
        var entries = new List<SearchEntry>();
        var title = (Style)FindResource("OptTitle"); var sub = (Style)FindResource("OptSub");
        for (int page = 0; page < _pages.Length; page++) Collect(_pages[page], page);
        void Collect(DependencyObject node, int pageIndex)
        {
            var children = LogicalTreeHelper.GetChildren(node).OfType<object>().ToList();
            foreach (var child in children)
            {
                if (child is TextBlock block && ReferenceEquals(block.Style, title) && !string.IsNullOrWhiteSpace(block.Text))
                {
                    string description = children.OfType<TextBlock>().FirstOrDefault(other => ReferenceEquals(other.Style, sub))?.Text ?? "";
                    entries.Add(new SearchEntry(pageIndex, block, block.Text + " " + description));
                }
                else if (child is DependencyObject deeper) Collect(deeper, pageIndex);
            }
        }
        _searchLanguage = Localizer.Current;
        return _searchIndex = entries;
    }

    private static bool ContainsAll(string text, string[] words) =>
        words.All(word => CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string[] words = SettingsSearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) { SettingsSearchPopup.IsOpen = false; return; }
        var matches = SearchIndex().Where(entry => ContainsAll(entry.Text, words)).Take(14).ToList();
        SettingsSearchResults.Items.Clear();
        foreach (var match in matches)
        {
            string page = (CategoryList.Items[match.Page] as ListBoxItem)?.Content?.ToString() ?? "";
            SettingsSearchResults.Items.Add(new ListBoxItem { Content = $"{page} › {match.Title.Text}", Tag = match, ToolTip = match.Text });
        }
        if (matches.Count == 0) SettingsSearchResults.Items.Add(new ListBoxItem { Content = Localizer.T("No settings found"), IsEnabled = false });
        SettingsSearchPopup.IsOpen = true;
    }

    private void SettingsSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SettingsSearchPopup.IsOpen = false; return; }
        if (e.Key == Key.Down && SettingsSearchResults.Items.Count > 0) { SettingsSearchResults.SelectedIndex = Math.Min(SettingsSearchResults.Items.Count - 1, SettingsSearchResults.SelectedIndex + 1); e.Handled = true; }
        else if (e.Key == Key.Up && SettingsSearchResults.Items.Count > 0) { SettingsSearchResults.SelectedIndex = Math.Max(0, SettingsSearchResults.SelectedIndex - 1); e.Handled = true; }
        else if (e.Key == Key.Enter) { Open(SettingsSearchResults.SelectedItem as ListBoxItem ?? SettingsSearchResults.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.IsEnabled)); e.Handled = true; }
    }

    private void SettingsSearchResults_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SettingsSearchResults, source) is ListBoxItem item) Open(item);
    }

    private System.ComponentModel.ICollectionView HotkeyView => System.Windows.Data.CollectionViewSource.GetDefaultView(_hotkeyItems);

    private void HotkeyFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string[] words = HotkeyFilterBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        HotkeyView.Filter = words.Length == 0 ? null : item => item is HotkeyItem hotkey && ContainsAll(Localizer.T(hotkey.DisplayName) + " " + hotkey.KeyDisplay, words);
    }

    private void HotkeySortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeySortCombo == null || !IsLoaded || HotkeyView is not System.Windows.Data.ListCollectionView view) return;
        string mode = GetComboTag(HotkeySortCombo) ?? "Default";
        view.CustomSort = mode switch
        {
            "Name" => new HotkeyComparer((a, b) => string.Compare(Localizer.T(a.DisplayName), Localizer.T(b.DisplayName), StringComparison.CurrentCultureIgnoreCase)),
            "Shortcut" => new HotkeyComparer((a, b) => string.Compare(a.KeyDisplay, b.KeyDisplay, StringComparison.CurrentCultureIgnoreCase)),
            "Assigned" => new HotkeyComparer((a, b) => b.IsAssigned.CompareTo(a.IsAssigned)),
            _ => null
        };
    }

    private sealed class HotkeyComparer(Comparison<HotkeyItem> by) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => x is HotkeyItem a && y is HotkeyItem b ? by(a, b) : 0;
    }

    private void CustomizeMenu_Click(object sender, RoutedEventArgs e) => Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()?.CustomizeMenu();

    private void Open(ListBoxItem? item)
    {
        if (item?.Tag is not SearchEntry entry) return;
        SettingsSearchPopup.IsOpen = false;
        CategoryList.SelectedIndex = entry.Page;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => entry.Title.BringIntoView());
    }
}
