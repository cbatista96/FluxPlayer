# Processing and subtitle production — one hundred untested capabilities

**Current acceptance update:** all 100 F0481–F0580 capability IDs now pass the documented local fixtures. See [acceptance scope and limits](PROCESSING_ACCEPTANCE.md). The source-only statements below describe the earlier checkpoint.

The source batch **F0481–F0580** adds 25 offline audio processes, 25 offline video processes, ten audio diagnostics/visualizations, ten video diagnostics and thirty subtitle operations. All have status `implemented_unverified`: **no compilation, application launch, media rendering, diagnostic job, subtitle fixture test, performance measurement or visual review was performed for this batch**, as requested by the user. No new executable was generated and nothing was published to GitHub. Historical acceptance reports apply only to their original checkpoints.

[Individual verification checklist](PROCESSING_HUNDRED_CHECKLIST.md) · [Machine-readable operation/source mapping](processing-hundred-manifest.json) · [Full public inventory](../FEATURES.md)

These are individually selectable subfeatures of the original broad requirements. The batch does **not** close 100 original requirements. Broad DSP, quality, subtitle, spatial and diagnostic requirements remain partial; their earlier evidence is retained. Existing verified functions retain their status.

## Access and presentation

| Settings category | Section | Panel | Source capabilities |
|---|---|---|---|
| Audio | Offline effect recipes | Audio effect recipes | F0481–F0505 |
| Video | Offline filter recipes | Video filter recipes | F0506–F0530 |
| Audio | Signal and measurements | Audio analysis and visualizations | F0531–F0540 |
| Video | Picture diagnostics | Video quality diagnostics | F0541–F0550 |
| Subtitles | Quality and cleanup | Subtitle quality and cleanup | F0551–F0580 |

The main player still has one Settings entry. Audio analysis and subtitle cleanup also declare curated context-menu access through the existing `SettingsAction.QuickAccess` registry. Effects and diagnostic choices are inside their panels, not added as 100 context-menu items. Audio/video recipe editors further group choices by purpose. New windows use the existing Fluent/Mica workbench, labeled controls, collapsible sections, grouped actions, virtualized tables, cancel controls and work-area fitting. Their actual layout, scaling, keyboard behavior and theme rendering await visual acceptance.

## Audio effect recipes

Choose a local source, excerpt start/duration and WAV/FLAC output. Choose a family and effect, edit its numeric parameters, and add it to the ordered recipe. Steps can be selected, edited, replaced, removed or moved. Named recipes are profile-local; explicit JSON import/export permits reuse. Import loads data only: the graph is rebuilt from allowlisted effect identifiers and validated numeric values. Arbitrary executable/filter strings are never taken from recipe JSON.

The 25 processes include de-essing, derived dialogue reinforcement, Earwax headphone filtering, phaser/pulsation/tremolo/vibrato, transient shaping, harmonic excitation, soft clipping, audio contrast, explicit DC compensation, decorrelation, Haas/delay widening, denormal prevention, tilt/notch/band-pass/all-pass filters, custom stable biquads, piecewise/multiband companding, frequency translation and phase rotation. These are distinct algorithms/workflows rather than a separate feature count for every parameter or preset. Native playback DSP remains a separate path.

Bounds: 1–8 ordered steps, start within 24 hours, excerpt 0.1–600 seconds, 48 kHz stereo and 24-bit WAV/FLAC. Earwax explicitly uses 44.1 kHz internally and returns to 48 kHz. Dialogue reinforcement derives a center from stereo and folds it into stereo; it is not stem separation or AI voice isolation. The custom normalized biquad applies a second-order pole stability condition; this is not a gain/clipping guarantee. Float processing and intentional gain/nonlinear effects can still change or clip peaks on quantization. Review exported sound later before using it in a production workflow. Output-device, exclusive-mode and bit-perfect claims are unaffected.

## Video filter recipes

The same ordered editor exposes non-local means, adaptive temporal, wavelet, FFT and chroma denoisers; rank/bilateral smoothing; CAS; smart blur; gradient dithering; selective color balance/contrast/correction and RGB levels; color temperature; histogram/temporal normalization; deflickering; chroma registration; lens/perspective correction; rectangular logo interpolation; duplicate-frame dropping; temporal blending and decaying trails.

Exports are **SDR-oriented MP4 excerpts**, up to 120 seconds, first reduced to a maximum 1920×1080 while retaining aspect and even dimensions, then processed. Video uses H.264 and optional first audio uses AAC. Frame timing is variable-rate to preserve `mpdecimate` timestamps. Recipes containing `gradfun` request lossless H.264 (CRF 0), because subsequent lossy compression can destroy its dither; these files can be large and some devices do not support the resulting H.264 profile. Other recipes use CRF 21. No HDR transfer/tone mapping is inferred or certified here. Do not treat these edits as live GPU effects, shader support, AI enhancement or universal decoder compatibility.

At most two expensive denoisers and a combined declared temporal window of 40 frames are accepted per recipe; split heavier work into separate exports. `delogo` coordinates refer to the **scaled frame** and FFmpeg rejects rectangles outside it. Perspective coefficients are fractional source corners; they do not estimate a camera model. Noise, blur and sharpening settings require later visual review.

## Analysis and visualization

Select a local source, interval and one diagnostic. Reports use **Analyze excerpt**, then **Export current report**. Image/video/decoded-frame manifests use **Export visual / manifest** directly. Report export records the actual displayed report, including its source and interval even if the source fields have since changed.

Audio reports include silence intervals, channel statistics, spectral-feature metadata, phase correlation and DR estimates. Visual outputs are log-frequency spectrogram and channel-separated waveform PNGs, plus frequency bars, constant-Q spectra and stereo vectorscope MP4s. Visualization videos deliberately contain no audio. They are exported artifacts, not always-running visualizers in the main playback path.

Video reports include black/frozen intervals, field-order estimates, signal metadata, entropy, blur, blocking, scene changes and crop suggestions. Frame SHA-256 manifests hash **decoded frames**, not original media files. All report/PNG/frame-manifest intervals are at most 60 seconds; animated visualization intervals are at most 30 seconds. Reports keep the existing bounded 128 KiB diagnostic tail and up to its last 1,000 nonempty lines; early events can be omitted. They are not exhaustive whole-file reports.

Most video measurements run after downscaling to maximum 960×540 and crop coordinates belong to that scaled frame. Field analysis uses decoded source frames before scaling; frame hashes also use decoded source video. Audio summary paths decode at 48 kHz; reported sample statistics/bit depth describe the decoded representation. Phase and stereo visualizers negotiate stereo. Timelines are relative to the chosen excerpt; retain the start offset when relating them to the full file. DR, blur, scene, blocking and freeze measures are heuristic; they do not certify perceptual quality or physical hardware behavior.

## Subtitle quality, text and timing

Open SRT or WebVTT, choose **Quality review**, **Text cleanup** or **Timing and structure**, then choose an operation. Reviews populate Findings and do not edit captions. Go to finding selects its caption for manual correction. Edits produce an immutable in-memory document; Undo/Redo retains up to eight snapshots within an estimated 32 MiB history budget. Subtitle export creates a new SRT/WebVTT copy. Findings can also be exported as JSON.

Ten reviews cover exposure limits, reading speed, line budget, overlap, short positive gaps, repeated adjacent text, invisible control characters, basic-tag nesting and balanced dialogue brackets. Reading speed/columns use Unicode text elements, including spaces, rather than assuming ASCII byte counts. Bracket/markup checks are bounded syntax checks, not language-aware proofing.

Ten text edits provide NFC normalization, horizontal whitespace cleanup, bracketed-description removal, musical-note removal, exact duplicate removal, typography normalization, sentence-start capitalization, line reflow, visually empty cue removal and unwanted control removal. Explicit SDH stripping removes square-bracket spans and can remove intended dialogue; use Undo to review. Reflow strips basic italic/bold/underline markup and wraps on whitespace; overlong words remain intact. No automatic translation or OCR is implemented. Locale-sensitive capitalization and typography remain review tasks.

Ten timing/structure edits extend short cues, cap long cues, target reading duration, close small gaps, trim overlaps, extract/rebase an interval, split at sentence punctuation, merge adjacent equal captions, append another document and snap to a frame grid. Same-start overlaps require manual correction. Reading-duration fitting never shortens an already long cue; run the maximum-duration action separately if needed. Split timing is distributed by text length and is not speech alignment. Frame-grid timing is rounded to finite milliseconds and is not exact fractional-frame timecode.

Limits: 4 MiB UTF text documents, 20,000 cues, 8,192 characters per cue, valid ordered intervals within the existing 999-hour ceiling. SRT/WebVTT text and line breaks are retained; WebVTT styles, regions and cue settings are discarded. Timing requires three-digit milliseconds. Empty captions and blank lines inside a caption must be corrected before export, because blank lines delimit cues. Unsupported/malformed blocks fail instead of silently dropping dialogue. Basic markup checks cover `i`, `b`, `u`; full ASS/SSA authoring remains in the existing production editor.

## Execution and dependencies

New processing and analysis code uses the existing `FfmpegJob` and `OfflineMediaJob`: argument lists without a shell, below-normal process priority, two worker threads / one filter thread, bounded diagnostics, cancellation, source-version checks, staging and no-overwrite publication. Media jobs run off the UI thread and only on explicit action. No new decoder/device, startup scan, listener, timer, network service, package dependency or playback-time filter was added. Installed FFmpeg must contain the requested filters and encoders; missing support reports a job failure. No executable/tool availability probe was run in development.

Processing has a two-hour deadline; report analysis ten minutes; visualizations fifteen minutes. Cancellation removes owned staging files and preserves original files. These limits control scope; they do not prove zero overhead or real-time performance. Successful process exit and nonempty staging checks still require later decode/measurement acceptance.

Implementation reference consulted: [official FFmpeg filter documentation](https://ffmpeg.org/ffmpeg-filters.html), including the upstream `mcompand` parser. Documentation review is not runtime verification of the installed build.

## Deferred acceptance

Later acceptance must compile Release x64, check all 100 manifest mappings, run synthetic PCM/video/subtitle fixtures and independently examine outputs. Cover every filter, parameter extrema and combinations; missing filters/encoders; mono/multichannel and absent streams; short sources and nonzero offsets; unchanged original hashes; output collisions, canceled/deadline jobs and stale sources; recipe import limits; temporal memory/CPU use; variable-rate timing and dither-preserving exports; bounded-report truncation; subtitle Unicode/layout/timing round trips; same-start conflicts, undo/redo, empty captions and malformed import; Fluent/DPI/light/dark and context/Settings reachability. None of these deferred checks has been run for F0481–F0580.
