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
        ["Spanish"] = Variants(Merge(SpanishCatalog, SpanishWindows, SpanishWindows2, SpanishTools1, SpanishTools2, SpanishTools3, SpanishMessages1, SpanishMessages2, SpanishMessages3, SpanishMessages4))
    };

    private static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("Source", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty RenderedProperty = DependencyProperty.RegisterAttached("Rendered", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty TipSourceProperty = DependencyProperty.RegisterAttached("TipSource", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty TipRenderedProperty = DependencyProperty.RegisterAttached("TipRendered", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty BarSourceProperty = DependencyProperty.RegisterAttached("BarSource", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty BarRenderedProperty = DependencyProperty.RegisterAttached("BarRendered", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty HintSourceProperty = DependencyProperty.RegisterAttached("HintSource", typeof(string), typeof(Localizer));
    private static readonly DependencyProperty HintRenderedProperty = DependencyProperty.RegisterAttached("HintRendered", typeof(string), typeof(Localizer));

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

    private static readonly System.Text.RegularExpressions.Regex ValueTail = new(@"^(?<head>\D*?[^\d\s+\-−:])(?<sep>:?\s+)(?<tail>[+\-−]?\d.*)$", System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Translates a message that carries values, such as "Speed 2x", "Bookmark at 00:12:00" or "Channel routing: Stereo": the words are translated and the values kept.
    /// Text that is already translated, or has no known words, comes back unchanged.
    /// </summary>
    public static string Rich(string text)
    {
        if (Current == "English" || string.IsNullOrEmpty(text)) return text;
        string direct = T(text);
        if (direct != text) return direct;
        // A message may carry trailing spaces or a final period of its own; translate what is inside and keep the rest.
        string trimmed = text.TrimEnd();
        if (trimmed.Length > 0 && trimmed != text) return Rich(trimmed) + text[trimmed.Length..];
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0)
        {
            string head = T(text[..colon]);
            if (head != text[..colon]) return head + ": " + Rich(text[(colon + 2)..]);
        }
        try
        {
            var match = ValueTail.Match(text);
            if (match.Success)
            {
                string head = T(match.Groups["head"].Value);
                if (head != match.Groups["head"].Value) return head + match.Groups["sep"].Value + match.Groups["tail"].Value;
            }
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { }
        // Messages that start with fixed words followed by a value.
        if (Current == "Spanish")
            foreach (var (english, spanish) in SpanishPrefixes)
                if (text.StartsWith(english, StringComparison.Ordinal)) return spanish + text[english.Length..];
        return text;
    }

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
        if (root is Window window) TranslateTitleBars(window);
    }

    // The Fluent title bar lives in the window template, so the logical walk above never reaches it.
    private static void TranslateTitleBars(DependencyObject node)
    {
        int count = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(node) : 0;
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
            if (child is Wpf.Ui.Controls.TitleBar { Title.Length: > 0 } bar && !BindingOperations.IsDataBound(bar, Wpf.Ui.Controls.TitleBar.TitleProperty))
                Render(bar, bar.Title, text => bar.Title = text, BarSourceProperty, BarRenderedProperty);
            TranslateTitleBars(child);
        }
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
            case Wpf.Ui.Controls.TextBox { PlaceholderText.Length: > 0 } box when !BindingOperations.IsDataBound(box, Wpf.Ui.Controls.TextBox.PlaceholderTextProperty):
                Render(box, box.PlaceholderText, text => box.PlaceholderText = text, HintSourceProperty, HintRenderedProperty);
                break;
            case DataGrid grid:
                foreach (var column in grid.Columns)
                    if (column.Header is string columnHeader && columnHeader.Length > 0)
                        Render(column, columnHeader, text => column.Header = text);
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

    private static IReadOnlyDictionary<string, string> Merge(params IReadOnlyDictionary<string, string>[] parts)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts) foreach (var (key, value) in part) all[key] = value;
        return all;
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
