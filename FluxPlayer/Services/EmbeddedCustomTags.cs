using FluxPlayer.Models;
using System.Collections.Immutable;

namespace FluxPlayer.Services;

/// <summary>Explicit namespaced user text fields for MP3 ID3v2 and FLAC/Ogg Vorbis comments.</summary>
public static class EmbeddedCustomTags
{
    private static object? Container(TagLib.File file, bool create)
    {
        string extension = System.IO.Path.GetExtension(file.Name).ToLowerInvariant();
        return extension switch
        {
            ".mp3" => file.GetTag(TagLib.TagTypes.Id3v2, create),
            ".flac" or ".ogg" or ".oga" or ".opus" => file.GetTag(TagLib.TagTypes.Xiph, create),
            _ => null
        };
    }
    public static bool Supports(TagLib.File file) => System.IO.Path.GetExtension(file.Name).ToLowerInvariant() is ".mp3" or ".flac" or ".ogg" or ".oga" or ".opus";
    public static ImmutableDictionary<string, string> Read(TagLib.File file)
    {
        var values = ImmutableDictionary.CreateBuilder<string, string>();
        switch (Container(file, false))
        {
            case TagLib.Ogg.XiphComment xiph:
                foreach (string key in xiph.Where(key => key.StartsWith(CustomTagFields.Prefix, StringComparison.Ordinal)))
                    values[key[CustomTagFields.Prefix.Length..]] = string.Join("; ", xiph.GetField(key));
                break;
            case TagLib.Id3v2.Tag id3:
                foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>().Where(frame => frame.Description.StartsWith(CustomTagFields.Prefix, StringComparison.Ordinal)))
                    values[frame.Description[CustomTagFields.Prefix.Length..]] = string.Join("; ", frame.Text);
                break;
        }
        return values.ToImmutable();
    }
    public static void Apply(TagLib.File file, IReadOnlyDictionary<string, string> values)
    {
        CustomTagFields.Validate(values);
        switch (Container(file, true))
        {
            case TagLib.Ogg.XiphComment xiph:
                foreach (var pair in values)
                    if (pair.Value.Length == 0) xiph.RemoveField(CustomTagFields.Prefix + pair.Key);
                    else xiph.SetField(CustomTagFields.Prefix + pair.Key, new[] { pair.Value });
                break;
            case TagLib.Id3v2.Tag id3:
                foreach (var pair in values)
                {
                    var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3, CustomTagFields.Prefix + pair.Key, TagLib.StringType.UTF8, pair.Value.Length > 0);
                    if (frame == null) continue;
                    if (pair.Value.Length == 0) id3.RemoveFrame(frame); else frame.Text = new[] { pair.Value };
                }
                break;
            default: throw new NotSupportedException("Custom embedded fields support MP3, FLAC, Ogg Vorbis and Opus only.");
        }
    }
}
