# Feature checklist

Checked items have an implementation in this source tree. Unchecked items remain planned or require additional engineering and validation. A setting or catalog entry alone does not count as an implemented feature. This checklist describes the public application scope and is not a claim that every competitor feature is complete.

## Workspaces and interface

- [x] Audio and video workspaces.
- [x] Automatic mode selection using decoded audio/video tracks, with extension-based hints before parsing.
- [x] Manual Automatic / Audio / Video preference.
- [x] Separate saved audio window dimensions.
- [x] Compact music layout with artwork, metadata, transport and playlist.
- [x] English interface, dialogs, tooltips and accessibility names.
- [x] Dark Mica, light Mica, black and acrylic themes with system-dependent materials.
- [x] Fullscreen playback and automatic control hiding.
- [x] Mini player using the same playback session.
- [x] On-screen playback and volume feedback.
- [x] Configurable keyboard shortcuts and mouse actions.
- [x] Presentation control lock.
- [x] Tray controls and system media transport integration.
- [x] Sleep timer.
- [ ] Fully configurable HUD layout.
- [ ] Advanced touch gestures.
- [ ] Extension/plugin API.

## Playback and formats

- [x] LibVLC playback of supported audio/video files and network sources.
- [x] File picker, drag and drop, folder loading and recent files.
- [x] Local/remote source detection, including UNC paths.
- [x] Play, pause, stop, seek, previous and next.
- [x] Playback speed, repeat and shuffle.
- [x] A–B loop and video frame advance.
- [x] Playback position persistence per file.
- [x] Configurable intro/outro skipping.
- [x] Queue a track to play next without reordering the playlist.
- [x] Adaptive caching and playback fallback profiles.
- [x] Plugin cache generation for local packaging.
- [x] Optional local engine diagnostics.
- [ ] Dedicated GPU-specific renderer selection policy.
- [ ] Exhaustive codec/container/hardware compatibility matrix.
- [ ] Native DSD and verified bit-perfect exclusive output.

## Audio

- [x] Artwork and title/artist/album/genre metadata.
- [x] Metadata editing through LibVLC for formats that support saving tags.
- [x] Synchronized local LRC lyrics and plain text lyrics.
- [x] CUE sheet chapters and native chapter navigation.
- [x] Ten-band equalizer with preamplifier and 30 presets.
- [x] Equalizer peak compensation to reduce clipping risk from boosted bands.
- [x] Migration of presets saved by the earlier localized interface.
- [x] ReplayGain track/album options, preamplification and peak protection.
- [x] Output device selection, audio tracks, channel options and audio delay.
- [x] LibVLC normalization and compressor controls.
- [x] Optional FFmpeg conversion to FLAC, WAV, MP3, Opus, Vorbis, AAC, ALAC and WavPack.
- [x] Sequential conversion, progress, cancellation and optional loudness normalization.
- [x] Conversion writes to a temporary file and refuses to overwrite an existing destination.
- [ ] Real 20-band and 31-band DSP equalizers. The current engine has ten bands.
- [ ] Audio profiles per output device.
- [ ] Headphone crossfeed.
- [ ] Verified gapless transitions and crossfade.
- [ ] Advanced DSP chain and measured output quality matrix.
- [ ] Online lyric providers and lyric editing.

## Library and playlists

- [x] Persistent local JSON index.
- [x] Folder scanning outside the UI dispatcher.
- [x] Watched folders with file change updates.
- [x] Favorites, playback counts and last played dates.
- [x] Search by text and fields, quoted phrases and exclusions.
- [x] Format, genre, duration, size, play count and date rules.
- [x] Saved smart playlists evaluated against the current library.
- [x] Library JSON backup export and import.
- [x] M3U/M3U8, PLS and XSPF import/export.
- [x] Unicode paths and relative playlist entries.
- [x] Playlist reorder, clear, play-next queue and persistence.
- [x] Bounded initial duration parsing rather than decoding the full library at startup.
- [ ] Duplicate detection and merge workflows.
- [ ] File organization and bulk renaming.
- [ ] Remote library synchronization.
- [ ] Arbitrary nested collections and advanced tagging workflows.

## Video and subtitles

- [x] Embedded and external subtitles supported by LibVLC.
- [x] Subtitle track selection, auto loading, language preference and timing offset.
- [x] Subtitle display options exposed through the current renderer.
- [x] Text search in external SRT, VTT and ASS subtitle files with seek-to-result.
- [x] Bookmarks and chapter navigation.
- [x] Timeline thumbnail previews generated on demand.
- [x] Aspect ratio and video adjustment controls.
- [x] Deinterlacing options.
- [x] Video snapshots.
- [x] Ambient light sampled from the displayed frame.
- [x] A–B clip export through LibVLC, with copy and transcode paths.
- [ ] Automatic subtitle provider downloads.
- [ ] Live subtitle synchronization editor.
- [ ] Saved subtitle style profiles.
- [ ] Bilingual subtitle display.
- [ ] OCR of burned-in subtitles.
- [ ] Scheduled snapshots.
- [ ] Complete HDR/color management validation.

## Network and distribution

- [x] Open supported stream URLs through LibVLC.
- [x] Source-only public repository and Windows build/test CI.
- [x] Local self-contained build profile and Inno Setup source.
- [ ] Chromecast/DLNA sender.
- [ ] Mobile web remote control.
- [ ] Published releases, downloadable builds and release automation.
- [ ] Signed executable distribution and automatic updates.

## Keeping this list accurate

Keep the user-facing status in `FluxPlayer/Models/FeatureCatalog.cs` consistent with this document. Some legacy feature identifiers retain historical names for settings compatibility; for example, `equalizer-31-band-preamp` currently identifies the implemented ten-band equalizer. UI labels and this document describe its actual capability.

Check a planned item only after its complete user flow works and the relevant validation passes. Record important constraints next to the feature rather than hiding them behind a checked box.
