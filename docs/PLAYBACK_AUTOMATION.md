# Playback automation, library workflows and presentation

This batch completes the [thirty-item checklist](THIRTY_MORE_FEATURES.md). The public catalog now contains **167 implemented / 21 pending / 188 tracked capabilities**; the private original inventory contains **480 covered / 1,210 pending or partial / 1,690 entries**. Twenty-eight capabilities receive F0161–F0188; bilingual subtitles (F0082) and playback normalization/compression (F0042) complete their existing IDs. Original list occurrences are counted separately from distinct public capabilities.

## Where to find the controls

Open Settings with **Ctrl+,**. New actions use the existing category registry, Fluent controls, Mica window chrome, labeled fields and virtualized preview tables. There is one Settings gear and no catch-all Tools page.

| Category | New controls |
|---|---|
| General → Player presentation | Playback OSD; Player toolbar |
| Playback | Virtual playback edits; Playback recommendations; extended Playback schedule |
| Audio | Ringtone export; Channel matrix; Adaptive RMS leveling under level/dynamics |
| Video | Contact sheet; Custom aspect ratio; Renderer statistics |
| Subtitles | Bilingual external subtitles |
| Library | Artist mix; Album mix; Computed ratings; Repair moved library files |
| Files and tags | Tags from filenames; Track numbering; Metadata case; Latin transliteration |
| Keyboard shortcuts | Global transport shortcuts |
| Mouse | Directional gestures |

The player context menu exposes selected playback, subtitle and video shortcuts, including virtual edits, bilingual subtitles, custom aspect and renderer statistics. Full editing and advanced preferences remain in the corresponding Settings categories. Toolbar overflow retains access to the existing optional controls in narrow windows.

## Virtual playback edits

Open a local source and wait for its decoded duration, then open **Virtual playback edits**. Enter one `start end` pair in seconds per line in Skip ranges, Mute ranges or Ordered segments, for example `12.5 18.0`. Save and apply validates intervals and stores the program in the active profile. Empty Ordered segments leaves normal source order with any configured skips and mutes. Nonempty segments play in their declared order and stop after the final segment; a segment reaching source EOF can continue at a later declared segment.

Skip and mute boundaries are end-exclusive. Same-type overlapping intervals and segments entirely covered by skips are rejected. An edit has at most 1,000 intervals in total and must fit the decoded duration. Mute combines with manual mute; leaving an automatic mute range or bypassing the edit preserves the manual setting. A saved edit is restored only for its unchanged source path, size and modification time. Stop resets the sequence cursor. A–B loops and intro/outro rules take precedence. Timing follows native notifications and seek precision; this is transport automation rather than a sample-accurate media renderer.

Import/export uses versioned `.fluxedl` JSON containing source identity and ranges. Imports become drafts; Save and apply commits them. The format cannot execute code. Exports create new copies and refuse replacement. Source media bytes remain unchanged.

## Copies and subtitles

**Contact sheet** samples a video into a new PNG using installed FFmpeg. Choose 1–8 rows/columns, at most 48 cells and a 120–480-pixel cell width. Each image fits a bounded even-height cell with padding, including portrait input. Decoding, seeking and available frames remain source-dependent.

**Ringtone** exports an explicit 1–120-second interval as WAV (16-bit PCM), MP3 or M4A (AAC), stereo at 44.1 kHz, with optional short fades. The interval must fit the decoded source. Both workflows run only on request, support cancellation, use the existing reduced-priority bounded-thread job runner and publish completed copies without overwriting source/destination files. FFmpeg and the selected encoder must be installed separately. Filter behavior follows the primary [FFmpeg filter documentation](https://www.ffmpeg.org/ffmpeg-filters.html).

**Bilingual external subtitles** loads two local SRT/VTT/plain ASS/SSA text sources and an independent secondary offset within ±10 minutes. An interval tree looks up overlapping cues against the existing playback clock. The WPF overlay displays both languages while suppressing the native subtitle track; clearing the overlay restores that track, and changing source clears the pair. At most eight active cues and 4,096 characters per track are displayed, with bounded/clipped overlay geometry. Bitmap subtitles, ASS styling/animation and exporting the bilingual overlay into video are outside this capability.

## Presentation and input

**Custom aspect ratio** accepts integer `X:Y`, reduces equivalent ratios and bounds the picture ratio to 0.1–10. The renderer and snapshots use the selected ratio alongside existing orientation/viewport transforms. The toolbar displays the custom ratio and retains preset choices.

**Renderer statistics** shows native decoded video/audio buffer totals, demux/read counts and WPF submitted/presented/coalesced frame totals. Its one-second refresh exists only while the window is open. Presentation counters describe renderer activity, not a physical display refresh measurement.

**Playback OSD** controls visibility, text size 12–32, duration 400–10,000 ms, Center/Top/Bottom position and ARGB text color. Apply validates and saves before publishing preferences. **Player toolbar** changes visibility and order of four optional groups: Subtitles, Picture, Loops and Capture. Core transport remains fixed. Full arbitrary button assignment, draggable HUDs and complete layout customization remain pending.

**Mouse gestures** is opt-in. Hold the right button on the video surface and move at least 48 pixels: left/right changes track, up/down changes volume. Short right clicks retain the context menu. Mouse capture is released after use; locked controls disable gestures.

**Global transport shortcuts** configures Play/pause, Stop, Previous, Next and Volume up/down. Each combination requires Ctrl or Alt, may use Shift and cannot use Windows or F12. Empty fields disable an action. No global shortcuts are registered by default. Actual OS conflicts reject the new mapping and attempt to restore the previous registrations; closing the player unregisters them. Windows ownership/conflict rules are documented by [Microsoft RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

## Recurring schedules and alarms

Playback schedule supports saved-playlist playback, Stop, fade, immediate volume and Alarm. Select zero weekdays for one-time execution or one or more weekdays for a recurring local-time series. The profile stores the time zone and wall-clock time. The first occurrence respects both the entered start date and weekday selection. Invalid or ambiguous DST occurrences are skipped rather than duplicated; choosing an invalid one-time date/time is rejected.

Tasks sharing a due timestamp dispatch from priority −10 through +10, followed by stable ID order. Higher priorities run last. A durable claim disarms each occurrence before execution, then completion rearms a recurring series at its next future time. Missed occurrences more than five minutes old are skipped. An abandoned recurring claim advances after the recovery bound without replaying an interrupted action. These semantics avoid duplicate transport actions after a crash; they do not promise transactional rollback of already dispatched native actions.

Immediate-volume tasks cancel an active fade and use normal volume/mute behavior. Alarm shows an owned nonmodal notice and plays a system sound. Dismiss closes the notice; Snooze creates a separate one-time task five minutes later, preserving the recurring series. FluxPlayer must remain open; no Windows scheduler, wake service or background agent is installed. At most 100 tasks are stored in the current profile.

## Library recommendations and ratings

Smart shuffle is an optional policy used **when Shuffle is also enabled**. It avoids the current file, recent files and consecutive matching artists when alternatives exist, with a fallback for small queues. Related autoplay is separate and opt-in: at queue end it ranks available indexed local audio by genre, artist and year, excluding the current queue and recent choices. It performs no online recommendation request or automatic metadata scan.

Artist mix and Album mix create a reviewed 1–1,000-track preview from available indexed audio. Artist groups interleave for variety. Album mixes interleave album groups while retaining indexed track numbers within each album; unknown track numbers fall back to title/path. Disc numbering is not inferred. Add mix to queue preserves stopped playback; Play mix explicitly starts the added queue.

Computed ratings require retained opt-in playback history and at least three audio sessions for each source. The rounded 1–5 score reflects average played-duration completion, with completed sessions scoring fully. Preview shows eligible unrated library sources; apply updates personal index ratings and preserves existing manual ratings and embedded tags. Results depend on retained history and do not infer ratings for unobserved files.

The optional Remaining library column sorts numeric remaining milliseconds using the current/resume position. Position values are captured once and projected with filtering on the worker thread. They update on library refresh, without a timer per row or native reads for every row.

## Reviewed metadata and moved-file repair

Each metadata mode selects up to 500 local files, previews current/proposed fields, then applies selected rows or all reviewed rows. File identity is rechecked before writing and loaded native sources are protected. Cancel stops between files; successful earlier writes remain if a later file fails. Existing batch-tag logic refreshes indexed metadata and preserves unrelated fields. Actual test fixtures verify that decoded PCM is unchanged.

- Filename patterns use distinct `{title}`, `{artist}`, `{album}`, `{genre}` and `{track}` tokens with literal separators, such as `{track} - {artist} - {title}`. Matching is anchored, escaped and time-bounded.
- Track numbering uses explicit selected-file order, start and increment, bounded to 0–65,535.
- Case transforms use invariant Title, Sentence, Upper or Lower casing on selected text fields.
- Latin transliteration removes Latin diacritics and expands supported ligatures/letters. Other scripts remain intact; universal phonetic transliteration is not implemented.

Moved-file repair first captures SHA-256 fingerprints **while indexed sources still exist**. After an external move, choose a folder and find matches. Search bounds traversal, skips reparse points/inaccessible folders, filters by size and hashes potential matches. Only unique byte-identical one-to-one matches enter the preview; ambiguous, changed and previously unfingerprinted files are omitted.

Apply reviewed repairs revalidates identity, refuses loaded sources, atomically changes library paths/collection memberships and relocates current queue, recent, resume, bookmark and per-file preference references. It does not move media. Separate saved playlists and sessions remain independent documents and are not rewritten. Fingerprinting and folder search run only on request and can be canceled.

## Logical channel matrix and RMS normalization

The Audio channel-matrix editor exposes 8 output rows × 8 input columns with identity defaults. Each finite coefficient is within −2..2; a row's absolute sum is at most four. The native processor uses the leading decoded logical channels, up to eight at 192 kHz. Actual stereo channel swapping is measured. This does not establish physical 5.1/7.1 speaker mapping or device bit-perfect output.

Adaptive RMS leveling is opt-in under Audio dynamics. Its linked-channel RMS envelope drives gain toward a −30..−6 dBFS target, bounded by 0–18 dB maximum boost and a 100–5,000 ms response. Silence gating, faster reduction and sample protection bound gain behavior; the existing compressor/limiter remains separately configurable. Tests measure quiet/loud PCM convergence and maximum gain. This is RMS normalization, without LUFS/EBU R128 or true-peak compliance claims.

Native protocol 3 appends matrix and leveling parameters to the previous configuration prefix. Disabled processing retains the bypass path. The [native cost checkpoint](NATIVE_DSP.md#matrix-and-adaptive-leveling-checkpoint) reports measured enabled costs; no feature is described as free of CPU/memory cost.

## Verification

Release x64 compiles with warnings treated as errors. All **84 full regression groups pass** (50 managed and 34 native/container groups). The nine focused batch groups also pass after the worker-projection, EOF-adjacent sequence and aspect-selector refinements. Local evidence: `session-state/tests/more-thirty-full-results.json`, `more-thirty-final-results.json` and `more-thirty-benchmark.txt`. Test sources are in `FluxPlayer.Tests/MoreThirtyChecks.cs`.

Checks cover actual Windows hotkey conflicts/cleanup, native PCM matrix and RMS behavior, source skip/mute/order/EOF-adjacent playback, scheduled volume/alarm dispatch, actual bilingual video text, actual writes for all four tag modes, decoded PCM preservation, three ringtone formats, tiled PNG dimensions, aspect-correct snapshots and renderer counters. Managed checks cover edit scripts, source stamps, policies, ratings, subtitle overlaps, recurrence/DST/priority/recovery and SHA-256 repair. A full hardware, theme, DPI, codec and physical multichannel matrix remains unmeasured. No GitHub publication or release is part of this batch.

Desktop review used the local Release executable with an isolated synthetic profile. It confirmed category placement in General/Playback/Files and tags/Subtitles; aligned weekday and priority controls; OSD preferences with successful apply and a more compact layout; actual current/proposed tag preview through the file picker; and Windows 11 styled toolbar checkboxes that toggle with one click and immediately hide the chosen group after Apply. Both generated subtitle documents were displayed together on the paused synthetic video, with readable centered text over the existing picture surface. No personal media was written or moved. The theme/DPI, physical gesture and device matrix remains outside this visual checkpoint.

Final refinements add worker-side remaining-time projection, linear lookup of fingerprint matches and excluded recommendation paths, source-EOF skip chaining, failed relink rollback, ambiguity/unknown-extension repair cases, non-Latin grapheme preservation, null hotkey rejection and deterministic cancellation of pending related autoplay on manual Stop. All nine affected groups pass after these refinements; the 84-group archive is the earlier full checkpoint. The latest Release build has zero warnings/errors. Final UI-only aspect-selection and Fluent checkbox adjustments are compiled and visually checked; no second full native matrix is inferred.

The isolated review profile is archived at `session-state/ui-profile-more-thirty-reviewed`; the development player exited normally and its temporary portable marker was removed. A real Settings save preserved the custom `239:100` ratio. The final compiled sizing adjustment gives longer custom ratios sufficient selector width. Normal user profile data and personal media remain untouched.
