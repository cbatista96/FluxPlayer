using FluxPlayer.Services;
using System.Windows;
using System.Windows.Controls;

namespace FluxPlayer.Windows;

/// <summary>A TextBlock whose text is translated as it is set, line by line, so the status messages of the tool windows follow the interface language.</summary>
public sealed class LocalizedStatusText : TextBlock
{
    static LocalizedStatusText() =>
        TextProperty.OverrideMetadata(typeof(LocalizedStatusText), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, null, (_, value) => Translate(value)));

    private static object Translate(object value) =>
        value is string text && Localizer.Current != "English" ? string.Join("\n", text.Split('\n').Select(line => Localizer.Rich(line))) : value;
}
