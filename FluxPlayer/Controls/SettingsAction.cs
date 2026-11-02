using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Controls;

public enum SettingsCategory { Unassigned, Playback, Audio, Video, Subtitles, Library, Files, General, Keyboard, Mouse }
public sealed record SettingsActionEntry(MenuItem Item, SettingsCategory Category, string Section, bool QuickAccess);

/// <summary>Explicit placement shared by preferences and the curated playback shortcuts.</summary>
public static class SettingsAction
{
    public static readonly DependencyProperty CategoryProperty = DependencyProperty.RegisterAttached("Category", typeof(SettingsCategory), typeof(SettingsAction), new PropertyMetadata(SettingsCategory.Unassigned));
    public static readonly DependencyProperty SectionProperty = DependencyProperty.RegisterAttached("Section", typeof(string), typeof(SettingsAction), new PropertyMetadata(""));
    public static readonly DependencyProperty QuickAccessProperty = DependencyProperty.RegisterAttached("QuickAccess", typeof(bool), typeof(SettingsAction), new PropertyMetadata(false));
    public static void SetCategory(DependencyObject item, SettingsCategory value) => item.SetValue(CategoryProperty, value);
    public static SettingsCategory GetCategory(DependencyObject item) => (SettingsCategory)item.GetValue(CategoryProperty);
    public static void SetSection(DependencyObject item, string value) => item.SetValue(SectionProperty, value);
    public static string GetSection(DependencyObject item) => (string)item.GetValue(SectionProperty);
    public static void SetQuickAccess(DependencyObject item, bool value) => item.SetValue(QuickAccessProperty, value);
    public static bool GetQuickAccess(DependencyObject item) => (bool)item.GetValue(QuickAccessProperty);
    public static SettingsActionEntry[] Collect(ContextMenu source) => source.Items.OfType<MenuItem>().Select(item =>
    {
        var category = GetCategory(item); string section = GetSection(item);
        if (category == SettingsCategory.Unassigned || string.IsNullOrWhiteSpace(section)) throw new InvalidOperationException($"Choose an explicit settings category and section for '{item.Header}'.");
        return new SettingsActionEntry(item, category, section, GetQuickAccess(item));
    }).ToArray();
    public static object Clone(object source, Action<MenuItem> invoke)
    {
        if (source is not MenuItem item) return new Separator();
        object? header = item.Header is TextBlock text ? new TextBlock { Text = text.Text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 } : item.Header;
        var clone = new MenuItem { Header = header, Tag = item.Tag, IsEnabled = item.IsEnabled, IsCheckable = item.IsCheckable, IsChecked = item.IsChecked, ToolTip = item.ToolTip, InputGestureText = item.InputGestureText };
        foreach (var child in item.Items) clone.Items.Add(Clone(child, invoke));
        if (!item.HasItems) clone.Click += (_, e) => { e.Handled = true; if (item.IsCheckable) item.IsChecked = clone.IsChecked; invoke(item); };
        return clone;
    }
}
