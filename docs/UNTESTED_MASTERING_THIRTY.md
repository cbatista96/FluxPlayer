> **Verification update — 2026-10-28:** All 30 capabilities in this document passed acceptance tests. The original deferral narrative below is historical and is superseded by [100 verified capabilities](VERIFIED_HUNDRED.md).

# Thirty multichannel, video-finishing and mastering additions — untested

**Source implementation only. This batch was not compiled, launched, exercised, visually reviewed, benchmarked or tested.** No media-processing command, encoder probe, source analysis or export was executed during development. The existing executable predates all four untested batches. Historical passing test results do not validate the current source.

The public catalog now contains **120 implemented-but-untested capabilities**, including these 30. The original megalist retains **492 previously verified entries, 109 implemented without testing, and 1,089 pending or partial entries**. The totals differ deliberately: eleven original format/playback requirements remain partial because a new export workflow does not establish playback/decoder compatibility. Their evidence links to this source work without marking them complete.

## Implemented — verification pending

Each unchecked box below means pending verification of the new code, not missing source implementation. Original references identify scope; an asterisk means the broader original requirement remains in Pending with partial-coverage evidence.

| Pending verification | Capability | Original reference |
|---|---|---|
| [x] F0291 | Offline bass crossover and LFE redirection | R0017 |
| [x] F0292 | Named input/output speaker layouts | R0018 |
| [x] F0293 | Independent output-channel gain | R0019 |
| [x] F0294 | Independent output-speaker delay | R0020 |
| [x] F0295 | Editable stereo-to-surround spread | R0024 |
| [x] F0296 | Arbitrary bounded multichannel routing matrix | R0269 |
| [x] F0297 | Two-source multichannel file mixing | R0270 |
| [x] F0298 | Multichannel PCM WAV / compatible FLAC export | R0247* |
| [x] F0299 | Requested double-precision processing stages | R0279 |
| [x] F0300 | Explicit post-matrix channel permutation | R0154 |
| [x] F0301 | Local 3D LUT grading and interpolation selection | R0947 |
| [x] F0302 | Software deinterlacing / field order | R0922 |
| [x] F0303 | Stereoscopic packing / anaglyph conversion | R0633 |
| [x] F0304 | Four-edge overscan crop | R1483 |
| [x] F0305 | Anamorphic sample-aspect ratio | R1482 |
| [x] F0306 | Exact image placement in an output canvas | R1484 |
| [x] F0307 | Nine-coefficient RGB transform | R0948 |
| [x] F0308 | Bounded excerpt luminance measurement and report | R0955 |
| [x] F0309 | Optional SDR code-value legalization | R0957 |
| [x] F0310 | Explicit BT.2020 SDR to BT.709 gamut conversion | R0813 |
| [x] F0311 | Conditional ProRes 422 HQ / PCM MOV export | R0760* |
| [x] F0312 | Conditional HEVC / AAC MP4 export | R0754* |
| [x] F0313 | Conditional AV1 / Opus Matroska export | R0755* |
| [x] F0314 | MPEG-2 / MP2 program-stream export | R0758* |
| [x] F0315 | Conditional MXF / DNxHR HQ export | R1333* |
| [x] F0316 | Stereo AC-3 export | R0761* |
| [x] F0317 | Stereo E-AC-3 export | R0762* |
| [x] F0318 | Always-RF64 PCM export | R0236* |
| [x] F0319 | Big-endian AIFF PCM export | R0237* |
| [x] F0320 | DXD-rate 352.8 kHz PCM export | R0242* |

## Categorized access and interaction

- **Settings → Audio → Multichannel mastering → Multichannel file mastering**: source/layout, routing, gain/delay/order, crossover and file format sections.
- **Settings → Audio → Production → Audio mastering formats**: five explicit audio export profiles.
- **Settings → Video → Finishing and color → Video finishing and color**: deinterlacing, stereo conversion, crop, canvas/sample aspect and color/LUT sections; excerpt measurement/report actions.
- **Settings → Video → Production → Video mastering formats**: five explicit video export profiles.

The four owned windows reuse the current Fluent/Mica workbench, standard themed checkboxes/fields, expandable sections, operation lock and separate cancellation button. A second click on an entry activates its existing window. These specialized file workflows are available through categorized Settings; the existing quick-access menu is not expanded with every export option. Visual layout, keyboard operation, theme contrast and scaling still need review after a future build.

## Multichannel processing

Choose a local primary source and optionally a second local source. Each is explicitly normalized to the chosen input layout and rate. Output layout choices are mono, stereo, 5.1(side) and 7.1. This controls the file graph, not the physical Windows device or a multizone session. The existing live stereo DSP is independent.

**Load routing preset / reset channel controls** builds a matrix for the selected layouts and resets gains, delays and permutation. Layout changes alone do not silently overwrite an edited matrix; a dimension mismatch is rejected when reviewing/exporting. Matrix rows represent output channels, columns represent normalized input channels. Coefficients are finite −2 to +2, with an absolute row sum no larger than four. An entirely zero row intentionally produces silence. The stereo-spread preset uses direct fronts, a summed center and difference-based surrounds; it does not reconstruct original discrete channels or spatial objects.

The optional second source is merged and summed before the matrix, with gain 0–1. Mixing ends at the shorter input; there is no automatic loudness normalization, resynchronization or source delay in this panel. Input normalization may downmix/upmix source material through FFmpeg; the chosen input layout is not a certified statement about the source's native channel order.

The optional crossover high-passes main speakers and redirects a low-passed average of main-channel content, plus existing LFE, into LFE. It requires a surround output and a selected 40–200 Hz cutoff. These are two-pole filters; room measurement, complementary acoustic summation, speaker calibration and phase alignment have not been established. Do not use the output for calibration without later measurements.

The post-matrix channel permutation contains each zero-based output index exactly once. Per-channel gain and delay apply to the physical output positions after that permutation: −60 to +12 dB and 0–2,000 ms respectively. Global headroom is −24 to 0 dB, initially −6 dB. Positive gains and summed routes can still clip the final integer PCM output. There is no automatic peak scan or limiter here.

The graph requests double-planar sample format, merge/pan stages, double-precision crossover filtering and expression gains before final quantization. Actual negotiated formats and every filter's precision need verification; this is not a complete decoder-precision or bit-perfect guarantee. Outputs are new WAV 16/24/32-bit or FLAC 16/24-bit files, at 44.1, 48, 96 or 192 kHz, with up to eight channels. Wider hardware routing and PCM playback-format certification remain pending.

## Video finishing

The panel is intended for SDR material. Use the separate offline HDR-to-SDR workflow first for PQ/HLG inputs; selecting a BT.2020 gamut transform is not tone mapping.

Deinterlacing offers Yadif, Bwdif or W3fdif with auto/top/bottom field parity and one output frame per source frame. The source must actually match the chosen field order. Stereo conversion accepts full/half-width side-by-side or full/half-height above/below, left eye first. Outputs are left/right mono, Dubois red/cyan, side-by-side or above/below. This creates a file; there is no VR headset, display refresh, glasses control or HDMI frame-packing integration.

The working raster is reduced to at most 1920 × 1080 after the initial deinterlace/stereo stages. Four independent 0–20% crop edges are then rounded to even pixels. Final canvas dimensions are even, 128–3840 by 128–2160. The image fits while retaining its coded-pixel aspect inside the remaining area at the selected even x/y offset, with black padding. A separate rational sample aspect, numerator/denominator 1–100, is declared on the final canvas; changing it deliberately changes display aspect.

The explicit gamut conversion interprets source primaries/matrix as limited-range BT.2020 SDR with BT.709 transfer and converts to BT.709. Otherwise BT.709 SDR is assumed. A bounded 3×3 RGB matrix precedes an optional .cube LUT. The cube loader accepts 2–65 grid points, at most 16 MiB, finite coefficients and a complete 3D grid. It supports trilinear/tetrahedral interpolation. An owned temporary directory and fixed `grade.cube` filename keep user paths out of filter expressions; only the owned file/directory is removed, without recursive deletion. Domain/title handling, LUT correctness and gamut behavior remain untested.

Final output is H.264/AAC MP4, 8-bit YUV420, BT.709 limited-range metadata, first video and optional first audio, without additional tracks/subtitles. Optional legalization clamps Y to 16–235 and U/V to 16–240. This deliberately clips code values and does not restore highlights. RGB/LUT stages use an 8-bit working format in this implementation; this is not a high-bit-depth grading pipeline.

Excerpt luminance analysis samples up to sixty frames at one-per-second from the first sixty seconds, reduces width to at most 640 and measures 8-bit `YAVG`. Its min/mean/max summarize **frame averages**, not pixel extrema, HDR peak, nits, calibrated monitor output or the entire file. A JSON report can be saved to a new filename and includes source path, size, timestamp and measurement scope. Changing the source field does not rewrite an already obtained report's identity.

## Mastering formats

All profiles use a selected local source and explicit zero-based audio stream. Audio exports require that stream; video exports map it only when available. Duration 0 means the full source, or select a 1–7,200-second source limit. Jobs have a two-hour wall-clock deadline, cancellation and no-overwrite publication. Encoder availability is not probed at startup or during development; missing encoders/muxers fail the requested export with FFmpeg diagnostics.

| Profile | Output scope |
|---|---|
| ProRes 422 HQ | MOV, `prores_ks`, 10-bit 4:2:2, 48 kHz 24-bit PCM |
| HEVC / AAC | MP4, `libx265`, 8-bit SDR, AAC, hvc1 tag |
| AV1 / Opus | Matroska, `libaom-av1`, 8-bit SDR, `libopus` |
| MPEG-2 / MP2 | MPEG program stream, 25 fps, 48 kHz MP2; no DVD authoring |
| MXF / DNxHR HQ | MXF, `dnxhd` encoder with DNxHR HQ, padded 1920×1080 at 25 fps, PCM |
| AC-3 | Stereo elementary stream, 48 kHz, 384 kb/s |
| E-AC-3 | Stereo elementary stream, 48 kHz, 384 kb/s; no object metadata |
| RF64 | Always-RF64 WAV, 48 kHz stereo 24-bit little-endian PCM |
| AIFF | 48 kHz stereo 24-bit big-endian PCM |
| DXD-rate PCM | 352.8 kHz stereo 24-bit WAV, RF64-auto |

Video profiles fit within a 1080p raster; MXF adds fixed-canvas padding. They are intended for SDR sources, exclude extra tracks/subtitles and do not perform HDR preservation. Audio is stereo. Increasing rate/bit depth does not recover missing source detail; DXD-rate export is resampled PCM, not native DSD conversion. No new playback codec/runtime is installed. Original format/decoder requirements remain partial until separately implemented/verified at their full scope.

## Source structure and deferred acceptance

`ChannelMastering`, `VideoFinishing` and `MasteringFormats` are called only by their explicit workbench actions. `MainWindow.Mastering` owns the panels. `FfmpegJob` now accepts an optional working directory for the fixed-name LUT job; existing call sites retain their default. New-copy publication and source size/time stamps use `OfflineMediaJob`. None of these panels is constructed at application startup. No new playback-loop work, scanner or timer was added. Performance has not been measured.

Deferred work includes compilation, graph/encoder negotiation, real multichannel impulse measurements, order/gain/delay correctness, crossover response, integer clipping, source preservation/cancellation/collision handling, LUT/domain/Unicode-path cases, stereo eye geometry, field parity, crop/SAR/canvas behavior, YAVG report semantics, independent output decoding and every panel's Fluent/keyboard/DPI review. These are future acceptance items, not completed tests. Existing passing results must not be reused as evidence for this batch.

Implementation references: official [FFmpeg filters](https://ffmpeg.org/ffmpeg-filters.html), [codec options](https://ffmpeg.org/ffmpeg-codecs.html) and [muxer options](https://ffmpeg.org/ffmpeg-formats.html). Documentation was consulted for source wiring; no command was executed to validate the installed build.
