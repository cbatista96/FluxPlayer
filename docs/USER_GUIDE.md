# User guide

## Open media and choose a workspace

Press **Ctrl+O** to choose files or drag files onto the player. **Ctrl+U** opens a supported network source. The title bar mode selector offers **Automatic**, **Audio** and **Video**. Automatic mode first uses the file type as a hint, then uses the decoded tracks; audio with cover art should remain in the music workspace.

The audio workspace displays artwork, metadata, synchronized lyrics and a playlist. The video workspace gives the picture more space and supports fullscreen controls. Audio window dimensions are remembered separately.

## Playback controls

These are the default shortcuts. Change them in Settings > Keyboard shortcuts.

| Action | Shortcut |
|---|---|
| Play / pause | Space |
| Seek backward / forward | Left / Right |
| Large seek backward / forward | Shift+Left / Shift+Right |
| Previous / next track | Ctrl+Left / Ctrl+Right |
| Volume | Down / Up |
| Mute | M |
| Fullscreen | F |
| Playlist | P |
| Equalizer | E |
| Media information | I |
| A–B loop | L |
| Next frame | Period |
| Snapshot | F12 |
| Mini player | Ctrl+M |
| Add bookmark | B |
| Next / previous bookmark | N / Shift+N |
| Ambient light | A |
| Sleep timer | T |
| Control lock | Ctrl+L |
| Settings | Ctrl+, |

Right-click the player or a playlist item for context actions, including play next, media tools and A–B clip export.

## Equalizer and audio

Press **E** to open the floating equalizer. It offers ten bands from 31 Hz to 16 kHz, a preamplifier and 30 presets. Peak compensation reduces the preamplifier when boosted bands require additional headroom. It cannot guarantee that every source, output device or downstream effect will avoid clipping.

The audio settings include output device selection, ReplayGain options, audio delay and preferred track language. Tag editing writes supported metadata to the original file; release it from playback before saving. Tag write support depends on its format.

For local lyrics, place an LRC or text file beside the media with the same base name. An LRC file displays synchronized lines. A matching CUE sheet provides track chapters where supported.

## Library and smart playlists

Open the library from the title bar. Add a media folder to scan its files and watch for changes. Select an entry to play it, mark favorites and save searches as smart playlists. Library data can be exported to and imported from a JSON backup.

Search accepts plain text, quoted phrases, field rules and `-` exclusions. Terms are combined with AND:

```text
artist:"John Coltrane" format:flac duration:>3:00 -genre:pop
favorite:true plays:>=3 added:>=2026-01-01
"Blue Train" size:<100
```

Fields include `title`, `artist`, `album`, `genre`, `format`, `folder`, `duration`, `plays`, `size`, `added` and `favorite`. Duration uses seconds or `minutes:seconds`; size uses MiB; dates use `yyyy-MM-dd`. Existing saved queries using legacy localized aliases remain readable.

Playlists can be imported or exported as M3U/M3U8, PLS and XSPF. Relative file entries are resolved against the playlist folder.

**Library tools > Restore backup (merge)** validates a FluxPlayer JSON backup before merging it. Existing favorites, higher play counts and analyzed metadata are retained. Folder monitoring is restored only if you select that option and the folders are available. Unsupported or invalid backup records are rejected before the merge starts.

**Library tools > Find exact duplicates** compares actual file sizes first, then streams SHA-256 hashes for equal-size candidates. The result groups matching files and supports playback or removal from the library index. It never deletes files on disk. A watched folder can add an index entry back during a later scan. Scanning runs only when requested and supports cancellation.

## Portable mode and profiles

Launch with `--portable` to keep settings, library state and caches in a `Data` folder beside the executable. Alternatively, create an empty `portable.flag` beside the executable to use portable mode when launching it normally. This location must be writable.

Use `--profile Music` or `--profile Video` for independent named profiles under the user's FluxPlayer data folder. Each profile keeps its own settings, library, history, playlists and diagnostics. Names use letters, digits, hyphens or underscores. `--data-directory "D:\\FluxPlayerData"` selects an explicit data location. Choose one location switch per launch; it can be followed by media paths.

```powershell
.\FluxPlayer.exe --portable
.\FluxPlayer.exe --profile Music "D:\Music\song.flac"
.\FluxPlayer.exe --profile Video
.\FluxPlayer.exe --data-directory "D:\FluxPlayerData"
```

The Settings page shows the current profile's settings path. Its Open configuration folder action opens that same location. Profile selection occurs at startup, so changing profiles means launching another instance with the desired option.

## Subtitles and video tools

Load external subtitles or choose an embedded track from the video controls. Adjust subtitle and audio delay when timing differs from the picture. External subtitle text search supports SRT, VTT and ASS and lets you jump to a matching cue. Automatic subtitle downloads, OCR and bilingual subtitles remain planned.

Bookmarks, snapshots and A–B clip export are available through the video tools and context menu. Copy-based clip export depends on container and keyframe boundaries; transcoding takes additional time.

## Convert audio

Install FFmpeg separately, select the converter from the audio tools, add source files, choose an output profile and destination, then start the job. Jobs run sequentially and can be canceled. Existing destination files are not overwritten. Optional loudness normalization changes the signal; lossless encoding does not undo losses already present in a source.

## Local data

Settings, bookmarks, recent files and library data are stored under the current user's FluxPlayer application-data folders. Engine diagnostics, when enabled, are local files and can include media paths or stream URLs. Review them before attaching a report. The repository and CI do not include personal settings or media files.
