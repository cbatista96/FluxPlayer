# Architecture and performance

FluxPlayer is a Windows x64 WPF application using CommunityToolkit.Mvvm, dependency injection and WPF UI controls. LibVLCSharp wraps a shared native LibVLC playback instance. Optional conversion uses a separate FFmpeg process only for an explicit user job.

## Responsibilities

| Component | Responsibility |
|---|---|
| `MainWindow` and partial files | Window state, video surface, input, menus and audio/video layout transitions. |
| `MainViewModel` and audio partial | Observable playback state, playlist operations and workspace metadata. |
| `MediaService` and audio partial | Native playback lifetime, track detection, settings, metadata, chapters and devices. |
| `VlcWpfRenderer` | Shared-memory video callbacks and WPF `InteropBitmap` presentation. |
| `LibraryService` / `LibraryQuery` | Persistent local index, folder watching, smart playlists and filtering. |
| `PlaylistService` | Playlist parsing and export without UI dependencies. |
| `ConversionService` / `ClipExportService` | Explicit conversion and clip export jobs. |
| `BatchTagService` / `MetadataPatch` | Bounded, sequential tag jobs; null preserves a field and an empty string clears it. |
| `SettingsService` / `FeatureCatalog` | Persistence, migrations, normalization and implemented/planned feature status. |
| `ThumbnailService` | On-demand video preview generation. |

## Native callback lifetime

LibVLC callbacks may run on native event threads. Do not call back into the player synchronously from those callbacks or dispose native media there. Metadata parsing uses a shared semaphore. After an awaited parse completes, `Task.Yield()` moves subsequent reads and disposal out of the inline native completion callback. Service disposal waits for active metadata parsing before releasing LibVLC.

UI changes are dispatched to WPF. A–B seeks and stop operations that might reenter LibVLC must also leave the native callback thread first. Preserve these boundaries when refactoring asynchronous code.

## Video presentation

VLC writes RV32 frames into a Windows shared-memory section. WPF presents that section as an opaque Bgr32 `InteropBitmap`. Pitch and line count are aligned for the native converter. Callback delegates remain rooted for the playback lifetime.

Display notifications are coalesced so at most one WPF invalidation is queued. Replacing a format defers retirement of the old section to allow WPF composition to finish. The mini player mirrors the existing bitmap; it does not create another decoder. Ambient light samples a small grid from the current section rather than copying a full frame.

This presentation path avoids full-frame managed copies on the UI dispatcher. Its actual cost still depends on resolution, decoding, memory bandwidth, WPF composition and hardware. Hardware decoding is requested where configured; the complete display path is not claimed to be a universal GPU zero-copy pipeline.

## Startup and background work

- Load library state lazily. File enumeration and batch construction run outside the dispatcher.
- Bound initial playlist duration parsing to a small batch and skip the current playback item. Do not predecode the entire library at startup.
- Resolve actual audio/video tracks after opening; use filename extensions only as a provisional hint.
- Generate thumbnails on demand and keep expensive optional tools outside normal transport events.
- Keep FFmpeg conversion sequential, use bounded error capture and drain both redirected process pipes.
- Use tiered PGO for regular builds and ReadyToRun in the local publish profile. Trimming remains disabled because of WPF and native/reflection dependencies.
- The packaging script prepares the VLC plugin cache when possible to avoid repeated plugin scans.

These are implementation choices, not a guarantee of zero performance cost. Compare representative workloads before and after changes: cold/warm startup, time to first audio/frame, idle CPU, playback CPU, memory, seek responsiveness and large-library interaction. Record the machine and media details with measurements.

## Snapshot jobs

Capture requests are serialized and run outside the UI dispatcher. LibVLC 3 forces native snapshots to PNG, so JPEG/BMP output is explicitly encoded after the native completion event; changing a filename extension alone is insufficient. See the [upstream implementation](https://github.com/videolan/vlc/blob/3.0.21/lib/video.c#L136). Completion continuations run asynchronously, temporary files are cleaned up, and publication refuses to replace an existing image. Engine disposal cancels pending work before releasing native objects.

Scheduled captures are explicit bounded jobs. Each attempt completes before another delay starts, so slow encoding cannot accumulate queued requests. Paused, audio-only and busy playback are skipped. A schedule has no timer until the user starts it and ends when its image limit is reached or its window closes.

## Data and compatibility

Subtitle track additions, removals and selection events only enqueue a coalesced dispatcher refresh; native callbacks never query the player directly. Collection replacement suppresses selection write-back, preserving the actual native selection and a single Disabled entry. External subtitle paths are converted to absolute file URIs before `AddSlave`, which requires a scheme.

The subtitle timing editor transforms text cues on demand using decimal scale/offset arithmetic. Parsing and SRT output are bounded to 16 MB and 100,000 cues. Exports finish in a temporary file before publication, and the editor requires a different path from the original. Live native delay and exported timing are independent; loading an exported copy clears live delay. This tool does not introduce a per-frame scan or idle timer.

TagLibSharp is used only by explicit tag edits, without decoding media properties. Each save finishes before cancellation is honored and reopens the file to verify selected fields. This avoids the empty-field behavior of LibVLC 3's [metadata writer](https://github.com/videolan/vlc/blob/3.0.21/modules/meta_engine/taglib.cpp#L1172). Batch editing skips sources loaded in the player, processes one file at a time and reports each result. Successful writes refresh indexed metadata and file stamps, with one library notification at job completion. Editing does not add a playback filter or background scanner.

Settings and library files belong to the current Windows user. Write changes through the service layer and preserve migration paths. Legacy preset names and query aliases remain accepted as compatibility data even though the public interface and documentation are English.

The full competitor research and private working reports are not distribution inputs. Public capability status is maintained in `FEATURES.md` and `FeatureCatalog`; unimplemented catalog entries must remain visibly planned.
