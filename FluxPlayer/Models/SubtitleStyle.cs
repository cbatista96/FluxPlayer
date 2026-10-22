using System.Globalization;

namespace FluxPlayer.Models;

public sealed record SubtitleStyle(string Font = "Segoe UI", int FontSize = 0, string Color = "#FFFFFF",
    string OutlineColor = "#000000", int OutlineThickness = 2, bool Bold = false, bool Shadow = true, int VerticalMargin = 16)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Font) || Font.Length > 120 || Font.Any(char.IsControl))
            throw new ArgumentException("Use a font family name of 1–120 characters.");
        if (FontSize is < 0 or > 144 || OutlineThickness is < 0 or > 10 || VerticalMargin is < 0 or > 500)
            throw new ArgumentException("Use font size 0–144, outline 0–10 and margin 0–500.");
        _ = Rgb(Color); _ = Rgb(OutlineColor);
    }

    public static int Rgb(string color)
    {
        if (color is not { Length: 7 } || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rgb))
            throw new ArgumentException("Use colors in #RRGGBB format.");
        return rgb;
    }

    public static SubtitleStyle Capture(AppSettings settings) => new(settings.SubtitleFont, settings.SubtitleFontSize,
        settings.SubtitleColor, settings.SubtitleOutlineColor, settings.SubtitleOutlineThickness,
        settings.SubtitleBoldText, settings.ApplyShadowToSubtitle, settings.SubtitleVerticalMargin);

    public void Apply(AppSettings settings)
    {
        Validate(); settings.SubtitleFont = Font.Trim(); settings.SubtitleFontSize = FontSize;
        settings.SubtitleColor = Color.ToUpperInvariant(); settings.SubtitleOutlineColor = OutlineColor.ToUpperInvariant();
        settings.SubtitleOutlineThickness = OutlineThickness; settings.SubtitleBoldText = Bold;
        settings.ApplyShadowToSubtitle = Shadow; settings.SubtitleVerticalMargin = VerticalMargin;
    }

    public string[] NativeOptions()
    {
        Validate();
        return new[]
        {
            ":freetype-font=" + Font.Trim(),
            ":freetype-fontsize=" + FontSize.ToString(CultureInfo.InvariantCulture),
            ":freetype-color=" + Rgb(Color).ToString(CultureInfo.InvariantCulture),
            ":freetype-outline-color=" + Rgb(OutlineColor).ToString(CultureInfo.InvariantCulture),
            ":freetype-outline-thickness=" + OutlineThickness.ToString(CultureInfo.InvariantCulture),
            Bold ? ":freetype-bold" : ":no-freetype-bold",
            ":freetype-shadow-opacity=" + (Shadow ? "128" : "0"),
            ":sub-margin=" + VerticalMargin.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static SubtitleStyle Normalize(SubtitleStyle? style)
    {
        style ??= new();
        string font = !string.IsNullOrWhiteSpace(style.Font) && style.Font.Length <= 120 && !style.Font.Any(char.IsControl) ? style.Font.Trim() : "Segoe UI";
        string color = "#FFFFFF", outline = "#000000";
        try { _ = Rgb(style.Color); color = style.Color.ToUpperInvariant(); } catch (ArgumentException) { }
        try { _ = Rgb(style.OutlineColor); outline = style.OutlineColor.ToUpperInvariant(); } catch (ArgumentException) { }
        return style with
        {
            Font = font,
            FontSize = Math.Clamp(style.FontSize, 0, 144),
            Color = color,
            OutlineColor = outline,
            OutlineThickness = Math.Clamp(style.OutlineThickness, 0, 10),
            VerticalMargin = Math.Clamp(style.VerticalMargin, 0, 500)
        };
    }
}
