using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FluxPlayer.Services;

public sealed record InterfaceLanguage(string Id, string NativeName);

/// <summary>
/// Interface language catalog. English text is the key; a language without an entry falls back to English,
/// so a catalog can grow without touching the windows. Add a language by registering it in
/// <see cref="Available"/> and <see cref="Catalogs"/>.
/// </summary>
public static partial class Localizer
{
    public static readonly InterfaceLanguage[] Available = [new("English", "English"), new("Spanish", "Español")];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.Ordinal)
    {
        ["Spanish"] = Variants(SpanishCatalog)
    };

    private static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("Source", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty RenderedProperty = DependencyProperty.RegisterAttached("Rendered", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty TipSourceProperty = DependencyProperty.RegisterAttached("TipSource", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty TipRenderedProperty = DependencyProperty.RegisterAttached("TipRendered", typeof(string), typeof(Localizer));

    public static string Current { get; private set; } = "English";
    public static event EventHandler? Changed;

    public static string Normalize(string? id) =>
        Available.FirstOrDefault(language => string.Equals(language.Id, id, StringComparison.OrdinalIgnoreCase))?.Id ?? "English";

    public static void Use(string? id)
    {
        string next = Normalize(id);
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The English text an element was built with, even while it shows a translation (<paramref name="shown"/> when it was never translated).</summary>
    public static string SourceOf(DependencyObject element, string shown) => (string?)element.GetValue(SourceProperty) ?? shown;

    /// <summary>Translates a literal, keeping a dynamic tail such as " (+5 s)" or " — 4:12" intact.</summary>
    public static string T(string text)
    {
        if (Current == "English" || string.IsNullOrEmpty(text) || !Catalogs.TryGetValue(Current, out var catalog)) return text;
        if (catalog.TryGetValue(text, out var exact)) return exact;
        foreach (string separator in new[] { " (", " — " })
        {
            int at = text.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0 && catalog.TryGetValue(text[..at], out var head)) return head + text[at..];
        }
        return text;
    }

    /// <summary>
    /// Translates menu headers and visible labels below <paramref name="root"/>. The English original is remembered per
    /// element, so switching language repeatedly never translates a translation, and code that rewrites a label later is respected.
    /// </summary>
    public static void Apply(DependencyObject root)
    {
        Translate(root);
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node) Apply(node);
    }

    private static void Translate(DependencyObject node)
    {
        if (node is FrameworkElement { ToolTip: string tip } tipped)
            Render(tipped, tip, text => tipped.ToolTip = text, TipSourceProperty, TipRenderedProperty);
        switch (node)
        {
            case Window window when window.Title.Length > 0 && !BindingOperations.IsDataBound(window, Window.TitleProperty):
                Render(window, window.Title, text => window.Title = text);
                break;
            case MenuItem item when item.Header is string header:
                Render(item, header, text => item.Header = text);
                break;
            case TextBlock block when !BindingOperations.IsDataBound(block, TextBlock.TextProperty) && block.Inlines.Count <= 1:
                Render(block, block.Text, text => block.Text = text);
                break;
            // A combo item without a Tag carries its text as the stored value (e.g. equalizer presets), so it stays as written.
            case ComboBoxItem { Tag: null }:
                break;
            case ContentControl control when control.Content is string content
                && !BindingOperations.IsDataBound(control, ContentControl.ContentProperty):
                Render(control, content, text => control.Content = text);
                break;
        }
    }

    private static void Render(DependencyObject element, string current, Action<string> assign, DependencyProperty? sourceKey = null, DependencyProperty? renderedKey = null)
    {
        sourceKey ??= SourceProperty; renderedKey ??= RenderedProperty;
        string? source = (string?)element.GetValue(sourceKey);
        if (source == null || current != (string?)element.GetValue(renderedKey)) source = current;
        string rendered = T(source);
        element.SetValue(sourceKey, source);
        element.SetValue(renderedKey, rendered);
        if (rendered != current) assign(rendered);
    }

    // The same phrase is written with "..." in menus and "…" in dialogs.
    private static IReadOnlyDictionary<string, string> Variants(IReadOnlyDictionary<string, string> source)
    {
        var all = new Dictionary<string, string>(source, StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (key.EndsWith("...", StringComparison.Ordinal)) all.TryAdd(key[..^3] + "…", value.EndsWith("...", StringComparison.Ordinal) ? value[..^3] + "…" : value);
            else if (key.EndsWith('…')) all.TryAdd(key[..^1] + "...", value.EndsWith('…') ? value[..^1] + "..." : value);
        }
        return all;
    }
}
