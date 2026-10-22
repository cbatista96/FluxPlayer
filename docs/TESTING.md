# Testing

The regression suite is an executable Windows/WPF harness. A failed assertion returns a nonzero process exit code. It creates synthetic media and isolated test data under the ignored `session-state/tests/` directory; it does not require a user's media library or modify their settings.

## Managed service checks

```powershell
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 -warnaserror
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --managed-only
```

The managed suite covers:

1. Format hints, supported source schemes and UNC path classification.
2. M3U/M3U8, PLS and XSPF round trips, Unicode, relative paths, numeric ordering and DTD rejection.
3. LRC timestamps, offsets and time lookup.
4. SRT/VTT/ASS cue parsing and markup cleanup.
5. CUE time conversion and chapters.
6. Library query fields, phrases, exclusions and comparisons.
7. Folder scanning, file watching, favorites, statistics, smart lists, backup export and persistence.
8. English defaults and migration of existing equalizer presets.
9. Backup restoration, repeated merge behavior, preserved statistics, opt-in folder monitoring, invalid input rejection and index-only removal.
10. Duplicate hashing, stale indexed sizes, equal-size different content, missing files, cancellation and preservation of originals.
11. Portable/named profile parsing, isolated settings, option validation and persistence of unset window geometry.
12. CUE creation/editing, all 75 frame positions, multiple sources, Unicode, relative paths, invalid output rejection and overwrite protection.
13. Bounded snapshot scheduling, skipped intervals, non-overlap and cancellation before capture.
14. Metadata patch validation and the distinction between omitted fields and explicit clearing.
15. Subtitle offset/drift correction, anchor validation, clipping before zero, long timestamps, Unicode SRT export, cancellation, overwrite refusal and rejection of excessive cue counts instead of silent truncation.
16. Named subtitle styles, strict color validation, settings normalization, case-insensitive persistence, native option mapping and the 32-profile bound.
17. Per-output audio settings, independent band-array copies, invalid profile rejection, persistence and the 64-profile limit.
18. Personal-label transactions, queries, backup merging, cancellation and rollback.
19. OGM/XML chapters, format bounds, encoding, duration validation and safe copy export.
20. Multiband centers, preset interpolation, detached arrays and persistence of 10/15/20/31-band layouts.

GitHub Actions builds on Windows and runs these checks without publishing artifacts or creating releases. Its test invocation deliberately avoids native playback, which requires an interactive output environment.

## Full local suite

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build
```

Run in a Windows desktop session. The full suite also checks decoded-track detection independent of file extension, transport behavior and real native playback. When FFmpeg is found, it additionally checks canceled job cleanup and converts eight profiles, then reads metadata and plays the outputs with LibVLC. Tag saving, artwork and lyric sidecar paths are exercised with synthetic inputs.

FFmpeg-dependent checks are conditional. A passing run without FFmpeg does not validate conversion. A managed-only run does not validate the native renderer or audio output. Results are written locally to `session-state/tests/results.json` and are not checked into Git.

With FFmpeg available, tag saves and subsequent LibVLC playback are checked for all eight conversion profiles (FLAC, WAV, MP3, Opus, Vorbis, AAC/M4A, ALAC/M4A and WavPack). Batch checks use synthetic FLAC copies: selected-field replacement and clearing, retained titles, multiple album artists, year, track number, comment, embedded PNG artwork and a custom Xiph field. They also exercise per-file progress, duplicate inputs, missing/network/loaded sources, a paused source, cancellation before and between files, and refreshed library stamps after a rescan. Manual UI review saved an artist on two test copies and independently read the files back with FFprobe.

## Native transport coverage

Output-profile coverage checks detached band arrays, independent endpoints, invalid records, case-insensitive persistence and the 64-profile limit. A native session verifies delay and volume, equalizer headroom, mute preservation, the global boost cap, automatic opt-out, explicit recall, startup restoration and failed-save isolation. Device volume reporting can settle asynchronously; the test waits for the native reported value. Manual UI review saved a profile for an enumerated headphone endpoint, applied it and independently checked the isolated settings file. Pending metadata and track refreshes also avoid a disposed engine during teardown.

Native DSP checks capture processed signed 16-bit PCM in memory, without using the speakers. Synthetic 31.25 Hz and 1 kHz tones check a flat EQ against bypass, approximately +12 dB band gain and −12 dB compensation. These are targeted response checks, not a complete frequency-response or distortion certification.

Extended native checks measure the first, middle and last centers of all three extended layouts, including 20 Hz and 20 kHz. They check flat unity, +6 dB gain, compensated headroom, a −20 dB cut and a +20 dB preamp, plus low-frequency response at 192/384 kHz. The native module must acknowledge the actual rate, channel count and applied settings. Live layout/profile/settings tests verify full band arrays and continued playback. See [Native DSP measurements](NATIVE_DSP.md) for the capture boundary, manual visual scope and filter-only cost probe.

Playback normalization/compression is explicitly pending. The previous per-media filter-option path failed a loud-tone acceptance probe and was removed from the active controls. To reproduce that known failure for future implementation, run `dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --probe-pending-audio-filters --filter="Pending audio filters"`. This optional acceptance probe is expected to fail until processing is implemented; it is separate from the regression suite for implemented features. Conversion loudness normalization is unaffected.

The native transport scenario also verifies actual PNG/JPEG/BMP file signatures, snapshot overwrite refusal and temporary cleanup, two scheduled native frames, audio/subtitle offset values and persistence, and playback from a restored queue with missing files removed. Frame stepping compares sampled pixels while playback remains paused; LibVLC's reported time is not guaranteed to advance on every single frame.

The generated MKV contains an internal SubRip track. The test disables that track and checks that a subsequent track refresh preserves the choice, then attaches a second SRT with Unicode, spaces and `#` in its filename. It checks the new track and selected ID in the bound list after playback's initial parsing has finished. Manual UI verification exported two corrected cues at 110% scale plus 500 ms, checked the file contents and original hash, applied a live delay, and loaded the saved copy with live delay reset.

Chapter coverage round-trips OGM text and Matroska XML, checks default-edition selection, hidden/nested import scope, Latin-1 XML declarations, millisecond conversion, malformed keys/times, duration bounds, external-entity rejection, depth/size limits and canceled/overwrite-refused exports. Native playback checks external point application, seeking, preserving navigation on an invalid draft and clearing session points on source changes. The generated MKV also verifies native chapter offsets are in milliseconds. Manual UI review loaded two points, edited a title, saved a separate copy and used the player menu to seek to 3 seconds while remaining paused.

The style scenario measures colored text pixels in the actual WPF display buffer on a black synthetic video. It checks different colors, sizes, bottom margins and installed font geometry, then confirms the native snapshot includes the subtitles. It requests hardware decoding and checks that only an actual opaque surface triggers compatibility fallback. A paused external subtitle, selected track and position survive the switch; Stop cancels pending restoration. Disabling subtitle defaults is checked natively. Manual UI review saved a red profile, restarted FluxPlayer and confirmed visible styled text in the main window.

On hardware capable of reproducing the VLC 3 DX11 blending problem, that scenario intentionally encounters native YUVA/DX11 diagnostics before switching to the compatible path. This is separate from a claim that the hardware path itself supports accelerated subtitle composition.

To rerun a focused group after a relevant change, append `-- --filter="Native subtitle styles"` to the test command. A filter that runs no groups fails instead of reporting an empty success.

Rapid source switching or teardown can emit native diagnostics on stderr about unsupported on-top behavior or prevented buffer deadlocks even when assertions pass. A passing regression run is not a complete decoder stress test or hardware/performance certification.

## Manual UI verification

Personal-label regression checks cover Unicode and duplicate names, multi-file add/remove, exact and partial search, concurrent edits, reopened storage, backup unions, cancellation, rejected limits and rollback on a blocked save destination. They confirm the original audio bytes remain unchanged. Manual UI review added two labels, filtered the library with `label:"Road trip"`, saved that rule as a smart playlist and independently checked the stored labels, query and media hashes. UI removal was not exercised; removal behavior is covered by the service checks.

For a user-facing change, verify the complete affected flow in both workspaces:

- Open audio, video and audio with cover art; confirm automatic and manual mode selection.
- Switch files rapidly, pause, seek and reopen; check the timeline and transport state.
- Inspect the playlist, equalizer, library, converter and settings at common DPI settings.
- Confirm keyboard access, accessible names, focus visibility and reasonable window minimum sizes.
- Open fullscreen and mini player; verify the same playback session continues.
- Test relevant themes and materials on supported Windows versions.
- Compare startup and playback cost for changes affecting hot paths.

Do not report a visual inspection, codec matrix or performance benchmark as passed merely because compilation or service tests passed.
