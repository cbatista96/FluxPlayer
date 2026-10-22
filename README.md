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

**Development status:** this is a source-only repository. There are currently no published releases, installers, binaries or version tags. Build the application locally using the instructions below. The feature checklist separates working functionality from planned work.

## What it does

| Area | Available functionality |
|---|---|
| **Audio and video modes** | Automatic selection based on decoded tracks, manual mode selection and separate audio window dimensions. |
| **Music workspace** | Artwork, metadata, synchronized LRC lyrics, CUE chapters, favorites, playlist controls and a floating equalizer. |
| **Playback** | Local files and supported network sources, seeking, speed control, repeat, shuffle, A–B looping and frame stepping. |
| **Audio tools** | 10/15/20/31 native equalizer bands, preamplifier, 30 presets, gain compensation, per-output profiles, ReplayGain options and audio delay. |
| **Library** | A local index, watched folders, favorites, playback statistics, smart playlists, field queries, backup restore and exact duplicate detection. |
| **Playlists** | Import and export M3U/M3U8, PLS and XSPF; reorder tracks and queue an item to play next. |
| **Video tools** | Subtitle loading and timing, subtitle text search, bookmarks, timeline thumbnails, snapshots, ambient light and A–B clip export. |
| **Windows integration** | Mica and acrylic themes, mini player, tray controls, configurable shortcuts and system media controls. |
| **Optional conversion** | An on-demand FFmpeg audio converter with progress and cancellation. FFmpeg is installed separately. |
| **Portable and profiles** | A portable data folder, named profiles and an explicit data directory, each with independent settings and library state. |

Playback compatibility comes from the selected LibVLC runtime and its plugins. Supported containers, codecs, streams and hardware decoding depend on the input and environment; the project does not claim universal format compatibility, DRM playback or native bit-perfect DSD output. See the [feature checklist](FEATURES.md) for the current scope.

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
