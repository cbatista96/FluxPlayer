# Testing

The regression suite is an executable Windows/WPF harness. A failed assertion returns a nonzero process exit code. It creates synthetic media and isolated test data under the ignored `session-state/tests/` directory; it does not require a user's media library or modify their settings.

## Managed service checks

```powershell
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 -warnaserror
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release -- --managed-only
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

GitHub Actions builds on Windows and runs these checks without publishing artifacts or creating releases. Its test invocation deliberately avoids native playback, which requires an interactive output environment.

## Full local suite

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release
```

Run in a Windows desktop session. The full suite also checks decoded-track detection independent of file extension, transport behavior and real native playback. When FFmpeg is found, it additionally checks canceled job cleanup and converts eight profiles, then reads metadata and plays the outputs with LibVLC. Tag saving, artwork and lyric sidecar paths are exercised with synthetic inputs.

FFmpeg-dependent checks are conditional. A passing run without FFmpeg does not validate conversion. A managed-only run does not validate the native renderer or audio output. Results are written locally to `session-state/tests/results.json` and are not checked into Git.

## Native transport coverage

The native transport scenario also verifies actual PNG/JPEG/BMP file signatures, snapshot overwrite refusal and temporary cleanup, two scheduled native frames, audio/subtitle offset values and persistence, and playback from a restored queue with missing files removed. Frame stepping compares sampled pixels while playback remains paused; LibVLC's reported time is not guaranteed to advance on every single frame.

Rapid source switching or teardown can emit native diagnostics on stderr about unsupported on-top behavior or prevented buffer deadlocks even when assertions pass. A passing regression run is not a complete decoder stress test or hardware/performance certification.

## Manual UI verification

For a user-facing change, verify the complete affected flow in both workspaces:

- Open audio, video and audio with cover art; confirm automatic and manual mode selection.
- Switch files rapidly, pause, seek and reopen; check the timeline and transport state.
- Inspect the playlist, equalizer, library, converter and settings at common DPI settings.
- Confirm keyboard access, accessible names, focus visibility and reasonable window minimum sizes.
- Open fullscreen and mini player; verify the same playback session continues.
- Test relevant themes and materials on supported Windows versions.
- Compare startup and playback cost for changes affecting hot paths.

Do not report a visual inspection, codec matrix or performance benchmark as passed merely because compilation or service tests passed.
