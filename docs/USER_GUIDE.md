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

Press **E** to open the floating equalizer. **Layout** selects ten legacy bands from 31.25 Hz to 16 kHz, 15 ISO centers from 25 Hz to 16 kHz, 20 logarithmically spaced bands from 20 Hz to 20 kHz, or 31 ISO centers from 20 Hz to 20 kHz. The extended layouts use independent native filters and require the included DSP module to be built. They do not fold the controls into ten bands. Bands and the requested preamplifier support ±20 dB; 30 factory presets are available in every layout. Preset curves are sampled at that layout's centers.

A flat equalizer at 0 dB preserves the bypass level. **Compensate gain** reduces the preamplifier for boosted bands. Extended layouts estimate the combined response and leave a small margin; this is headroom estimation, not a peak limiter or a guarantee against clipping. The ten-band engine caps effective positive preamp at approximately +7.96 dB; extended processing supports the full requested range. The value below the controls reports the acknowledged effective native gain once processing starts. **Signal path and information** also shows the actual DSP rate and channel count. Bands above the current sample rate's Nyquist limit are omitted.

Switching layouts recalls each curve during the current session. The active layout and all its bands persist across restarts; saved output profiles retain their own complete layouts. The panel widens for more bands and scrolls horizontally when needed. Double-click a band or preamp to return it to 0 dB; arrow keys adjust it by 0.5 dB. Playback normalization and compressor controls remain pending verified signal processing and are disabled; the converter's optional loudness normalization is available.

The audio settings include output device selection, ReplayGain options, audio delay and preferred track language. Tag editing writes supported metadata to the original file; release it from playback before saving. Tag write support depends on its format.

**Settings > Audio > Channel routing** offers Original, Mono, Stereo, Reverse stereo, Left only and Right only. Mono mixes the input into the same signal on both sides. Reverse stereo swaps left/right; an isolated input channel plays on both sides. Stereo requests a two-channel mix, including a native downmix for supported multichannel sources. Original follows the source/output default. This preference applies to the current session, later sources and selected outputs; pause and position are retained when saving settings. It remains global rather than part of an output profile.

Earlier 2.1/5.1/7.1 settings were stored without configuring a speaker layout; they migrate to Original. Explicit speaker layouts, LFE management, arbitrary channel matrices, per-channel gain/delay and certified surround output remain pending. Actual routing is listed in **Signal path and information** and can depend on the source and output capabilities.

Open **Settings > Tools > Audio output profiles** to save the player's current volume, full 10/15/20/31-band equalizer layout, preamp, gain compensation and audio delay for a selected output. Up to 64 profiles are keyed by output identifier; saving for one device leaves other profiles independent. Saved devices remain listed when disconnected. **Refresh devices** updates availability.

Enable automatic application to recall a saved profile when selecting that output or starting FluxPlayer with it selected. With automatic application off, **Use output and saved profile** still applies it explicitly. Profile changes preserve mute and respect the global volume-boost limit. Changes made afterward do not overwrite a profile until **Save current settings** is chosen again. Removing a profile retains the current audio settings. The System default profile follows Windows' current default; automatic physical-device/hotplug profile switching remains pending. ReplayGain and other effects keep their global settings.

Open **Settings > Tools > Batch tag editor** to edit the current queue, or select several library rows and choose **Edit tags**. Add or remove files from the job, then check only the title, artist, album or genre fields you want to replace. A checked empty field clears that tag; unchecked fields are preserved. **Apply checked fields** writes to the original files and shows saved, skipped and failed results. Loaded files, missing files and network sources are skipped. Cancellation finishes the current save and stops before the next file; completed changes remain. Close the editor to cancel an active job. Keep backups of files before making large changes.

## Subtitle timing

Open **Settings > Tools > Subtitle timing editor**. The **Live delay** controls affect the currently selected subtitle track; positive milliseconds display subtitles later. Load an external SRT, VTT, ASS or SSA file to compare its original and adjusted timestamps. Select a row and use **Seek selected original cue** to navigate using the current live delay.

Export correction is separate from live delay. Set an offset and time scale, then choose **Update table**. For drift correction, pause the video at the matching time for an early cue, select that cue and capture the first anchor. Repeat with a later cue and the second anchor. The editor calculates a positive scale and offset from those two matches.

**Save corrected SRT copy** preserves the original. Text ending before zero is omitted; a partially visible cue is clipped to zero. The copy is plain text: ASS/VTT styling is not retained. **Load saved copy in player** selects it and resets live delay to zero, preventing a second timing adjustment. External subtitle tracks appear in the selector as they are discovered; choosing Disabled remains respected.

## Subtitle styles

Open **Settings > Tools > Subtitle style profiles** to set a default font family, font size, text/outline colors, outline thickness, bold text, shadow and bottom margin. A size of 0 uses automatic sizing; other sizes and margins are in video pixels. Outline thickness is a percentage of the font size. Choose **Use this style**, or enter a name and **Save profile**. Up to 32 profiles are stored in the active data profile. A saved profile can be selected, edited or removed; removing it keeps the current default style.

Restart FluxPlayer to apply a changed style. Embedded text styling and bitmap subtitles may override these defaults. Global italic and top/center placement overrides remain pending. Subtitles prefer the requested languages and then accept an unlabeled track; include `none` in the language list to stop that fallback. Disabling **Show subtitles by default** selects no subtitle track until you explicitly load or select one.

With the current WPF memory surface, an active subtitle on an opaque Windows hardware-decoder surface switches that source to software decoding so the text is visible. Position, pause and externally loaded subtitles are preserved. **Signal path and information** identifies this profile. This compatibility path can increase CPU usage; accelerated subtitle composition in this surface remains pending.

For local lyrics, place an LRC or text file beside the media with the same base name. An LRC file displays synchronized lines. A matching CUE sheet provides track chapters where supported.

**Settings > Tools > Create or edit CUE sheet** opens the track editor. Add source files, add the current playback position, or load an existing sheet. Edit titles, artists, source paths and `minutes:seconds:frames` start positions in the table; use Move up/down to arrange tracks. There are 75 frames per second and a maximum of 99 tracks. Play selected track opens its source at the indicated position.

Save as suggests an edited copy. The writer uses relative source paths, checks increasing positions within each source and rejects invalid text or an accidental overwrite. This editor writes audio `FILE`, `TRACK`, `TITLE`, `PERFORMER` and `INDEX 01` entries. Advanced directives such as CD-TEXT, PREGAP and other indexes are not retained, so keep the original when editing a more complex sheet.

## External chapters

Open **Settings > Tools > Chapter editor and external chapters**. Load an OGM simple chapter file (`.txt`/`.ogm`) or Matroska chapter XML, or copy the player's existing chapter points. Add a row or the current playback position, then edit titles and `hours:minutes:seconds.milliseconds` start times directly in the table. **Find** filters the draft by title or time; **Sort by time** orders it before validation. Saving and applying use the full draft, including rows hidden by Find.

**Apply to current media** updates navigation for this session. Use **Settings > Tools > Chapters** or **Seek selected in player** to jump to a point. The menu shows up to 200 entries; the editor can search all 10,000 supported points. Times must increase without duplicates and stay before the known end of the media. **Restore native chapters** restores source navigation while retaining the editor draft. Opening a different source clears session-only external chapter points.

**Save chapter copy** writes a new OGM text or flat Matroska XML document and refuses to overwrite existing files. Imported XML uses visible top-level chapters from the default or first edition, with times floored to milliseconds. Nested chapters, alternate languages/editions, ends, UIDs and other container metadata are not retained in the copy. Ordered and linked-segment chapters are rejected because they cannot describe one flat playback timeline. The original chapter document and media container remain unchanged.

## Library and smart playlists

Open the library from the title bar. Add a media folder to scan its files and watch for changes. Select an entry to play it, mark favorites and save searches as smart playlists. Library data can be exported to and imported from a JSON backup.

Search accepts plain text, quoted phrases, field rules and `-` exclusions. Terms are combined with AND:

```text
artist:"John Coltrane" format:flac duration:>3:00 -genre:pop
favorite:true plays:>=3 added:>=2026-01-01
"Blue Train" size:<100
```

Fields include `title`, `artist`, `album`, `genre`, `format`, `folder`, `duration`, `plays`, `size`, `added` and `favorite`. Duration uses seconds or `minutes:seconds`; size uses MiB; dates use `yyyy-MM-dd`. Existing saved queries using legacy localized aliases remain readable.

Select up to 10,000 library rows and choose **Personal labels** to add or remove labels across the selection. Enter one label per line; each file supports up to 32 labels of 64 characters. Adding preserves existing labels; removing affects only the entered labels. Labels belong to the local library and its backups, and do not modify media metadata. A failed or canceled save before commit leaves the previous labels intact.

Use `label:"Road trip"` for an exact label match, `-label:Private` to exclude one, or ordinary search text for a partial label match. Label matching ignores letter case. **Save view as playlist** stores those rules as a smart playlist. Backup restoration merges current and imported labels; a merge that would exceed the per-file limit is rejected before changing the index.

Playlists can be imported or exported as M3U/M3U8, PLS and XSPF. Relative file entries are resolved against the playlist folder.

When playlist persistence is enabled, reopening FluxPlayer restores its queue and selected item without starting playback. Play then starts the restored selection; unavailable files are skipped when restoring.

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

**F12** saves a native video frame in the selected PNG, JPEG or BMP format. Captures use unique names and refuse to replace an existing image. Image encoding and file I/O run outside the UI dispatcher. Capturing adds processing and storage work while the request runs.

**Settings > Tools > Scheduled snapshots** starts an explicit capture job. Choose an interval, image limit, format and folder, then select Start capture job. It captures only while video is playing, skips paused/audio-only/busy intervals, and stops at the image limit. Stop or close the window to cancel; completed images remain available. No capture job runs merely because the tool is installed or its window is opened.

## Convert audio

Install FFmpeg separately, select the converter from the audio tools, add source files, choose an output profile and destination, then start the job. Jobs run sequentially and can be canceled. Existing destination files are not overwritten. Optional loudness normalization changes the signal; lossless encoding does not undo losses already present in a source.

## Local data

Settings, bookmarks, recent files and library data are stored under the current user's FluxPlayer application-data folders. Engine diagnostics, when enabled, are local files and can include media paths or stream URLs. Review them before attaching a report. The repository and CI do not include personal settings or media files.

## Settings and tools

Use the single title-bar gear or Ctrl+, to open Settings. The Tools page contains the audio, video, chapter, subtitle, library and conversion utilities. Opening a tool saves current preferences; a failed save keeps Settings open. Dynamic output, chapter and ReplayGain choices use the same playback actions. Frequently used library, playlist, theme and equalizer shortcuts remain available. Future media tools should be registered in the shared tool menu so they also appear in Settings.
