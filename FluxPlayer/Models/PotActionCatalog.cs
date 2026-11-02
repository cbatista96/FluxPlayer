namespace FluxPlayer.Models;

/// <summary>
/// Player actions that have a menu entry and a configurable shortcut, but no default key (so existing bindings never change).
/// The key is the action name used by Settings > Keyboard shortcuts and by the menu's <c>Tag="act:Name"</c>.
/// </summary>
public static class PotActionCatalog
{
    public static readonly (string Key, string DisplayName)[] All =
    [
        ("GoToTime", "Go to a specific time"), ("SeekStart", "Go to the start"), ("SeekMiddle", "Go to the middle"), ("SeekEnd", "Go to 30 s before the end"),
        ("Seek5Back", "5 s back"), ("Seek5Forward", "5 s forward"), ("Seek30Back", "30 s back"), ("Seek30Forward", "30 s forward"),
        ("Seek60Back", "1 min back"), ("Seek60Forward", "1 min forward"), ("Seek300Back", "5 min back"), ("Seek300Forward", "5 min forward"),
        ("SubtitleSyncPlus500", "Subtitle delay +0.5 s"), ("SubtitleSyncMinus500", "Subtitle delay -0.5 s"), ("SubtitleSyncPlus5000", "Subtitle delay +5 s"),
        ("SubtitleSyncMinus5000", "Subtitle delay -5 s"), ("SubtitleSyncPlus50000", "Subtitle delay +50 s"), ("SubtitleSyncMinus50000", "Subtitle delay -50 s"),
        ("SubtitleDelayReset", "Reset the subtitle delay"), ("ToggleSubtitles", "Show / hide subtitles"), ("CopySubtitle", "Copy the current subtitle"),
        ("BrightnessDown1", "Brightness -1%"), ("BrightnessUp1", "Brightness +1%"), ("ContrastDown1", "Contrast -1%"), ("ContrastUp1", "Contrast +1%"),
        ("SaturationDown1", "Saturation -1%"), ("SaturationUp1", "Saturation +1%"), ("HueDown1", "Hue -1%"), ("HueUp1", "Hue +1%"), ("ResetPicture", "Reset the picture"),
        ("CopyFrame", "Copy the current frame to the clipboard"), ("CycleAspect", "Next aspect ratio"), ("ToggleAlwaysOnTop", "Always on top"),
        ("DeleteToRecycleBin", "Move the current file to the Recycle Bin"), ("CopyFilePath", "Copy the file path"),
        ("SectionRepeatPrevious", "Previous repeat section"), ("SectionRepeatNext", "Next repeat section"), ("RepeatLastSeconds", "Repeat the last few seconds"),
    ];

    public static readonly string[] AspectCycle = ["Auto", "16:9", "16:10", "4:3", "5:4", "21:9", "1:1", "Stretch"];
}
