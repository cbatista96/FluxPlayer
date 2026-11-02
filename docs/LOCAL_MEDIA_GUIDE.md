# Photos, ZIP media, device exports and display patterns

The [batch checklist](LOCAL_MEDIA_BATCH.md) tracks eight local-media capabilities. All controls reuse the existing Fluent resources and Mica window chrome. No startup photo scan, archive extraction, transfer worker or pattern animation is created.

## Settings locations

| Category | Action |
|---|---|
| Files and tags → Archives | Open ZIP media |
| Library → Photos | Photo library and slideshows |
| Audio → Device export | Export music to a device folder |
| Video → Display patterns | Display calibration patterns; White Wash |

The player context menu also includes the photo-library shortcut among its Library actions. Photo-viewer controls remain within the dedicated viewer.

## Open ZIP media

Inspect a local ZIP, review its recognized audio/video entries, choose an output folder and extract selected rows or all preview rows (up to 500). The player publishes copies into a new uniquely named subfolder and adds those paths to its queue. Playback is opt-in. Keep that subfolder while using the queue; this workflow does not create a transient ZIP stream or delete extracted files automatically.

Entry paths never determine extraction directories: publication uses generated, flattened filenames. Traversal, rooted/drive paths, duplicates and Unix symbolic-link entries are refused. Inspection checks the bounded end record before materializing entries, rejects split/ZIP64 archives, limits the central directory to 8 MiB and bounds the archive to 2 GiB, 5,000 entries, 2 GiB declared expanded total, 1 GiB per entry and 200:1 expansion for entries above 1 MiB. Selected bytes are counted while copying, the archive stamp is rechecked and the new folder is published only after extraction succeeds. Canceled/failed jobs remove their own staging directory. Archives requiring encryption, unsupported compression and other archive types remain unsupported. Existing archives, media and destination files are preserved.

## Photo albums and viewer

Choose photos or explicitly scan a folder to build an album. Folder scans include supported image paths, skip reparse points, stop at 50,000 visited files / 5,000 photos and run off the UI thread. Filter by filename or folder, save a named album and reload it later. Reusing a name replaces that album's path list. The profile stores up to 100 albums in `photo-albums.json`; media bytes are never rewritten. Missing paths stay visible and report an error when opened.

Open selected rows or all filtered photos. JPEG, PNG, BMP, GIF first frames and TIFF first pages use installed WPF decoders. Other formats, animated GIF playback and complete embedded color/HDR validation are outside this capability. Each input is limited to 64 MiB and 40 megapixels; displayed decoding bounds the longer edge to 2,560 pixels, freezes the result and closes its source stream. The viewer holds one displayed image and cancels superseded loads. Some native codecs may use additional temporary decode memory; this is not a universal memory-cost guarantee. The approach follows Microsoft's [BitmapImage guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-use-a-bitmapimage).

Use Previous/Next or arrow keys, Space to play/pause a slideshow, intervals of 2/5/10/30 seconds, R for quarter turns, Fit, Zoom +/− or Ctrl+wheel for 1–8× zoom. Scrollbars pan the enlarged image. Fit/zoom are relative to the bounded displayed image, not a promise of source-pixel inspection. F toggles fullscreen and Esc returns from it. Rotation and zoom reset on source change. Minimize pauses the slideshow, closing stops its timer/cancels loading, and a failed image pauses automatic advance. No original image is modified.

## Music export to a device folder

Choose up to 500 distinct local audio sources, a Windows-accessible destination folder and either Keep original files or an existing conversion profile. Preview captures source identities and reserves distinct destination names. Identical filenames and existing outputs receive suffixes. Export selected rows or all preview rows; completion invalidates the old preview.

Copy mode preserves source bytes. Conversion reuses the existing optional installed-FFmpeg service and encoder presets. Work runs sequentially with bounded buffers/encoder threads, rechecks source stamps, stages new files and publishes without replacement. Cancel stops further work and removes unpublished temporary output; successfully published earlier copies remain. An unplugged drive, changed source or new conflicting destination reports an error and requires a fresh preview. Source files and existing device files are preserved.

This is one-way folder export. It does not implement MTP-only devices, automatic deletion, bidirectional synchronization or matching an entire device to a source library. The original synchronization requirement R0218 therefore remains partial even though reviewed folder copies are available. Conversion during export fulfills its separate bounded capability.

## Display patterns

Choose static SDR color bars, 21 grayscale steps, near-black steps, near-white steps, a geometry grid/circle or White Wash. F hides controls/chrome and maximizes the pattern; Esc returns, and Left/Right changes patterns. Patterns are frozen vectors regenerated on size/selection changes, with no continuous animation or analysis timer.

White Wash is a static full-white field. These patterns help inspect a display, but do not measure luminance, certify calibration, repair panel damage or change Windows display, HDR, ICC or monitor settings. Pixel tests verify nominal SDR values and geometry; a physical monitor/color-management matrix remains unmeasured.

## Verification

`FluxPlayer.Tests/LocalMediaChecks.cs` checks real ZIP extraction and native audio decoding, traversal/duplicate/link/expansion/entry-count limits, source-stamp rejection, actual copy bytes, destination conflicts, cancellation, root containment, two converted audio outputs, five image decoders, bounded portrait decoding, released file handles, album persistence/failed writes, corrupt images and rendered pattern pixels. Source media is generated locally; personal media is not used.

The new focused groups are selected with `--filter="Local media"`. Local results, full-suite evidence and visual review are recorded in [Testing](TESTING.md). No release, installer rebuild or GitHub publication is included.

The full checkpoint passed 89 groups; all six focused local-media groups pass after final ZIP/presentation refinements, including the added initial-arrangement pattern regression. See Testing for the earlier transient restoration timeout and its successful isolated/full reruns.

Desktop inspection used the exact local Release executable with an isolated synthetic profile. It verified categorized Library/Files/Audio/Video entry points, album loading, actual image display, 90-degree rotation, zoom/scrollbars, slideshow advance/pause, ZIP and device-export form layouts, and a monitor-sized White Wash field with no chrome. Right-arrow changes the fullscreen pattern; Escape restores the normal bounds and controls. The initial blank-pattern layout discovered during review was corrected to render from its arranged host and covered by the added native content-arrangement regression. No personal media was written or copied; desktop review did not execute device transfers, whose copy/conversion mutations are covered by generated-file tests. Full theme/DPI/multimonitor/hardware validation remains unmeasured.
