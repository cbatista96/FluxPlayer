# Architecture and performance

FluxPlayer is a Windows x64 WPF application using CommunityToolkit.Mvvm, dependency injection and WPF UI controls. LibVLCSharp wraps a shared native LibVLC playback instance. Optional conversion uses a separate FFmpeg process only for an explicit user job.

## Responsibilities

| Component | Responsibility |
|---|---|
| `MainWindow` and partial files | Window state, video surface, input, menus and audio/video layout transitions. |
| `MainViewModel` and audio partial | Observable playback state, playlist operations and workspace metadata. |
| `MediaService` and audio partial | Native playback lifetime, track detection, settings, metadata, chapters and devices. |
| `Native/FluxDsp` / `NativeDspBridge` | Independent native 15/20/31-band audio filters and bounded shared-memory configuration. |
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

The format callback reports the original source FourCC before requesting RV32. VLC 3 can attempt to blend YUVA subtitles into an opaque DX11 surface before conversion, which fails. Only a detected Windows opaque surface with an active subtitle triggers the bounded software-decoder transition. Sources that already deliver CPU-compatible frames do not restart. The transition retains position, pause and up to 16 explicitly loaded subtitle sources; Stop and a new source cancel pending restoration. Disposal cancels and drains the operation before native objects are released. Recovery does not reintroduce a hardware profile on that source after subtitle compatibility fallback. A GPU-composed presentation path remains unimplemented.

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

The legacy ten-band VLC equalizer applies a fixed factor of 0.25 to its dry signal. The service offsets that attenuation by `20 * log10(4)` dB, so a flat displayed 0 dB matches bypass. Effective requested preamp is capped before conversion to the native ±20 dB range. ISO centers are retained, and equalizer updates run only on user changes or source initialization. See the [upstream filter](https://github.com/videolan/vlc/blob/3.0.21/modules/audio_filter/equalizer.c#L130).

Extended layouts use a C plugin built against the pinned VLC 3.0.21 SDK. It advertises the `equalizer` shortcut at higher priority and accepts only an enabled, valid 15/20/31-band configuration; the original module handles ten-band mode. Fifteen ISO centers use two-thirds-octave spacing; the 31-band layout uses one-third-octave spacing. The service forces module reselection only across the legacy/extended boundary. Changes within extended processing update a local 1 KiB memory map, using an odd/even sequence counter and memory barriers. Unchanged settings are deduplicated. A reader encountering an incomplete update retains its current coefficients instead of waiting. Native status acknowledges the applied sequence, rate, channels and effective preamp; readiness is distinct from active processing.

The filter processes interleaved FL32 in place with cascaded peaking biquads, using double-precision coefficients and history. Each nonzero band is independent; zero-gain sections are skipped and a flat, unity-gain block returns without a sample loop. Fixed history arrays support up to 31 sections and 32 channels; accepted rates are 8–768 kHz, with centers near/above Nyquist omitted. Coefficients use the peaking-filter form described by the [Audio EQ Cookbook](https://www.w3.org/TR/audio-eq-cookbook/#formulae), with fixed Q derived from each layout's octave spacing. This is a graphic EQ, not an arbitrary frequency/Q editor.

On configuration changes, old and new filter histories crossfade for 256 frames and the preamp ramps over a block. Flush clears history. Headroom estimation evaluates the combined response at 512 logarithmic frequencies plus the exact centers, then adds 0.25 dB where gain is present. It is not a true-peak limiter. These calculations run only on changes; the audio path performs no managed callback, waiting, heap allocation or file I/O. Mapping and filter storage are allocated when opening and released on close. The managed map outlives native engine disposal. The module frees VLC-owned strings through `libvlc_free` to preserve allocator ownership across C runtimes.

See [Native DSP measurements](NATIVE_DSP.md) for bounded response and processing-cost evidence. These measurements do not establish zero startup/playback overhead or a complete hardware matrix.

Per-media `audio-filter` options were measured to leave a compressor test signal unchanged in this pipeline. The previous normalization/compressor switches are therefore visibly planned rather than claimed as implemented. The [native equalizer setter](https://github.com/videolan/vlc/blob/3.0.21/lib/media_player.c#L2004) also replaces the audio-filter chain. A verified combined DSP chain remains pending; future work must test processed samples rather than only option persistence. Conversion normalization uses a separate FFmpeg path.

Channel routing uses the native player's `stereo-mode` setter, with values from the pinned SDK. Mono (7) and Original (0) are passed through LibVLCSharp's enum even though that older managed enum omits their names. See the [native setter](https://github.com/videolan/vlc/blob/3.0.21/lib/audio.c#L461) and [channel preparation](https://github.com/videolan/vlc/blob/3.0.21/src/audio_output/output.c#L375). Native Playing can precede decoded output initialization, so a bounded deferred pass checks at 100, 350 and 1,000 ms after a playback/settings/output change. It only reapplies a differing mode; Original gets one deferred application because it may resolve to a source-specific value. New requests supersede old revisions, and source changes, Stop and disposal invalidate stale work. Native callbacks only schedule this work. There is no recurring idle poll or managed sample processing in the application.

Subtitle track additions, removals and selection events only enqueue a coalesced dispatcher refresh; native callbacks never query the player directly. Collection replacement suppresses selection write-back, preserving the actual native selection and a single Disabled entry. External subtitle paths are converted to absolute file URIs before `AddSlave`, which requires a scheme.

Native default text styles are applied to the LibVLC instance at startup, matching the renderer's variable inheritance. Applying freetype options to individual media objects was verified to leave the default rendering unchanged in this pipeline. Style changes are therefore persisted for the next player start. Named styles are validated and bounded to 32 entries. No additional text renderer, per-frame managed styling or profile polling is introduced. See the [upstream FreeType options](https://github.com/videolan/vlc/blob/3.0.21/modules/text_renderer/freetype/freetype.c#L141) and [VLC 3 composition order](https://github.com/videolan/vlc/blob/3.0.21/src/video_output/video_output.c#L930).

The subtitle timing editor transforms text cues on demand using decimal scale/offset arithmetic. Parsing and SRT output are bounded to 16 MB and 100,000 cues. Exports finish in a temporary file before publication, and the editor requires a different path from the original. Live native delay and exported timing are independent; loading an exported copy clears live delay. This tool does not introduce a per-frame scan or idle timer.

TagLibSharp is used only by explicit tag edits, without decoding media properties. Each save finishes before cancellation is honored and reopens the file to verify selected fields. This avoids the empty-field behavior of LibVLC 3's [metadata writer](https://github.com/videolan/vlc/blob/3.0.21/modules/meta_engine/taglib.cpp#L1172). Batch editing skips sources loaded in the player, processes one file at a time and reports each result. Successful writes refresh indexed metadata and file stamps, with one library notification at job completion. Editing does not add a playback filter or background scanner.

Settings and library files belong to the current Windows user. Write changes through the service layer and preserve migration paths. Legacy preset names and query aliases remain accepted as compatibility data even though the public interface and documentation are English.

Personal library labels use immutable arrays, bounded to 32 names of 64 characters per entry. Explicit batch edits validate the complete selection before mutation, serialize under the index gate on a worker, and publish through a unique temporary file. Failure restores the prior records; cancellation is observed before commit. Backup label unions are preflighted before any imported entry changes. Queries compile exact label tests once per search; no label scan is added to playback or idle processing.

External navigation chapters are parsed only on explicit request. Input is bounded to 4 MiB, 10,000 chapter atoms and XML depth 32; XML resolvers are disabled and declarations control byte decoding. A draft must pass complete time/title/duration validation before replacing session navigation. Copy exports use a unique temporary file and refuse to replace an existing destination. The format scope follows [MKVToolNix's simple and XML chapter documentation](https://mkvtoolnix.download/doc/mkvmerge.html#mkvmerge.chapters); ordered/linked playback and container metadata edits remain pending. The menu creates at most 200 items and sends larger lists to the virtualized editor, avoiding unbounded menu construction.

The full competitor research and private working reports are not distribution inputs. Public capability status is maintained in `FEATURES.md` and `FeatureCatalog`; unimplemented catalog entries must remain visibly planned.
