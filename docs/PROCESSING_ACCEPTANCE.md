# Processing and subtitle acceptance checkpoint

The source-only status of **F0481–F0580** and the **100 processing algorithms F1081–F1180** is superseded by local acceptance fixtures. The other 400 operations in F0681–F1080 still require mpv/Kodi/MPD backend acceptance. This does not close 200 broad original requirements or certify all parameter combinations and hardware.

## Actual processing exports

`PaletteAcceptanceChecks` independently exercises all 150 palette algorithms: the earlier 25 audio/25 video operations plus the newer 25 audio/75 video additions. Each uses the actual application recipe/export path with its default parameters, a finite synthetic source and a new output file. A separate FFmpeg invocation decodes the output with errors treated as failures. Duration, nonempty output and unchanged source SHA-256 are checked. Installed backend: FFmpeg 9.0.2 Gyan full build.

The initial run found four defects, now corrected:

- `sab` used a nonexistent `lp` option; it now uses the documented `lpfr` option.
- Video `vectorscope` and `thistogram` needed explicit YUV format negotiation around the analysis filter.
- The installed FFmpeg crashed with `anlmdn`'s default 2 ms patch at 48 kHz. Explicit 1 ms patch/4 ms search windows execute successfully while retaining the non-local-means algorithm. No replacement executable was installed.

These fixtures verify execution and valid default exports, not a perceptual quality comparison, every legal parameter extreme or all recipe combinations. HDCD rendering against ordinary 16-bit PCM is not proof of HDCD marker decoding; no special source is falsely certified.

## Diagnostics and visualizations

`DiagnosticAcceptanceChecks` exercises all 20 diagnostic operations. Actual reports preserve kind/source/scope and bounded lines; JSON exports are parsed again. Known silence and black-frame sources produce the expected events. Spectrogram/waveform PNGs decode, animated visualizations independently decode, and frame manifests identify SHA-256. Sources remain unchanged.

This exposed an accounting error in the FFmpeg diagnostic tail: newline characters were excluded from the size limit. They are now counted, so the documented 128 Ki-character bound applies to the complete returned text. Spectrogram dimensions include its enabled legend; waveform dimensions are checked exactly. Diagnostic estimates retain their documented heuristic/excerpt scope.

## Subtitle operations

`SubtitleWorkshopAcceptanceChecks` has 30 individual action fixtures plus a shared safety group. Audits identify the intended problematic caption and do not flag a clean caption. Text edits check exact Unicode results; timing/structure edits check expected intervals, splits, merges, append offsets and frame alignment. Every edited result round-trips through both SRT and WebVTT. The source document remains unchanged. Invalid UTF-16, canceled transformations and preservation of existing export files are tested.

The five processing/diagnostic/subtitle panels also join the native dark/light WPF render checks. Other DPI combinations, keyboard journeys and subjective output acceptance remain outside these automated fixtures.

Machine-readable evidence is consolidated in [local-completion-test-results.json](local-completion-test-results.json). The initial failing run is preserved locally under `session-state/tests/palette-initial-results.json`; it is superseded by the passing final run.
