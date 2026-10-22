# Feature checklist

**78 implemented · 26 pending · 104 tracked capabilities.**

Each capability appears exactly once. Completing it moves the same stable ID from Pending to Implemented. A setting or catalog entry alone does not complete a feature. Source references describe the implementation; validation limits are documented in [Testing](docs/TESTING.md).

Maintained in `docs/feature-catalog.json`. Regenerate with `pwsh -File tools/Sync-PublicFeatures.ps1`; CI rejects an outdated checklist.

## Implemented (78)

### Workspaces and interface

- [x] **F0001** Audio and video workspaces. — MainWindow, Controls and Windows
- [x] **F0002** Automatic mode selection using decoded audio/video tracks, with extension-based hints before parsing. — MainWindow, Controls and Windows
- [x] **F0003** Manual Automatic / Audio / Video preference. — MainWindow, Controls and Windows
- [x] **F0004** Separate saved audio window dimensions. — MainWindow, Controls and Windows
- [x] **F0005** Compact music layout with artwork, metadata, transport and playlist. — MainWindow, Controls and Windows
- [x] **F0006** English interface, dialogs, tooltips and accessibility names. — MainWindow, Controls and Windows
- [x] **F0007** Dark Mica, light Mica, black and acrylic themes with system-dependent materials. — MainWindow, Controls and Windows
- [x] **F0008** Fullscreen playback and automatic control hiding. — MainWindow, Controls and Windows
- [x] **F0009** Mini player using the same playback session. — MainWindow, Controls and Windows
- [x] **F0010** On-screen playback and volume feedback. — MainWindow, Controls and Windows
- [x] **F0011** Configurable keyboard shortcuts and mouse actions. — MainWindow, Controls and Windows
- [x] **F0012** Presentation control lock. — MainWindow, Controls and Windows
- [x] **F0013** Tray controls and system media transport integration. — MainWindow, Controls and Windows
- [x] **F0014** Sleep timer. — MainWindow, Controls and Windows
- [x] **F0094** Portable data folder selected by --portable or portable.flag. — AppProfile and startup services; isolated persistence tests and portable UI test.
- [x] **F0095** Independent named profiles selected at startup with --profile. — AppProfile / SettingsService / LibraryService; separate settings persistence tests.
- [x] **F0096** Explicit profile directory selected with --data-directory and reflected in Settings. — AppProfile / SettingsPanel; argument validation and option terminator tests.

### Playback and formats

- [x] **F0018** LibVLC playback of supported audio/video files and network sources. — MediaService, MediaFormats and MainViewModel
- [x] **F0019** File picker, drag and drop, folder loading and recent files. — MediaService, MediaFormats and MainViewModel
- [x] **F0020** Local/remote source detection, including UNC paths. — MediaService, MediaFormats and MainViewModel
- [x] **F0021** Play, pause, stop, seek, previous and next. — MediaService, MediaFormats and MainViewModel
- [x] **F0022** Playback speed, repeat and shuffle. — MediaService, MediaFormats and MainViewModel
- [x] **F0023** A–B loop and video frame advance. — MediaService, MediaFormats and MainViewModel
- [x] **F0024** Playback position persistence per file. — MediaService, MediaFormats and MainViewModel
- [x] **F0025** Configurable intro/outro skipping. — MediaService, MediaFormats and MainViewModel
- [x] **F0026** Queue a track to play next without reordering the playlist. — MediaService, MediaFormats and MainViewModel
- [x] **F0027** Adaptive caching and playback fallback profiles. — MediaService, MediaFormats and MainViewModel
- [x] **F0028** Plugin cache generation for local packaging. — MediaService, MediaFormats and MainViewModel
- [x] **F0029** Optional local engine diagnostics. — MediaService, MediaFormats and MainViewModel
- [x] **F0103** External OGM/Matroska XML navigation chapters, an editable/searchable timeline, session application and separate copy export. — ChapterDocument / ChapterEditorWindow / MainViewModel.Chapters; encoding and timing round trips, safe XML limits, overwrite refusal, native seek/reset/duration checks, MKV chapter offsets and manual edit/export/menu navigation.

### Audio

- [x] **F0033** Artwork and title/artist/album/genre metadata. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0034** Metadata editing through LibVLC for formats that support saving tags. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0035** Synchronized local LRC lyrics and plain text lyrics. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0036** CUE sheet chapters and native chapter navigation. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0037** Native ten-band equalizer with preamplifier and 30 presets, alongside optional extended layouts. — Native ten-band equalizer, presets and output-profile integration; captured PCM verifies flat bypass level and gain/compensation at 31.25 Hz and 1 kHz.
- [x] **F0038** Equalizer gain compensation; extended layouts estimate the combined filter response. — Native combined-response headroom and acknowledged effective preamp; captured PCM at 20 Hz, middle centers and 20 kHz, including 192/384 kHz inputs. This is not a peak limiter.
- [x] **F0039** Migration of presets saved by the earlier localized interface. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0040** ReplayGain track/album options, preamplification and peak protection. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0041** Output device selection, audio tracks, channel options and audio delay. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0043** Optional FFmpeg conversion to FLAC, WAV, MP3, Opus, Vorbis, AAC, ALAC and WavPack. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0044** Sequential conversion, progress, cancellation and optional loudness normalization. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0045** Conversion writes to a temporary file and refuses to overwrite an existing destination. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0046** Independent native 20-band logarithmic and 31-band ISO graphic equalizers, with requested gains and preamp within ±20 dB. — Native/FluxDsp, NativeDspBridge and dynamic EqualizerWindow; measured processed PCM, high-rate precision, live layout switching, persistence and visual panel review. Filter-only measurements: docs/NATIVE_DSP.md.
- [x] **F0047** Saved volume, full 10/20/31-band EQ layout, preamp, gain compensation and audio delay per output, with recall when selecting an output or starting the player. — AudioOutputProfile / MainViewModel.AudioProfiles / AudioOutputProfilesWindow; independent endpoint persistence, live native delay/volume, mute, boost limit, opt-out, startup, failure isolation and manual headphone-profile review. Full native layout/profile recall and unrelated-settings reload are covered by the multiband scenario.
- [x] **F0098** Create and edit CUE sheets with track titles, artists, sources, ordering and 75-fps start positions. — CueSheet.Write / CueSheetEditorWindow; frame and multi-file regression tests, plus manual edit/save verification with original preservation.

### Library and playlists

- [x] **F0052** Persistent local JSON index. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0053** Folder scanning outside the UI dispatcher. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0054** Watched folders with file change updates. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0055** Favorites, playback counts and last played dates. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0056** Search by text and fields, quoted phrases and exclusions. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0057** Format, genre, duration, size, play count and date rules. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0058** Saved smart playlists evaluated against the current library. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0059** Library JSON backup export and import. — LibraryService.ImportBackupAsync / Library tools: validated merge, preserved state and opt-in folder monitoring; regression tests and manual restore flow.
- [x] **F0060** M3U/M3U8, PLS and XSPF import/export. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0061** Unicode paths and relative playlist entries. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0062** Playlist reorder, clear, play-next queue and persistence. — MainViewModel / playlist services; restored selection skips missing files and Play starts the saved queue; native regression coverage.
- [x] **F0063** Bounded initial duration parsing rather than decoding the full library at startup. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0064** On-demand exact duplicate detection using actual size and SHA-256, with progress and cancellation. — DuplicateFinder / DuplicateFilesWindow; automated content/cancellation tests and manual UI review.
- [x] **F0097** Remove selected records from the library without deleting media files. — LibraryService.RemoveEntriesAsync / Library tools / duplicate results; media preservation and reopened-index tests.
- [x] **F0100** Batch editing of selected title, artist, album and genre tags, with explicit clearing, per-file results, progress and cancellation. — BatchTagService / BatchTagEditorWindow / TagLibSharp; eight-format tag/playback checks, preserved artwork and custom metadata, library refresh, cancellation and a two-file manual UI review.
- [x] **F0102** Personal library labels with batch add/remove, label search rules, smart playlists and backup merging. — Immutable label values / LibraryService.UpdateLabelsAsync / LibraryLabelsWindow; atomic limits and persistence rollback, cancellation, concurrent updates, reopened/merged backups, preserved media bytes and manual label/smart-playlist review.

### Video and subtitles

- [x] **F0068** Embedded and external subtitles supported by LibVLC. — MediaService and coalesced ES track refresh; native embedded SubRip/external SRT selection tests, Disabled preservation and file URI handling for Unicode/spaces/#.
- [x] **F0069** Subtitle track selection, auto loading, language preference and timing offset. — MediaService and coalesced ES track refresh; native embedded SubRip/external SRT selection tests, Disabled preservation and file URI handling for Unicode/spaces/#.
- [x] **F0070** Default text-subtitle font, size, colors, outline, shadow, bold text and bottom margin settings. — Validated native instance options with restart semantics. Installed font, size, color and margin effects verified in actual displayed frames; italic/top/center overrides remain pending.
- [x] **F0071** Text search in external SRT, VTT and ASS subtitle files with seek-to-result. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0072** Bookmarks and chapter navigation. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0073** Timeline thumbnail previews generated on demand. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0074** Aspect ratio and video adjustment controls. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0075** Deinterlacing options. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0076** Native video snapshots with actual PNG/JPEG/BMP encoding, unique names and overwrite protection. — MediaService.Snapshots; native file signature, cancellation and temporary cleanup tests.
- [x] **F0077** Ambient light sampled from the displayed frame. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0078** A–B clip export through LibVLC, with copy and transcode paths. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0080** Live subtitle delay controls, cue navigation, two-anchor drift correction and corrected SRT copy export. — SubtitleTiming / SubtitleEditingService / SubtitleTimingWindow; anchor, clipping, limits, cancellation and export tests, native offsets and manual save/load verification.
- [x] **F0081** Up to 32 named default text-subtitle style profiles, applied when the player restarts. — SubtitleStyle / SubtitleStyleWindow / startup options; persistence, limits, rendered color/size/margin/font geometry, snapshot content and saved-profile restart UI checks.
- [x] **F0084** Explicit scheduled snapshot jobs with interval, image limit, format, folder, progress and stop. — SnapshotSchedule / SnapshotScheduleWindow; sequential/cancellation tests, native capture and manual two-image completion review.

### Network and distribution

- [x] **F0086** Open supported stream URLs through LibVLC. — MediaFormats, MediaService, CI and build scripts
- [x] **F0087** Source-only public repository and Windows build/test CI. — MediaFormats, MediaService, CI and build scripts
- [x] **F0088** Local self-contained build profile and Inno Setup source. — MediaFormats, MediaService, CI and build scripts

## Pending (26)

### Workspaces and interface

- [ ] **F0015** Fully configurable HUD layout.
- [ ] **F0016** Advanced touch gestures.
- [ ] **F0017** Extension/plugin API.

### Playback and formats

- [ ] **F0030** Dedicated GPU-specific renderer selection policy.
- [ ] **F0031** Exhaustive codec/container/hardware compatibility matrix.
- [ ] **F0032** Native DSD and verified bit-perfect exclusive output.
- [ ] **F0104** Nested, ordered and linked-segment chapter playback, alternate editions/displays and editing chapters inside media containers.

### Audio

- [ ] **F0042** Verified playback normalization and compression. The previous option-only controls did not change captured PCM and are disabled. — A native loud-tone acceptance probe found no compression from per-media audio-filter options. Conversion normalization is implemented separately.
- [ ] **F0048** Headphone crossfeed.
- [ ] **F0049** Verified gapless transitions and crossfade.
- [ ] **F0050** Advanced DSP chain and measured output quality matrix.
- [ ] **F0051** Online lyric providers and lyric editing.
- [ ] **F0099** Preserve and edit advanced CUE directives such as CD-TEXT, PREGAP and additional indexes.

### Library and playlists

- [ ] **F0065** File organization and bulk renaming.
- [ ] **F0066** Remote library synchronization.
- [ ] **F0067** Arbitrary nested collections and advanced tagging workflows.
- [ ] **F0093** Automatic duplicate consolidation and metadata merge workflows.

### Video and subtitles

- [ ] **F0079** Automatic subtitle provider downloads.
- [ ] **F0082** Bilingual subtitle display.
- [ ] **F0083** OCR of burned-in subtitles.
- [ ] **F0085** Complete HDR/color management validation.
- [ ] **F0101** Accelerated subtitle composition on the WPF memory-video surface without a software decoder fallback.

### Network and distribution

- [ ] **F0089** Chromecast/DLNA sender.
- [ ] **F0090** Mobile web remote control.
- [ ] **F0091** Published releases, downloadable builds and release automation.
- [ ] **F0092** Signed executable distribution and automatic updates.

## Compatibility notes

Playback depends on the selected LibVLC runtime, plugins, input and hardware. Universal format support, native DSD, bit-perfect output and a complete hardware matrix are not claimed. The 20/31-band equalizer requires the native DSP module built from source; a legacy-only build retains ten-band processing. UI status identifies actual native acknowledgement.
