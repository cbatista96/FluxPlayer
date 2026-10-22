# Feature checklist

**70 implemented · 27 pending · 97 tracked capabilities.**

Each capability appears exactly once. Completing it moves the same stable ID from Pending to Implemented. A setting or catalog entry alone does not complete a feature. Source references describe the implementation; validation limits are documented in [Testing](docs/TESTING.md).

Maintained in `docs/feature-catalog.json`. Regenerate with `pwsh -File tools/Sync-PublicFeatures.ps1`; CI rejects an outdated checklist.

## Implemented (70)

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

### Audio

- [x] **F0033** Artwork and title/artist/album/genre metadata. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0034** Metadata editing through LibVLC for formats that support saving tags. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0035** Synchronized local LRC lyrics and plain text lyrics. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0036** CUE sheet chapters and native chapter navigation. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0037** Ten-band equalizer with preamplifier and 30 presets. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0038** Equalizer peak compensation to reduce clipping risk from boosted bands. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0039** Migration of presets saved by the earlier localized interface. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0040** ReplayGain track/album options, preamplification and peak protection. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0041** Output device selection, audio tracks, channel options and audio delay. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0042** LibVLC normalization and compressor controls. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0043** Optional FFmpeg conversion to FLAC, WAV, MP3, Opus, Vorbis, AAC, ALAC and WavPack. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0044** Sequential conversion, progress, cancellation and optional loudness normalization. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools
- [x] **F0045** Conversion writes to a temporary file and refuses to overwrite an existing destination. — MediaService.Audio, MainViewModel.Audio, EqualizerPresets and media tools

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
- [x] **F0062** Playlist reorder, clear, play-next queue and persistence. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0063** Bounded initial duration parsing rather than decoding the full library at startup. — LibraryService, LibraryQuery, PlaylistService and LibraryWindow
- [x] **F0064** On-demand exact duplicate detection using actual size and SHA-256, with progress and cancellation. — DuplicateFinder / DuplicateFilesWindow; automated content/cancellation tests and manual UI review.
- [x] **F0097** Remove selected records from the library without deleting media files. — LibraryService.RemoveEntriesAsync / Library tools / duplicate results; media preservation and reopened-index tests.

### Video and subtitles

- [x] **F0068** Embedded and external subtitles supported by LibVLC. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0069** Subtitle track selection, auto loading, language preference and timing offset. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0070** Subtitle display options exposed through the current renderer. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0071** Text search in external SRT, VTT and ASS subtitle files with seek-to-result. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0072** Bookmarks and chapter navigation. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0073** Timeline thumbnail previews generated on demand. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0074** Aspect ratio and video adjustment controls. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0075** Deinterlacing options. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0076** Video snapshots. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0077** Ambient light sampled from the displayed frame. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService
- [x] **F0078** A–B clip export through LibVLC, with copy and transcode paths. — MediaService, MainWindow.Features, SubtitleDocument and ThumbnailService

### Network and distribution

- [x] **F0086** Open supported stream URLs through LibVLC. — MediaFormats, MediaService, CI and build scripts
- [x] **F0087** Source-only public repository and Windows build/test CI. — MediaFormats, MediaService, CI and build scripts
- [x] **F0088** Local self-contained build profile and Inno Setup source. — MediaFormats, MediaService, CI and build scripts

## Pending (27)

### Workspaces and interface

- [ ] **F0015** Fully configurable HUD layout.
- [ ] **F0016** Advanced touch gestures.
- [ ] **F0017** Extension/plugin API.

### Playback and formats

- [ ] **F0030** Dedicated GPU-specific renderer selection policy.
- [ ] **F0031** Exhaustive codec/container/hardware compatibility matrix.
- [ ] **F0032** Native DSD and verified bit-perfect exclusive output.

### Audio

- [ ] **F0046** Real 20-band and 31-band DSP equalizers. The current engine has ten bands.
- [ ] **F0047** Audio profiles per output device.
- [ ] **F0048** Headphone crossfeed.
- [ ] **F0049** Verified gapless transitions and crossfade.
- [ ] **F0050** Advanced DSP chain and measured output quality matrix.
- [ ] **F0051** Online lyric providers and lyric editing.

### Library and playlists

- [ ] **F0065** File organization and bulk renaming.
- [ ] **F0066** Remote library synchronization.
- [ ] **F0067** Arbitrary nested collections and advanced tagging workflows.
- [ ] **F0093** Automatic duplicate consolidation and metadata merge workflows.

### Video and subtitles

- [ ] **F0079** Automatic subtitle provider downloads.
- [ ] **F0080** Live subtitle synchronization editor.
- [ ] **F0081** Saved subtitle style profiles.
- [ ] **F0082** Bilingual subtitle display.
- [ ] **F0083** OCR of burned-in subtitles.
- [ ] **F0084** Scheduled snapshots.
- [ ] **F0085** Complete HDR/color management validation.

### Network and distribution

- [ ] **F0089** Chromecast/DLNA sender.
- [ ] **F0090** Mobile web remote control.
- [ ] **F0091** Published releases, downloadable builds and release automation.
- [ ] **F0092** Signed executable distribution and automatic updates.

## Compatibility notes

Playback depends on the selected LibVLC runtime, plugins, input and hardware. Universal format support, native DSD, bit-perfect output and a complete hardware matrix are not claimed. Legacy feature identifiers may retain historical names for settings compatibility: `equalizer-31-band-preamp` currently identifies the ten-band equalizer. UI labels describe its actual capability.
