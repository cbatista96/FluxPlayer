> **Verification update — 2026-10-28:** All 30 capabilities in this document passed acceptance tests. The original deferral narrative below is historical and is superseded by [100 verified capabilities](VERIFIED_HUNDRED.md).

# Thirty more processing capabilities — implemented, untested

This is the second untested batch. The later [external-control/capture/media batch](UNTESTED_EXTERNAL_MEDIA_THIRTY.md) brings the overall pending-verification queue to ninety; this document retains the scope of its own thirty processing capabilities.

**Source implementation only. No compilation, application launch, rendering job, test, visual review or benchmark was executed for this batch.** The user explicitly deferred testing. This follows the [preceding thirty untested catalog capabilities](UNTESTED_THIRTY.md), bringing the untested implementation queue to sixty. The previously built executable is unchanged.

The original-list requirements below are implemented in bounded offline/export or source-analysis workflows. They do not claim a new real-time playback renderer, certified audio fidelity, physical monitor control or general-purpose AI recognition. Their status is `implemented_unverified`; boxes track verification that still needs to happen.

## Implemented — verification pending

| Pending verification | Capability | Original |
|---|---|---|
| [x] F0231 | Low/high/band-pass FIR impulse design | R0264 |
| [x] F0232 | Offline impulse-response convolution | R0265 |
| [x] F0233 | Cubic / Blackman-Nuttall / Kaiser audio resampling filters | R0254 |
| [x] F0234 | Configurable rectangular/triangular 16-bit dither | R0261 |
| [x] F0235 | Shibata / low-Shibata / Lipshitz noise-shaped dither | R0262 |
| [x] F0236 | Two-pass processed sample-peak normalization | R0381 |
| [x] F0237 | Independent offline semitone pitch changes | R0389 |
| [x] F0238 | Independent offline tempo changes | R0390 |
| [x] F0239 | RIAA reproduction curve for appropriate recordings | R0281 |
| [x] F0240 | DC/subsonic cleanup | R0280 |
| [x] F0241 | Source FFT spectrum and peak-envelope analyzer | R0579 |
| [x] F0242 | Explicit sonic analysis of selected library tracks | R0514 |
| [x] F0243 | Track similarity from sonic descriptors | R0515 |
| [x] F0244 | Album similarity from analyzed-track aggregates | R0516 |
| [x] F0245 | Artist similarity from analyzed-track aggregates | R0517 |
| [x] F0246 | Bilinear offline video scaling | R0926 |
| [x] F0247 | Bicubic offline video scaling | R0927 |
| [x] F0248 | Lanczos offline video scaling | R0928 |
| [x] F0249 | Spline offline video scaling | R0929 |
| [x] F0250 | Aspect-preserving downscaling and no-upscale limits | R0918 |
| [x] F0251 | Offline video debanding | R0920 |
| [x] F0252 | Offline video noise reduction | R1058 |
| [x] F0253 | Small-shake stabilization attempt | R1059 |
| [x] F0254 | Motion-compensated frame interpolation | R1060 |
| [x] F0255 | Burned-in text and local-logo overlays | R1394 |
| [x] F0256 | Explicit offline HDR-to-SDR conversion | R0941 |
| [x] F0257 | Reinhard tone-mapping selection | R0951 |
| [x] F0258 | Hable tone-mapping selection | R0952 |
| [x] F0259 | Offline reference target luminance | R0958 |
| [x] F0260 | Input/output video color-range interpretation and conversion | R0960 |

## Categorized entry points

- **Settings → Audio → Offline processing**: Offline audio processing; FIR impulse designer.
- **Settings → Audio → Signal and measurements**: Source spectrum and waveform. This focused analyzer is also registered for the existing curated right-click menu.
- **Settings → Library → Sonic discovery**: Sonic library discovery.
- **Settings → Video → Offline processing**: Offline video processing.

The export editors organize their settings in named expandable sections. They reuse `FeatureWorkbench`, its Fluent/Mica chrome, standard styled fields and checkboxes, and a cancellation button outside the disabled job controls. This is design reuse in source; its visual result remains unreviewed.

## Audio workflow and limits

The audio editor creates a new stereo WAV (16/24/32-bit PCM) or FLAC (16/24-bit). Its graph applies selected resampling, an optional 20 Hz high-pass, optional RIAA reproduction, pitch/tempo processing, optional convolution, optional two-pass sample-peak gain, and final quantization. Original files are preserved. Playback DSP settings are independent.

Resampling offers 44.1/48/96/192 kHz and three swresample filter types. Basic dither supports none, rectangular, triangular or high-pass triangular. Noise-shaped dither replaces the base method; Shibata/low-Shibata require 44.1/48 kHz here, and Lipshitz is restricted to 44.1 kHz. Dither/noise-shaping controls in this workflow require 16-bit output. These restrictions are source-level guards, not measured quality guarantees.

Pitch is bounded to ±12 semitones. Tempo is 0.5–2×, with compensating resampling/tempo stages intended to make it independent of pitch. Artifacts, transient behavior, exact duration and accuracy remain unmeasured. RIAA must be explicitly enabled only for a source needing the reproduction curve; the application does not automatically identify already-corrected recordings.

The FIR designer writes a new mono IEEE-float WAV containing 31–8,191 odd Hamming-windowed sinc taps. It supports low-pass, high-pass and band-pass designs; reference-frequency gain is normalized in the generator. The designer reports the intended linear-phase delay. It does not measure a room or guarantee the resulting frequency response.

Convolution consumes the first ten seconds of a local impulse, downmixed to mono and resampled to output rate, with an explicit 0–1 coefficient gain. Convolution latency, impulse alignment and output-tail behavior remain pending verification. High-gain filters can clip a quantized export if normalization is disabled.

Sample-peak normalization measures the processed floating-point signal, then re-renders using a bounded gain. It caps amplification at 24 dB and refuses more than 60 dB attenuation. A silent source receives no extra gain. It is not true-peak limiting, ReplayGain tagging or EBU/R128 mastering.

## Spectrum and similarity

The source analyzer decodes at most 120 seconds to 11,025 Hz mono 16-bit PCM. It computes a 512-bin peak envelope, at most 128 Hann-windowed 2,048-point FFT snapshots, relative spectral power, a 32-band descriptor, PCM RMS, spectral centroid, zero-crossing rate and the existing tempo estimate. The display excludes frequencies above about 5.5 kHz and is not a live output meter or calibrated loudness analyzer.

Sonic library discovery analyzes explicitly selected batches of 1–100 indexed audio tracks sequentially. Each completed descriptor is written to the lazy profile-local `sonic-library.json`; cancellation retains completed entries. There is no startup scan or unattended acoustic analysis. The profile is bounded to 10,000 descriptors / 32 MiB.

Use the existing library's tag-reading workflow before analysis for meaningful artist/album grouping. Comparison rechecks source size/modification stamps, excluding missing or changed tracks. Track ranking combines spectral-band cosine, centroid and rhythm estimates. Album and artist comparisons average only the analyzed available subset, excluding unknown group metadata. Results are capped at 200 groups; explicit result playback queues at most 1,000 available paths.

Scores are relative, approximate acoustic matches, not confidence probabilities, exact duplicate detection, recording identity, musical key, mood, genre classification or semantic recommendations. A sparse analyzed subset can produce poor album/artist matches. Descriptor compatibility, rankings, persistence and cancellation all remain untested.

## Video workflow and limits

The video editor exports a new 8-bit H.264/AAC MP4 containing the first video and optional first audio stream. Subtitles, chapters, attachments, additional tracks and source metadata are not copied. It does not preserve HDR output. The None tone-map mode assumes SDR input.

The output bounding box is even, 64–3,840 pixels wide and 64–2,160 high. Aspect ratio is preserved, with an optional no-upscale policy. Bilinear, bicubic, Lanczos and natural-spline kernels are selected through libswscale. Dimensions are reduced before float-HDR/restoration/interpolation allocations; this ordering is a performance choice whose visual quality still needs review.

Debanding and spatiotemporal denoising are disabled at zero strength. Stabilization uses the single-pass `deshake` small-motion attempt, with a 16-pixel search and mirrored edges. Interpolation uses motion-compensated `minterpolate` at a selected 24/25/30/50/60 FPS; it can be expensive and may introduce motion artifacts. This is not vidstab, AI stabilization or real-time GPU frame generation.

The overlay group supports a local image and/or caption. Captions are rasterized using local Segoe UI into an owned temporary PNG; user text is not inserted into filter expressions. Logos fit inside the selected square size; a caption is placed below that reserved area when both are supplied. Coordinates outside the actual aspect-fitted frame can clip the overlay. Transparency, positioning and text shaping still need visual verification.

HDR-to-SDR is explicitly configured from declared BT.2020 PQ or HLG input, source peak and target/reference luminance. The graph linearizes to floating-point samples, applies Reinhard or Hable, converts to BT.709 SDR, removes selected HDR frame side data and writes SDR output tags. Target nits (50–400) alter the export reference and declared source/target ratio; they do not read, calibrate or control a monitor. Incorrect source assumptions can produce incorrect output. There is no Dolby Vision, HDR10+, ICC/LUT or full display-pipeline certification.

Input range can follow metadata or be forced Limited/Full; output Limited/Full conversion has matching range tags. Range conversion, tone-map math, highlights, gamut and encoder-side metadata behavior remain unverified.

## Job behavior and dependencies

Installed FFmpeg is required for actual decoding/export. No capability probe or job was executed in this batch. Some builds may lack filters, libraries or option versions; the error path reports the subprocess diagnostics. Exports have cancellation, a two-hour deadline, source-version checks, same-folder staging and a non-replacing final move. The source-level guard implementation itself still needs testing.

Official references consulted for filter/option wiring:

- [FFmpeg filters](https://ffmpeg.org/ffmpeg-filters.html): afir, aemphasis, astats, atempo, deshake, hqdn3d, deband, overlay, minterpolate, tonemap and zscale.
- [FFmpeg resampler](https://ffmpeg.org/ffmpeg-resampler.html): resampling filter types and dither/noise-shaping methods.
- [FFmpeg scaler](https://ffmpeg.org/ffmpeg-scaler.html): bilinear, bicubic, Lanczos and spline options.

Documentation lookup does not verify the installed FFmpeg build or this application's command construction.

## Deferred verification

- [ ] Compile the source and categorized XAML actions.
- [ ] Verify FIR impulse headers, responses, gain and group delay.
- [ ] Measure convolution, sample-peak normalization, resampling, dither/noise shaping, pitch and tempo.
- [ ] Exercise no-overwrite publication, cancellation, timeout, source changes and failed writes.
- [ ] Check FFT/envelope math, corpus persistence, missing metadata and ranking behavior.
- [ ] Review source/excerpt biases and album/artist aggregate behavior.
- [ ] Render all four video kernels and restoration/interpolation options.
- [ ] Inspect actual caption/logo composition and Fluent/DPI/keyboard behavior.
- [ ] Verify PQ/HLG input assumptions, target luminance, tone-map curves, ranges and SDR metadata.
- [ ] Run the existing regression suite against the combined sixty untested capabilities.

No new tests or binaries were produced. Historical passing results describe an earlier source checkpoint. Nothing was published to GitHub.
