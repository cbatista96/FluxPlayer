# User guide

## Current local implementation

The [local completion guide](LOCAL_COMPLETION_PROGRESS.md) lists the exact settings paths for the configurable HUD, GPU policy, remote catalogue sync, OpenSubtitles downloads, Chromecast/DLNA sending, persistent duplicate consolidation and ordered/linked chapter playback. It explains dependencies and acceptance limits. Publication and certificate setup are deferred; local use requires no PFX.

## Added tools

**Subtitle OCR:** open **Subtitles → Recognition and extraction → Burned-in subtitle OCR**. Choose a local video, leave the engine path empty for Windows OCR, and use **List Windows OCR languages** to choose an installed language (`eng`/`en-US`, for example). Choose an excerpt and normalized subtitle region, then recognize. Review the sampled timings and text, save cue corrections, and export a new SRT or WebVTT. FFmpeg is required. A separately installed trusted Tesseract remains available for its own language models, including combined models such as `eng+spa`.

**Rules and appearance:** Playback → Media profiles provides saved extension rules. Move them up/down to set first-match priority, edit/delete them, and explicitly Activate/reload for the session. Disable stops automatic application. General → Appearance provides saved custom accent, idle-video background and equalizer docking. Invalid images are rejected before saving; a failed apply retains the previous working preferences.

**Libraries and metadata:** Library → Remote libraries / Personal media servers provides explicit Subsonic, Jellyfin, Emby and Plex sessions. Files → Cloud and network provides WebDAV. Credentials stay in the session; opening a panel does not connect. Streams use temporary authenticated relays while the panel remains open. Downloaded copies and offline playlists use local paths. Repeated Subsonic occurrences download each source once while offline playlists can retain repeated occurrences. Library → Online metadata provides MusicBrainz searches, release tracks, relationships, selected cover copies and a separate review/apply step for local tags.

**Online sources:** use **Choose yt-dlp…** to select a trusted portable executable, or leave its field empty to search PATH. Inspect an HTTPS page and select a format for playback or a new native-format download. For a playlist, read its entries and resolve the selected one; entries sharing a page preserve their individual positions. Authored WebVTT language tracks can be downloaded separately. Live sources play through the selected temporary URL; recording uses the finite stream-recording workflow. Provider support and required headers remain conditional. The tested local copy is `session-state/tools/yt-dlp-2026.10.26/yt-dlp.exe`; no global installation is required.

**Mobile control and catalogue sync:** start the mobile remote on a listed private interface and pair the browser using the displayed code. It controls the existing queue; stop the remote to revoke pairing. Remote catalogue synchronization requires a server that enforces strong ETags and conditional PUT. WsgiDAV 4.4.0 passed the conflict test; dufs 0.47.0 did not enforce the tested If-Match and should not be used for this workflow. Catalogue sync transfers metadata and local paths, not the media files. [Acceptance and limits](LOCAL_ONLINE_ACCEPTANCE.md).

**Other engines and devices:** Playback → Alternative engines controls a separately installed mpv. Library → Home theater integration provides Kodi JSON-RPC; Audio → Network players provides MPD. These sessions control the selected external backend independently of the main LibVLC player. Provider/version/device support remains conditional. [Individual feature status](../FEATURES.md), [local backend acceptance](LOCAL_BACKEND_ACCEPTANCE.md) and [server/public metadata acceptance](LOCAL_SESSION_ACCEPTANCE.md) distinguish tested workflows from remaining verification.

**Offline production:** Audio/Video → Offline recipes exports bounded excerpts as new copies. Audio → Signal and measurements, Video → Picture diagnostics and Subtitles → Quality and cleanup provide measurements, visualizations and editable subtitle documents. Audio → Production includes stream extraction, batch conversion, gain/delay mixing and joins; FFmpeg is required. See [audio production](AUDIO_PRODUCTION.md) and [processing acceptance](PROCESSING_ACCEPTANCE.md).

**Capture and television:** Video → Capture / Television provides explicit device recording, local IPTV/XMLTV catalogs, finite stream copies and buffering. Recording schedules run only after being armed and while the application remains open. Physical input devices and provider-specific streams need their own acceptance. The photo viewer supports bounded animated GIF playback; HEIF/HEIC depends on an available Windows decoder.

Settings → Mouse → Touch gestures enables video-picture gestures. Settings → General → Extensions provides the [versioned extension API](EXTENSION_API.md). Library → Find exact duplicates previews metadata consolidation while retaining original media files. Broader physical touch, device and extension compatibility limits remain documented in Testing.

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

## Remember playback choices for each file

Settings > Playback contains independent Remember volume per file, Remember audio track per file and Remember subtitles per file controls. They are off by default and take effect when opening a local file. Explicit changes are remembered for up to 1,000 files, retaining the most recently edited entries. The regular settings save and player shutdown persist these choices.

Volume is restored within the current boost limit and keeps the current mute state. When enabled, a file's stored volume takes precedence over the initial global/output-profile volume when that file opens. Audio and embedded subtitle choices require the same native track ID and name; unmatched choices leave the native default. Selecting Disabled is remembered as well. Loading an external subtitle remembers its local path; reopening the media reloads it if it still exists. Selecting a different enabled subtitle track replaces the external-path preference. Subtitle delay and style remain global settings.

The media's path, size and modification time identify a remembered file. Changed files do not reuse old choices; this stamp is not a content hash. Streams are excluded. Renaming through the batch rename tool relocates preferences together with queue paths. Turning an option off stops recording/restoring it without deleting its stored choices. Forget all file preferences clears this history when Settings is saved; Cancel discards the clear request. Clearing does not alter media, bookmarks, resume points or library statistics.

## Equalizer and audio

Press **E** to open the floating equalizer. **Layout** selects ten legacy bands from 31.25 Hz to 16 kHz, 15 ISO centers from 25 Hz to 16 kHz, 20 logarithmically spaced bands from 20 Hz to 20 kHz, or 31 ISO centers from 20 Hz to 20 kHz. The extended layouts use independent native filters and require the included DSP module to be built. They do not fold the controls into ten bands. Bands and the requested preamplifier support ±20 dB; 30 factory presets are available in every layout. Preset curves are sampled at that layout's centers.

A flat equalizer at 0 dB preserves the bypass level. **Compensate gain** reduces the preamplifier for boosted bands. Extended layouts estimate the combined response and leave a small margin; this is headroom estimation, not a peak limiter or a guarantee against clipping. The ten-band engine caps effective positive preamp at approximately +7.96 dB; extended processing supports the full requested range. The value below the controls reports the acknowledged effective native gain once processing starts. **Signal path and information** also shows the actual DSP rate and channel count. Bands above the current sample rate's Nyquist limit are omitted.

Switching layouts recalls each curve during the current session. The active layout and all its bands persist across restarts; saved output profiles retain their own complete layouts. The panel widens for more bands and scrolls horizontally when needed. Double-click a band or preamp to return it to 0 dB; arrow keys adjust it by 0.5 dB. Playback normalization and compressor controls remain pending verified signal processing and are disabled; the converter's optional loudness normalization is available.

The audio settings include output device selection, ReplayGain options, audio delay and preferred track language. Tag editing writes supported metadata to the original file; release it from playback before saving. Tag write support depends on its format.

**Settings > Audio > Channel routing** offers Original, Mono, Stereo, Reverse stereo, Left only and Right only. Mono mixes the input into the same signal on both sides. Reverse stereo swaps left/right; an isolated input channel plays on both sides. Stereo requests a two-channel mix, including a native downmix for supported multichannel sources. Original follows the source/output default. This preference applies to the current session, later sources and selected outputs; pause and position are retained when saving settings. It remains global rather than part of an output profile.

Earlier 2.1/5.1/7.1 settings were stored without configuring a speaker layout; they migrate to Original. Explicit speaker layouts, LFE management, arbitrary channel matrices, per-channel gain/delay and certified surround output remain pending. Actual routing is listed in **Signal path and information** and can depend on the source and output capabilities.

Open **Settings > Audio > Audio output profiles** to save the player's current volume, full 10/15/20/31-band equalizer layout, preamp, gain compensation and audio delay for a selected output. Up to 64 profiles are keyed by output identifier; saving for one device leaves other profiles independent. Saved devices remain listed when disconnected. **Refresh devices** updates availability.

Enable automatic application to recall a saved profile when selecting that output or starting FluxPlayer with it selected. With automatic application off, **Use output and saved profile** still applies it explicitly. Profile changes preserve mute and respect the global volume-boost limit. Changes made afterward do not overwrite a profile until **Save current settings** is chosen again. Removing a profile retains the current audio settings. The System default profile follows Windows' current default; automatic physical-device/hotplug profile switching remains pending. ReplayGain and other effects keep their global settings.

Open **Settings > Files and tags > Batch tag editor** to edit the current queue, or select several library rows and choose **Edit tags**. Add or remove files from the job, then check only the title, artist, album or genre fields you want to replace. A checked empty field clears that tag; unchecked fields are preserved. **Apply checked fields** writes to the original files and shows saved, skipped and failed results. Loaded files, missing files and network sources are skipped. Cancellation finishes the current save and stops before the next file; completed changes remain. Close the editor to cancel an active job. Keep backups of files before making large changes.

## Subtitle timing

Open **Settings > Subtitles > Subtitle timing editor**. The **Live delay** controls affect the currently selected subtitle track; positive milliseconds display subtitles later. Load an external SRT, VTT, ASS or SSA file to compare its original and adjusted timestamps. Select a row and use **Seek selected original cue** to navigate using the current live delay.

Export correction is separate from live delay. Set an offset and time scale, then choose **Update table**. For drift correction, pause the video at the matching time for an early cue, select that cue and capture the first anchor. Repeat with a later cue and the second anchor. The editor calculates a positive scale and offset from those two matches.

**Save corrected SRT copy** preserves the original. Text ending before zero is omitted; a partially visible cue is clipped to zero. The copy is plain text: ASS/VTT styling is not retained. **Load saved copy in player** selects it and resets live delay to zero, preventing a second timing adjustment. External subtitle tracks appear in the selector as they are discovered; choosing Disabled remains respected.

## Subtitle styles

Open **Settings > Subtitles > Subtitle style profiles** to set a default font family, font size, text/outline colors, outline thickness, bold text, shadow and bottom margin. A size of 0 uses automatic sizing; other sizes and margins are in video pixels. Outline thickness is a percentage of the font size. Choose **Use this style**, or enter a name and **Save profile**. Up to 32 profiles are stored in the active data profile. A saved profile can be selected, edited or removed; removing it keeps the current default style.

Restart FluxPlayer to apply a changed style. Embedded text styling and bitmap subtitles may override these defaults. Global italic and top/center placement overrides remain pending. Subtitles prefer the requested languages and then accept an unlabeled track; include `none` in the language list to stop that fallback. Disabling **Show subtitles by default** selects no subtitle track until you explicitly load or select one.

With the current WPF memory surface, an active subtitle on an opaque Windows hardware-decoder surface switches that source to software decoding so the text is visible. Position, pause and externally loaded subtitles are preserved. **Signal path and information** identifies this profile. This compatibility path can increase CPU usage; accelerated subtitle composition in this surface remains pending.

For local lyrics, place an LRC or text file beside the media with the same base name. An LRC file displays synchronized lines. A matching CUE sheet provides track chapters where supported.

**Settings > Audio > Create or edit CUE sheet** opens the track editor. Add source files, add the current playback position, or load an existing sheet. Edit titles, artists, source paths and `minutes:seconds:frames` start positions in the table; use Move up/down to arrange tracks. There are 75 frames per second and a maximum of 99 tracks. Play selected track opens its source at the indicated position.

Save as suggests an edited copy. The writer uses relative source paths, checks increasing positions within each source and rejects invalid text or an accidental overwrite. This editor writes audio `FILE`, `TRACK`, `TITLE`, `PERFORMER` and `INDEX 01` entries. Advanced directives such as CD-TEXT, PREGAP and other indexes are not retained, so keep the original when editing a more complex sheet.

## External chapters

Open **Settings > Playback > Chapter editor and external chapters**. Load an OGM simple chapter file (`.txt`/`.ogm`) or Matroska chapter XML, or copy the player's existing chapter points. Add a row or the current playback position, then edit titles and `hours:minutes:seconds.milliseconds` start times directly in the table. **Find** filters the draft by title or time; **Sort by time** orders it before validation. Saving and applying use the full draft, including rows hidden by Find.

**Apply to current media** updates navigation for this session. Use **Settings > Playback > Chapters** or **Seek selected in player** to jump to a point. The menu shows up to 200 entries; the editor can search all 10,000 supported points. Times must increase without duplicates and stay before the known end of the media. **Restore native chapters** restores source navigation while retaining the editor draft. Opening a different source clears session-only external chapter points.

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

Load external subtitles or choose an embedded track from the video controls. Adjust subtitle and audio delay when timing differs from the picture. External subtitle text search supports SRT, VTT and ASS and lets you jump to a matching cue. Provider subtitle downloads, local OCR with reviewed SRT/WebVTT export and offline bilingual burn-in are available through their dedicated subtitle panels. Provider credentials, installed OCR languages and FFmpeg availability determine the usable workflows.

Bookmarks, snapshots and A–B clip export are available through the video tools and context menu. Copy-based clip export depends on container and keyframe boundaries; transcoding takes additional time.

**F12** saves a native video frame in the selected PNG, JPEG or BMP format. Captures use unique names and refuse to replace an existing image. Image encoding and file I/O run outside the UI dispatcher. Capturing adds processing and storage work while the request runs.

**Settings > Video > Scheduled snapshots** starts an explicit capture job. Choose an interval, image limit, format and folder, then select Start capture job. It captures only while video is playing, skips paused/audio-only/busy intervals, and stops at the image limit. Stop or close the window to cancel; completed images remain available. No capture job runs merely because the tool is installed or its window is opened.

## Convert audio

Install FFmpeg separately, select Batch audio converter from Settings > Audio, add source files, choose an output profile and destination, then start the job. Jobs run sequentially and can be canceled. Existing destination files are not overwritten. Optional loudness normalization changes the signal; lossless encoding does not undo losses already present in a source.

## Advanced and custom metadata

Open Settings > Files and tags > Batch tag editor. The single-file tag dialog also links to this selective editor. Add files, select exactly one row and choose Read selected file tags to inspect its metadata. Reading fills the inputs and unchecks every field; it does not modify the file or automatically authorize applying its values to other rows.

Advanced metadata fields contains album artist, composer, year, track number/count, disc number/count, BPM, conductor, grouping, copyright, comment and embedded lyrics. Check only the fields to change. Unchecked fields are preserved; a checked empty text field clears its value. Empty numeric fields clear to zero. Year accepts 0–9999; track/disc numbers and totals accept 0–65535; BPM accepts a whole number from 0–1000. BPM editing does not calculate tempo. Text fields support 4096 characters; comment and lyrics accept line breaks. Embedded lyrics editing is separate from synchronized LRC files.

Custom embedded fields supports MP3 ID3v2 and FLAC/Ogg Vorbis/Opus comments. Check Apply these custom fields and enter one NAME=value per line. A name starts with a letter and uses up to 48 uppercase ASCII letters, digits or underscores; values support up to 4096 characters on one line. Up to 32 fields can be changed per job. NAME= removes only that named field; omitted names remain untouched. Values may contain equals signs and Unicode. Fields are stored with a FLUXPLAYER_ prefix, so a custom TITLE field does not replace the standard title. Other applications may display that prefix. Other containers reject a custom-field patch before saving any fields.

Save field template exports the currently checked fields and values as a new JSON file. Existing files are never overwritten. Load field template replaces the draft's checked fields after complete validation; review the result before Apply checked fields. Templates contain no media paths or executable actions and have a 2 MB input limit. They preserve omitted fields versus explicit clearing, including multiline lyrics. Loading or saving a template does not edit media files.

Jobs remain sequential and skip loaded/currently selected sources. Cancel stops before the next file; a write already in progress finishes. Every save is reread to verify requested values. A format that cannot retain every field can report a failure after supported edits have been written; these are not transactional undo operations. MP3, FLAC, Ogg Vorbis and Opus advanced/custom edits were verified along with unchanged decoded audio and preserved cover bytes. Existing ID3v1 common fields can be read and edited within that format's length/encoding limits; ID3v2.3 and 2.4 retain Unicode and custom text. Arbitrary tag-frame editing, online metadata matching and nested library collections remain separate pending work.

## Browse media folders

Open Settings > Library > Browse media folder. Choose folder opens a directory picker; Up one folder goes to its parent. The tool starts in the current local media's folder or the last opening directory when available. Double-click or press Enter on a folder to enter it, or on a media file to play it. Open / Play selected starts playback even when automatic playback on opening files is disabled. Add selected to playlist and Add all visible media enqueue without starting playback. Missing files are skipped.

All media, Audio and Video filter by the supported extension catalog. Filter filenames narrows file names without hiding navigation folders. The current directory is read on a worker; media is not decoded to populate the list. Refresh rereads it. Each view is limited to 10,000 entries and reports truncation or inaccessible entries; it does not recursively scan subfolders, watch changes, move files or populate the library index. A matching extension alone does not guarantee that its contents can be decoded.

## Browse artists, albums and personal labels

Open Media library from Settings > Library or its title-bar shortcut. The Artist, Album and Personal label selectors show exact indexed values and matching file counts. Choose an artist to scope the album list, then optionally choose an album and label. Choosing a different artist resets album and label; choosing a different album resets label. Clear browsing resets these three selections while preserving the search and collection filters.

Names match without case sensitivity and artist/album names ignore surrounding whitespace. An artist's complete indexed text is one value; multiple performer names are not split into separate artists. Identical album names are grouped together unless an artist is selected. Unknown artist/album means an empty metadata value. No personal labels selects unlabeled files. Literal names such as Unknown artist remain separate from empty metadata.

Browsing combines with search rules, Audio/Video, Favorites, Recently played and the other collection filters. Counts are the number of matching files, not unique artists or albums; a file with several personal labels is counted once in All labels. A selected value remains visible with a zero count if another rule excludes all its files. Save view as playlist retains all three exact criteria; reopening it and restoring library backups preserve them. Older saved playlists keep their original scope.

Only existing index metadata is used. Use Read tags to enrich selected files, or up to 500 unanalyzed files in the current view. Opening a selector does not parse media or contact an online service. These controls use the same Fluent theme and virtualized lists as the library.

## Rename files in batches

Open Settings > Files and tags > Batch file rename. Local playlist files are included initially; Add files extends the selection up to 10,000 files. Use `{filename}`, `{title}`, `{artist}`, `{album}`, `{albumartist}`, `{genre}`, `{year}`, `{track}`, `{disc}` and `{index}` in the template. The index starts at 001. Embedded tags are read when previewing, with playlist metadata and the original filename as fallbacks. Missing artist/album values become Unknown artist/Unknown album. Invalid characters in metadata become underscores.

Select Preview names and review the destination names; hovering shows each full path. Editing the template invalidates the preview. Rename previewed files keeps each file's folder and extension. Existing destinations are never overwritten; duplicate destinations or changed source size/modification time reject the entire preflight. A later per-file failure is reported without undoing completed files. Loaded or currently selected sources are skipped. Case-only changes and swaps are not supported.

Successful renames update indexed favorites, labels and play statistics, playlist/play-next entries, recent paths, bookmarks and saved resume paths. Cancel job stops before the next file; completed renames remain. A library-save warning means files have already moved even if index persistence failed. Sidecar artwork/lyrics/subtitles, embedded tags and external playlists are not renamed or rewritten. Renaming changes names within their existing folders; use the organization tool for folder moves. There is no undo operation.

## Local data

Settings, bookmarks, recent files and library data are stored under the current user's FluxPlayer application-data folders. Engine diagnostics, when enabled, are local files and can include media paths or stream URLs. Review them before attaching a report. The repository and CI do not include personal settings or media files.

## Settings and tools

Use the single title-bar gear or Ctrl+, to open Settings. Actions are grouped within Playback, Audio, Video, Subtitles, Library, and Files and tags. Audio includes output/processing, CUE documents, lyrics and conversion. Playback contains chapters and signal information; Video contains capture and picture orientation. Library contains browsing, collections and session history. Files and tags contains metadata editing, renaming and folder organization. Opening an action saves current preferences; a failed save keeps Settings open. New actions require an explicit category and section in the shared action registry.

## Edit synchronized lyrics

Open Settings > Audio > Lyrics editor. Open a UTF-8 LRC file or load the current local media's same-name sidecar. Edit text and [minutes:seconds.milliseconds] stamps; Insert current time places a stamp at the caret. Repeated timestamps and integer offset metadata are supported. Apply to current media previews the validated draft for this session; opening another source restores its own lyrics. Save LRC copy writes a new .lrc file and refuses to overwrite any existing file, including the original. To discover the saved copy automatically on a later opening, keep it beside the media with the same base name. The editor supports 2 MB and 10,000 synchronized cues. It does not download lyrics, edit embedded lyrics, or retain enhanced word-level timing.

## Organizing media into folders

Open **Settings > Files and tags > Organize media files** for local playlist files, or select library rows and choose **Library tools > Organize selected files**. Choose an existing destination root on the same drive/share as the sources. A folder template such as `{albumartist}/{year}/{album}` generates subfolders automatically; the separate filename template defaults to `{filename}` and always preserves the extension. Both templates accept the ten tokens listed above. Track/disc numbers use at least two digits and the selection index starts at 001. Missing tags use the playlist/library fallback where available, then Unknown artist/album/genre/year or 00 for missing track/disc. Album artist falls back to artist.

Choose **Preview names** and review the paths relative to the chosen root; tooltips show complete paths. Preview reads tags but creates no folders and moves nothing. Applying rechecks source size/modification stamps and all destinations before starting. Existing destinations, duplicate names, path traversal, cross-drive moves and symbolic-link/junction paths are rejected. Metadata characters unsuitable for Windows names are replaced with underscores; reserved device names get a leading underscore. Templates support up to six folder levels and 10,000 files per task.

Loaded media is skipped. Successful moves retain encoded media bytes and indexed metadata, labels, favorites and statistics. Playlist/play-next/recent paths, bookmarks, resume keys and per-file playback preferences follow the new location. Cancel stops between files; completed moves remain, and folders created before a later failure may remain. This is an explicit, sequential task with no undo or background organization. Sidecars, cover files and external playlist contents are not moved or rewritten; linked lyrics/art may therefore need to be relocated separately. The tool does not copy across drives or remove empty source folders.

## Customizing library columns

From **Settings > Library > Media library**, choose **Library tools > Columns** to show/hide the 14 predefined columns. Title always remains visible. Optional columns include Genre, Folder, Size in bytes and Added/Modified/Last played dates in UTC. Drag headers to reorder, drag header edges to resize, click a header to sort, and Shift-click another header for additional criteria (up to three saved). Duration and size sort numerically. Layout and sorting survive filtering and are saved when closing the library.

**Save column view** stores the current columns, widths, order and sorting under a name of up to 80 printable characters. Save up to 16 names; saving an existing name replaces that view. Use **Restore column view**, **Remove saved column view**, or **Reset columns and sorting** from Library tools. Search, collection and artist/album/label criteria remain separate smart-playlist rules. A restored column sort takes precedence over the collection's initial order. These views configure predefined indexed columns; they do not define computed columns or formulas.

## Nested library collections

Open **Settings > Library > Library collections**, or **Manage collections** in the library. Create a name and choose Root or a parent. Select an existing row to rename or reparent it; New collection starts a draft under the selected parent. Names are unique among siblings, with up to 120 printable characters. Trees support up to 1,000 collections and 32 levels. These are virtual groups: files stay in their current locations and can belong to several groups.

In the library, choose a specific collection from the collection dropdown. Select rows and use **Library tools > Add selected to current collection** or **Remove selected from current collection**. Membership edits accept up to 10,000 selected indexed files, with 100,000 total memberships across groups. Include child collections shows a deduplicated union of descendants; clearing it shows direct members only. Dropdown counts show direct members. Artist/album/label/search rules intersect with the collection filter. Save view as playlist retains the collection and the descendant choice.

Library backups include the hierarchy and members. Restoring merges stable identities and matching sibling names, unions memberships and remaps imported scoped playlists. Renaming or organizing an indexed file updates its memberships. Removing a library entry prunes its references. Removing a collection tree retains media and index entries; first remove any smart playlists scoped to that tree. Collection writes are immediate and revert their in-memory change if storage fails. This is local grouping, without cloud synchronization or automatic classical-work/movie-franchise metadata.

## Advanced CUE document editing

Use **Settings > Audio > Advanced CUE document editor**. The basic CUE editor also redirects imports containing comments, album-level metadata or extended directives to this preserving editor. Choose UTF-8 / Unicode BOM or explicit Windows-1252 before opening. The draft preserves CD-TEXT fields, CATALOG/CDTEXTFILE, ISRC, FLAGS, PREGAP/POSTGAP, INDEX 00/01/additional indexes, REM comments and unknown directives. Edit the text, then Validate and list tracks before navigation or export. Validation reports line numbers for malformed known directives. Unknown directives are retained verbatim and are not interpreted.

The editor supports 4 MiB, 20,000 lines and 99 increasing track numbers. Timestamps use minutes:seconds:frames, with seconds 0–59 and frames 0–74. Track indexes increase in number and do not run backward within a FILE; INDEX 01 is required. AUDIO preview opens the source at INDEX 01. Data-track declarations remain editable but are not offered as audio playback. Gaps and PRE/4CH flags are preserved metadata, without silence synthesis, de-emphasis, CD burning or a gapless-playback guarantee.

Save UTF-8 copy always creates a new .cue file in an existing folder. It refuses the original, any existing destination and referenced source paths. FILE/CDTEXTFILE paths are rebased for the new folder so they continue to refer to the same media and CD-TEXT files; those files are not copied or changed. Other directives, whitespace and line endings are retained. Missing referenced files can be repaired in the draft before validation/navigation. Source documents are never rewritten by the advanced editor.

## Conversion output parameters

In **Settings > Audio > Batch audio converter**, select a profile and adjust Sample rate, Output channels, Bitrate and PCM depth. Preset/source defaults keep the existing profile policy; explicit values override it. Sample rates range from 8 to 192 kHz where the encoder supports them. MP3 is limited to 48 kHz, AAC to 96 kHz, and Opus exposes its 48 kHz playback clock. Channels can keep the source, downmix to mono or produce stereo. Lossy bitrate choices replace the profile bitrate/quality parameter; lossless profiles disable that control. WAV supports 16/24/32-bit integer PCM; FLAC supports 16/24-bit output. Other profiles keep their preset depth.

Unsupported combinations are rejected before starting or reported by FFmpeg without publishing a partial destination. MP3 below 32 kHz requires an explicit bitrate of at most 160 kb/s. Loudness normalization can be combined with the requested output rate/depth. Raising frequency or bit depth does not recover information absent from the source. These options process newly converted files; they do not change the current player's output format or provide real-time DSP resampling. FFmpeg remains optional and runs only for an explicit job.

## Frequent actions from the player context menu

Right-click the playback surface. Play includes workspace mode (Automatic/Audio/Video), shuffle, repeat, chapters and signal information. Audio includes mute, current audio tracks, output device/profiles, ReplayGain and channel routing. Subtitles includes current tracks (including Disabled), timing and style actions. Video includes picture orientation alongside the existing aspect and picture controls. Library offers browsing and collection shortcuts. Editors and batch jobs remain in their Settings categories.

Checked items reflect the current choice whenever the menu opens. Track selection applies to the currently loaded source. Routing changes apply immediately and preserve playback, mute and volume. ReplayGain mode is saved for the next source opening; changing its preference does not rebuild the active decoder. ReplayGain signal-level measurements remain pending.

## Playback session history and listening totals

In **Settings > Library**, enable **Keep playback history** to record future source openings. It is off by default. Choose 1–5,000 sessions and 1–3,650 days (defaults: 1,000 sessions and 90 days). Limits filter the displayed view and prune the stored file at the next session checkpoint. Turning recording off retains existing records. Enabling it during playback starts recording with the next source opening.

Open **Settings > Library > Playback history** to search title, artist or recorded path, filter Audio/Video, sort columns and inspect session state, UTC start, duration and listened time. The footer totals sessions and listening time in the filtered view. Listening time uses elapsed playing time: pauses and seeks do not count as listening. This records player activity rather than proof of an audible or completed listen. Metadata and positions are snapshots, saved at playback transitions, explicit refresh and orderly shutdown; a crash may lose activity since the last checkpoint.

Select a row and choose **Play from start** or **Resume selected** (also double-click). Near-end positions restart from zero. Files must still be available at their recorded path; history does not relocate moved files or silently find substitutes. Export view copy writes a new JSON file containing the filtered view and refuses overwrite. Clear all history removes all recorded sessions and discards the active history session after confirmation; later openings can be recorded. It does not clear media, favorites, bookmarks, per-file preferences or the queue. History is local to the selected data profile, with a 32 MiB store bound.

## Several saved playlists

Open **Settings > Library > Saved playlists**, or right-click the player and choose **Library > Saved playlists**. These ordered lists are separate from smart playlists (which save library rules) and the active playback queue. Create a list, append the current queue, add files, import an M3U/M3U8/PLS/XSPF, or save a new playlist copy. Names are unique without letter case. Changes save immediately in the current profile.

Search matches names, track titles and paths across every saved list. Select a matching list to see matching entries; a matching list name shows its full contents. Clear search and select one row to move it up/down. Sort by title orders the entire saved list; Remove exact duplicates keeps the first occurrence of each identical path. Repeated entries otherwise remain valid. Remove selected entries affects the selected original rows, including when search hides other entries.

**Lock playlist** blocks name changes, entry edits and removal until explicitly unlocked. Playback and export remain available. This is protection against accidental edits, not encryption or file access control. Revision checks reject an edit made from an outdated catalog view; Refresh loads the latest contents. Removing a saved list does not remove media or alter the playback queue.

Play selected appends the chosen entries and starts the first added one, even with autoplay off. With no track rows selected it uses the entire selected list. Add to playback queue appends without starting playback. Missing local files remain in saved lists for later repair but are skipped during playback. Local paths are absolute; renames and moves outside this editor do not repair them automatically. The catalog supports 200 lists, 10,000 entries per list, 100,000 entries overall and a 32 MiB store. It loads when requested, without a startup scan or recording timer.

## Named playlist copies

The saved-media editors preserve titles and ordering in UTF-8 M3U8, numbered PLS and XSPF imports/exports. Relative local paths resolve against the imported document's directory, including unavailable files. Export creates a new file and refuses overwrite. These copies contain titles and sources; they do not carry list protection, smart rules, radio favorites or country/genre fields. Existing playback-menu playlist import retains its compatibility behavior of skipping unavailable files.

## Your internet radio library

Open **Settings > Library > Internet radio**, or the player context menu's Library submenu. New station starts an empty draft. Enter a name and a direct HTTP, HTTPS, RTSP or MMS stream URL, optionally add genre/country and Favorite, then Save station. Search matches all four text fields and Favorites only narrows the view. Select a station to edit it, append it to the queue, or explicitly play it. Saving, browsing and enqueuing do not contact the stream. Playback uses the existing decoder and automatic audio/video detection.

Import playlist adds addresses and titles from M3U/M3U8, PLS or XSPF. An existing address retains its favorite and descriptive fields. URL path/query letter case remains significant. Invalid addresses or a merged total over 1,000 stations reject the whole import. Export new playlist copy saves the currently matching station view, preserving names and URLs. No radio directory, account, station search provider, download, recording or stream-availability monitor runs automatically. Tested playback includes a real HTTP audio stream served by a local fixture; other protocols/codecs depend on the native engine and source. Embedded URL credentials are not accepted by the station editor.

## Browse genres and decades

The library now has Genre and Decade selectors. Genre scopes available decades, and both scope artist/album/label browsing. Counts derive from indexed metadata, without reopening every media file during a search. Unknown genre and Unknown year are explicit choices. Year comes from the media date tag; older indexes show unknown until the file is played or Read tags is requested. The optional Year column sorts numerically.

Save view as playlist retains genre and decade together with the other smart-playlist rules; library backup/restore carries those fields and indexed years. Clear browsing resets all five browsing criteria while keeping the text and collection filters. Search rules include `year:>=1990 year:<2000`, `decade:1990s`, and `-decade:2000s`. A stored year of 0 is unknown (`decade:-1`). New static playlists and radio entries belong to media-catalog.json, separately from the library-index backup.

## Podcasts, audiobooks and additional playback features

The [extended media guide](EXTENDED_MEDIA.md) covers ten additional capabilities: RSS/Atom podcasts, offline episodes, OPML, audiobooks, listening statistics, personal ratings, external audio, video crop, picture presets and named playback sessions. It lists their categorized settings/context access, limits, progress checkpoints and profile data locations. Each capability has its own implemented entry in [the public feature checklist](../FEATURES.md).

## Audio effects, measurements, video copies and schedules

The [processing guide](AUDIO_VIDEO_PROCESSING.md) explains thirty additional capabilities, their categorized settings and selected context-menu access. It covers native parametric EQ and effects, source measurements, parallel conversion, audio joining, video/mobile exports, stereo eye selection, zoom/pan, vertical flip, one-time playback tasks and gradual volume fades. Effects start disabled and offline jobs run on request.

## Virtual edits, recurring tasks and library automation

The [automation guide](PLAYBACK_AUTOMATION.md) covers thirty further capabilities: skip/mute/ordered source segments, declarative edit documents, bilingual external text subtitles, contact sheets/ringtones, custom aspect, counters, OSD/toolbar preferences, gestures/global keys, weekday schedules/priorities/alarms, smart local queues, computed ratings, remaining-time sorting, reviewed metadata transformations, moved-file repair, native logical-channel matrix and adaptive RMS leveling. It gives category paths, operating steps, limits and source-preservation behavior. The [batch checklist](THIRTY_MORE_FEATURES.md) lists every completed item.

## Photos, archives and portable music

The [local media guide](LOCAL_MEDIA_GUIDE.md) covers photo albums/slideshows, image zoom/rotation, reviewed ZIP media extraction, audio copies or conversions into device-accessible folders, and static SDR calibration/White Wash patterns. It lists category paths, preservation/cancellation behavior and supported limits.
