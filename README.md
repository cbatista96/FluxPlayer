<div align="center">

<img src="FluxPlayer/Assets/logo.png" width="96" alt="FluxPlayer logo">

# FluxPlayer

**A modern Windows player for music and video.**

An audio workspace with artwork, lyrics and a playlist. A video workspace with subtitles, timeline previews and theater controls. One LibVLC playback engine, presented with Fluent controls and Windows 11 materials.

[![Build and test](https://github.com/cbatista96/FluxPlayer/actions/workflows/ci.yml/badge.svg)](https://github.com/cbatista96/FluxPlayer/actions/workflows/ci.yml)
[![GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)
[![Windows x64](https://img.shields.io/badge/Windows-x64-0078D6)](#requirements)
[![WPF](https://img.shields.io/badge/UI-WPF-512BD4)](FluxPlayer/FluxPlayer.csproj)

[Features](FEATURES.md) · [Build from source](docs/BUILDING.md) · [User guide](docs/USER_GUIDE.md) · [Architecture](docs/ARCHITECTURE.md) · [Testing](docs/TESTING.md)

</div>

**Current local checkpoint:** Release builds successfully and 684 distinct automated groups pass. Continuous WASAPI gapless/crossfade, an ordered managed DSP plug-in chain, direct PCM, a native ASIO DSD backend and recursive ordered linked editions now join the HUD, GPU policy, duplicate consolidation, catalogue sync, subtitle downloads and casting panels. [Audio transport guide and limits](docs/REALTIME_AUDIO.md) · [Implementation and remaining work](docs/LOCAL_COMPLETION_PROGRESS.md) · [Extension API](docs/EXTENSION_API.md). A self-contained local ZIP is available under `artifacts/releases/local-online-ready`. **Publication and signed distribution are deferred by the user.** GPU subtitles, recursive/discovered chapters, native Direct3D11 presentation and 200 processing/diagnostic/subtitle capability IDs now pass their documented local tests. Physical DSD/surround, end-to-end HDR/calibration and remaining backend acceptance stay open. See docs/GPU_SUBTITLES.md, docs/NATIVE_GPU_PRESENTATION.md and docs/PROCESSING_ACCEPTANCE.md. Historical source-only checkpoints below do not describe the current build status.

**Latest local acceptance:** eleven more capability IDs pass real yt-dlp source/playback/live/playlist/download/caption workflows, native and browser mobile control, independently decoded PGS extraction and WsgiDAV catalogue synchronization. Shared-page playlist entries now resolve/download their selected position, and a portable yt-dlp executable can be selected without changing PATH. [Evidence and compatibility limits](docs/LOCAL_ONLINE_ACCEPTANCE.md).

**Earlier backend acceptance:** 158 additional capability IDs pass individual checks for Windows SQLite, authenticated control transports, independent audio zones, subtitle production, recorded-audio alignment, advanced Matroska editing, television recording/buffering and actual mpv IPC. Execution fixes and precise limits are recorded in [local backend acceptance](docs/LOCAL_BACKEND_ACCEPTANCE.md). 428 source-implemented capabilities still await their documented acceptance; physical devices and account-backed services are not certified by local fixtures.

**New local functionality:** Windows subtitle OCR now works without Tesseract when an installed Windows recognition language is available. Automatic rules support priority/deletion, and appearance saves preserve working settings on failure. Another 94 capability IDs pass actual UI/native checks, isolated WebDAV/Subsonic/Jellyfin servers and live public metadata. [Changes, evidence and limits](docs/LOCAL_SESSION_ACCEPTANCE.md).

**Development status:** this is a source-only repository. There are currently no published releases, installers, binaries or version tags. Build the application locally using the instructions below. The feature checklist separates working functionality from planned work.

**Earlier acceptance checkpoint:** [100 capabilities](docs/VERIFIED_HUNDRED.md) have completed acceptance testing, with a 192-group passing regression and a final 100-group focused recheck. The current catalog tracks **746 implemented / 428 implemented but unverified / 6 pending**. The earlier acceptance results do not cover the new expansion. Untested source work remains separately identified in the [feature checklist](FEATURES.md).

**Latest source batch — 500 operations/subfeatures:** [F0681–F1180](docs/FIVE_HUNDRED_CHECKLIST.md) adds 100 controls for an owned alternate mpv session, 184 explicit Kodi API methods, 116 MPD protocol commands and 100 offline algorithms (25 audio, 75 video). Panels are categorized under Playback, Library, Audio and Video. **The 100 processing algorithms now pass actual default-export/decode fixtures; the 100 mpv controls pass real IPC acceptance, while 300 Kodi/MPD operations still await backend acceptance. Nothing has been published.** This is not a claim that 500 broad original requirements are complete. [Scope, bounds and deferred acceptance](docs/UNTESTED_FIVE_HUNDRED.md) · [Reference attribution](docs/THIRD_PARTY_INTEROP.md).

**Previous remote-library source batch:** [100 additional capabilities, F0581–F0680](docs/NETWORK_HUNDRED_CHECKLIST.md): 30 Subsonic, 20 Jellyfin, 20 Emby, 20 Plex and 10 UPnP actions. Categorized panels provide explicit discovery/metadata reads and reviewed favorites, state, playlist, bookmark and device changes. These are **implemented but untested**; no build, authenticated provider/device request, new executable or GitHub publication was performed. [Scope and deferred acceptance](docs/UNTESTED_NETWORK_HUNDRED.md). Broad original integration/synchronization requirements remain partial.

**Previous source-only processing batch:** [100 additional capabilities, F0481–F0580](docs/PROCESSING_HUNDRED_CHECKLIST.md): 25 audio processes, 25 video processes, 20 analysis/visualization workflows and 30 subtitle quality/text/timing operations. Their five panels are categorized under Audio, Video and Subtitles. All 100 now pass their documented local processing, diagnostic or subtitle fixtures; see [acceptance scope](docs/PROCESSING_ACCEPTANCE.md). See [scope and deferred acceptance](docs/UNTESTED_PROCESSING_HUNDRED.md). Original broad requirements remain partial where appropriate. GitHub publication remains deferred.

**Previous source-only expansion:** [100 additional capabilities, F0381–F0480](docs/EXPANSION_HUNDRED_CHECKLIST.md), now have implementation under categorized Settings: Jellyfin/Emby/Plex, UPnP, ListenBrainz/Last.fm, derived SQL catalogs, independent audio zones, advanced Matroska chapters and Desktop/HTPC controls. They have **not been compiled, run or tested**. See [scope and deferred acceptance](docs/UNTESTED_EXPANSION_HUNDRED.md). No new executable, release or GitHub publication was produced for this expansion.

## What it does

| Area | Available functionality |
|---|---|
| **Audio and video modes** | Automatic selection based on decoded tracks, manual mode selection and separate audio window dimensions. |
| **Music workspace** | Artwork, metadata, synchronized LRC lyrics, CUE chapters, favorites, playlist controls and a floating equalizer. |
| **Playback** | Local files and supported network sources, seeking, speed, smart shuffle, A–B looping, virtual skip/mute/ordered segments, saved sessions, weekday schedules, priorities, alarms and volume events. |
| **Audio tools** | 10/15/20/31 graphic EQ, parametric EQ, bass, crossfeed, compressor/limiter, adaptive RMS leveling, logical channel matrix, gain/delay, width, time effects and measured signal-path levels. |
| **Audio measurements** | On-demand source RMS, estimated true peak, calculated ReplayGain and BPM estimates, with new-copy JSON export. |
| **Library** | A local index, watched folders, favorites, smart playlists, related local autoplay, artist/album mixes, computed personal ratings, numeric remaining-time sorting, backup restore and SHA-256 moved-file repair. |
| **Playlists** | Import and export M3U/M3U8, PLS and XSPF; reorder tracks and queue an item to play next. |
| **Video tools** | Bilingual external text subtitles, timing/search, bookmarks, thumbnails, snapshots/contact sheets, picture controls, custom aspect, zoom/pan, SBS/TB eye selection and renderer counters. |
| **Windows integration** | Mica and acrylic themes, mini player, tray controls, optional global hotkeys, mouse gestures, configurable OSD/toolbar groups and system media controls. |
| **Optional conversion** | Parallel audio conversion, ordered WAV/FLAC joining, MP4/WebM video copies and mobile profiles, with progress and cancellation. FFmpeg is installed separately. |
| **Portable and profiles** | A portable data folder, named profiles and an explicit data directory, each with independent settings and library state. |

Playback compatibility comes from the selected LibVLC runtime and its plugins. Supported containers, codecs, streams and hardware decoding depend on the input and environment; the project does not claim universal format compatibility, DRM playback or native bit-perfect DSD output. See the [feature checklist](FEATURES.md) for the current scope.

The [processing guide](docs/AUDIO_VIDEO_PROCESSING.md) documents the thirty-capability audio/video/scheduling batch, categorized settings, selected context-menu access, supported bounds and measurement limits. Native effects are disabled by default; analysis and conversion run on request.

The next [thirty-capability batch](docs/THIRTY_MORE_FEATURES.md) adds [playback automation and reviewed library/metadata workflows](docs/PLAYBACK_AUTOMATION.md), including filename tag patterns, batch numbering, case transformations, Latin transliteration and ringtone copies. Preferences remain organized in their existing Settings categories. The public checklist tracks **746 implemented / 428 implemented but unverified / 6 pending / 1,180 capabilities**.

The [local-media batch](docs/LOCAL_MEDIA_BATCH.md) adds photo albums/slideshows, image zoom/rotation, ZIP media extraction, reviewed music/device-folder exports and static display patterns. See the [workflow guide](docs/LOCAL_MEDIA_GUIDE.md) for formats, bounds and the distinction between folder export and device synchronization.

[Audio production](docs/AUDIO_PRODUCTION.md) adds selected-track audio extraction from video, reviewed batch extraction, simultaneous per-source gain/delay mixing and new WAV/FLAC mix exports. Both panels live in Settings → Audio → Production and use installed FFmpeg only for explicit jobs.

## Requirements

- 64-bit Windows. The application targets Windows 10 1809 or later; Mica and other Windows 11 materials depend on OS support.
- The .NET SDK selected by [`global.json`](global.json) to build, and the .NET 8 desktop runtime to run a framework-dependent build.
- Visual Studio 2022 Build Tools with **Desktop development with C++** for the native 15/20/31-band DSP module. See [Building](docs/BUILDING.md) for the optional ten-band-only build.
- NuGet access for the initial dependency restore. LibVLC is restored with the project; a separate VLC installation is unnecessary.
- Optional: `ffmpeg.exe` on `PATH` or next to the application for audio conversion. The required encoders must be available in that FFmpeg build.

## Build and run

```powershell
git clone https://github.com/cbatista96/FluxPlayer.git
cd FluxPlayer
dotnet restore FluxPlayer.sln -p:Platform=x64
dotnet build FluxPlayer.sln -c Release -p:Platform=x64
dotnet run --project FluxPlayer/FluxPlayer.csproj -c Release -p:Platform=x64
```

Open a file with **Ctrl+O**, open a network source with **Ctrl+U**, or drop files into the player. Use the mode selector for **Automatic**, **Audio** or **Video**. Press **E** for the equalizer and **Ctrl+,** for settings.

See [Building](docs/BUILDING.md) for SDK setup and optional local packaging. Build scripts and installer sources are included for development; generated output stays outside Git.

## Test

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release -- --managed-only
```

Omit `--managed-only` to also exercise the native playback engine. FFmpeg-dependent tests run when FFmpeg is available. The full suite needs a Windows desktop session with working playback output. See [Testing](docs/TESTING.md) for coverage and limits.

## Project structure

```text
FluxPlayer/          WPF application, views, models and playback services
FluxPlayer.Tests/    Executable regression suite
Native/FluxDsp/      Native 15/20/31-band VLC filter and processing-cost probe
docs/               Build instructions, user guide and engineering notes
installer/          Inno Setup installer source
.github/            Source-only CI and contribution guidance
```

## Contributing

Use English for code comments, application text, documentation, issues and pull requests. Keep optional work off the playback path and update the checklist when a feature becomes functional. Read [Contributing](CONTRIBUTING.md) before submitting a change.

## License

FluxPlayer source is licensed under [GNU GPL v3.0](LICENSE). Dependencies retain their own licenses; see [Third-party notices](THIRD_PARTY_NOTICES.md).
