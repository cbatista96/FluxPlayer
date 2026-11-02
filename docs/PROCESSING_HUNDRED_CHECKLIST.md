# Processing hundred — verification checklist

100 new source capabilities, F0481–F0580. All 100 now pass the documented local acceptance fixtures. The historical source-only checkpoint is superseded. See PROCESSING_ACCEPTANCE.md for the exact tested scope and limits. These are concrete subfeatures of broader original requirements; they do not mean 100 original requirements have been fully completed.

Access paths, bounds and deferred acceptance: [workflow guide](UNTESTED_PROCESSING_HUNDRED.md). Machine-readable mapping: [manifest](processing-hundred-manifest.json).

## Audio diagnostics (10)

- [x] **F0531** Silence interval detection. Original: R0290 (partial). Operation: `silence`. Find intervals below −45 dBFS for at least 0.5 seconds.
- [x] **F0532** Per-channel signal statistics. Original: R0290 (partial). Operation: `channelstats`. DC offset, min/max, RMS, crest factor, crossings and reported bit depth; decoded signal, not container bit depth.
- [x] **F0533** Spectral feature timeline. Original: R0290 (partial). Operation: `spectralstats`. Per-frame centroid, spread, flatness and other spectral metadata in a bounded diagnostic tail.
- [x] **F0534** Stereo phase-correlation timeline. Original: R0290 (partial). Operation: `phase`. Stereo phase metadata after stereo negotiation; not proof of physical speaker phase.
- [x] **F0535** Excerpt dynamic-range meter. Original: R0290 (partial). Operation: `dr`. FFmpeg DR estimate for the chosen excerpt; not EBU LRA or a certified whole-album rating.
- [x] **F0536** Log-frequency spectrogram image. Original: R0290 (partial). Operation: `spectrogram`. One bounded excerpt spectrogram PNG.
- [x] **F0537** Channel-separated waveform image. Original: R0290 (partial). Operation: `waveform`. One waveform PNG; height is shared between decoded channels.
- [x] **F0538** Animated frequency bars. Original: R0290 (partial). Operation: `frequencybars`. H.264 frequency-bar visualization of up to 30 seconds, without embedded audio.
- [x] **F0539** Animated constant-Q spectrum. Original: R0290 (partial). Operation: `constantq`. Constant-Q frequency visualization, up to 30 seconds; requires FFmpeg showcqt support.
- [x] **F0540** Animated stereo vectorscope. Original: R0290 (partial). Operation: `vectorscope`. Stereo phase-space visualization without embedded audio, up to 30 seconds.

## Audio processing (25)

- [x] **F0481** Offline sibilance reduction. Original: R0980 (partial). Operation: `deesser`. Attenuate sibilance; audition the result before replacing any workflow.
- [x] **F0482** Offline stereo dialogue reinforcement. Original: R1084 (partial). Operation: `dialoguenhance`. Derive a center channel from stereo and fold the reinforced center back into stereo. Not voice separation.
- [x] **F0483** Offline headphone stereo spatialization. Original: R0091 (partial). Operation: `earwax`. Earwax binaural-style filtering at its required 44.1 kHz; output returns to 48 kHz.
- [x] **F0484** Offline sweeping phaser. Original: R0191 (partial). Operation: `aphaser`. Modulated comb-like phase effect, with conservative fixed input/output gain.
- [x] **F0485** Offline alternating stereo pulsation. Original: R0191 (partial). Operation: `apulsator`. LFO stereo amplitude modulation with opposite channel phases.
- [x] **F0486** Offline amplitude tremolo. Original: R0191 (partial). Operation: `tremolo`. Periodic amplitude modulation.
- [x] **F0487** Offline pitch vibrato. Original: R0191 (partial). Operation: `vibrato`. Periodic pitch modulation, separate from playback speed.
- [x] **F0488** Offline transient crystalizer. Original: R0191 (partial). Operation: `crystalizer`. Transient enhancement or softening; positive settings may raise peaks.
- [x] **F0489** Offline harmonic exciter. Original: R0191 (partial). Operation: `aexciter`. Add synthesized upper harmonics above a selected frequency.
- [x] **F0490** Offline oversampled soft clipping. Original: R0090 (partial). Operation: `asoftclip`. Tanh transfer curve; deliberately changes peaks and is not true-peak limiting.
- [x] **F0491** Offline audio contrast shaping. Original: R0191 (partial). Operation: `acontrast`. Apply FFmpeg's nonlinear audio contrast effect.
- [x] **F0492** Offline explicit dc offset compensation. Original: R0980 (partial). Operation: `dcshift`. Apply a measured constant offset; this does not estimate DC automatically.
- [x] **F0493** Offline stereo decorrelation. Original: R1404 (partial). Operation: `adecorrelate`. All-pass decorrelation with a repeatable random seed.
- [x] **F0494** Offline haas stereo delay. Original: R1404 (partial). Operation: `haas`. Short relative channel delays; check mono compatibility.
- [x] **F0495** Offline delay-based stereo widening. Original: R1404 (partial). Operation: `stereowiden`. Delay/crossfeed widening rather than a simple mid/side gain.
- [x] **F0496** Offline denormal prevention noise. Original: R0980 (partial). Operation: `adenorm`. Inject very low-level DC noise to avoid denormal processing in subsequent stages.
- [x] **F0497** Offline broadband tilt equalization. Original: R0980 (partial). Operation: `tiltshelf`. Tilt around a selected pivot frequency.
- [x] **F0498** Offline notch band rejection. Original: R0980 (partial). Operation: `bandreject`. Reject a bounded frequency band, for example a measured hum frequency.
- [x] **F0499** Offline isolated frequency-band pass. Original: R0980 (partial). Operation: `bandpass`. Retain a selected band for inspection or sound design.
- [x] **F0500** Offline all-pass phase shaping. Original: R0980 (partial). Operation: `allpass`. Second-order all-pass phase shaping at a chosen frequency.
- [x] **F0501** Offline stable custom biquad. Original: R0980 (partial). Operation: `biquad`. Explicit normalized coefficients with a strict second-order pole stability check.
- [x] **F0502** Offline piecewise dynamic expansion. Original: R1649 (partial). Operation: `compand`. Expand low-level sound with a fixed piecewise curve and adjustable attack/release.
- [x] **F0503** Offline three-band companding. Original: R1649 (partial). Operation: `mcompand`. Independent low/mid/high transfer curves with crossovers at 200 Hz and 2 kHz.
- [x] **F0504** Offline single-sideband frequency shift. Original: R0191 (partial). Operation: `afreqshift`. Translate frequency by a fixed Hz offset; differs from musical pitch shifting.
- [x] **F0505** Offline broadband phase rotation. Original: R0980 (partial). Operation: `aphaseshift`. Rotate phase using an all-pass approximation.

## Subtitle quality review (10)

- [x] **F0551** Find short exposure. Original: R1654 (partial). Operation: `qa-short`. Flag cues shorter than the minimum exposure.
- [x] **F0552** Find long exposure. Original: R1654 (partial). Operation: `qa-long`. Flag cues longer than the maximum exposure.
- [x] **F0553** Find excessive reading speed. Original: R1654 (partial). Operation: `qa-speed`. Count Unicode text elements, including spaces, divided by display seconds.
- [x] **F0554** Find line-layout overflow. Original: R1654 (partial). Operation: `qa-lines`. Flag more than two lines or lines exceeding the column budget.
- [x] **F0555** Find timeline overlaps. Original: R1654 (partial). Operation: `qa-overlap`. Flag a cue starting before any preceding cue has ended.
- [x] **F0556** Find short positive gaps. Original: R1654 (partial). Operation: `qa-gap`. Flag positive inter-cue gaps below the chosen minimum.
- [x] **F0557** Find repeated adjacent captions. Original: R1654 (partial). Operation: `qa-repeat`. Flag identical adjacent visible text within the gap threshold.
- [x] **F0558** Find invisible control characters. Original: R1654 (partial). Operation: `qa-controls`. Flag control/format characters except line breaks and tabs, retaining ZWNJ/ZWJ.
- [x] **F0559** Find unbalanced basic markup. Original: R1654 (partial). Operation: `qa-markup`. Check nesting of basic i, b and u tags; other markup is outside scope.
- [x] **F0560** Find unbalanced dialogue brackets. Original: R1654 (partial). Operation: `qa-brackets`. Check parentheses, square brackets and curly quotation marks.

## Subtitle text cleanup (10)

- [x] **F0561** Normalize Unicode to NFC. Original: R1654 (partial). Operation: `nfc`. Compose equivalent Unicode sequences without transliteration.
- [x] **F0562** Normalize horizontal whitespace. Original: R1654 (partial). Operation: `space`. Collapse spaces/tabs per line, trim edges and preserve meaningful line breaks.
- [x] **F0563** Remove bracketed sound descriptions. Original: R1654 (partial). Operation: `sdh`. Remove square-bracket descriptions; use Undo if brackets carry dialogue.
- [x] **F0564** Remove musical-note markers. Original: R1654 (partial). Operation: `music`. Remove ♪ and ♫ markers; retain sung text.
- [x] **F0565** Remove exact duplicate cues. Original: R1654 (partial). Operation: `deduplicate`. Remove duplicate start/end/text triples while preserving the first occurrence.
- [x] **F0566** Normalize typography. Original: R1654 (partial). Operation: `punctuation`. Convert three-dot ellipses, double hyphens and straight paired quotes to typographic forms.
- [x] **F0567** Capitalize sentence starts. Original: R1654 (partial). Operation: `sentence`. Uppercase the first letter and letters following sentence punctuation; no language inference.
- [x] **F0568** Reflow text to column budget. Original: R1654 (partial). Operation: `reflow`. Wrap on whitespace using Unicode text-element lengths. Long words remain intact.
- [x] **F0569** Remove visually empty cues. Original: R1654 (partial). Operation: `empty`. Remove empty cues and cues containing only basic markup/whitespace.
- [x] **F0570** Remove unwanted control characters. Original: R1654 (partial). Operation: `controls`. Remove control/format characters except line breaks, tabs and ZWNJ/ZWJ.

## Subtitle timing and structure (10)

- [x] **F0571** Extend short cue durations. Original: R1654 (partial). Operation: `minimum`. Extend to minimum duration, bounded by the next cue start when possible.
- [x] **F0572** Cap long cue durations. Original: R1654 (partial). Operation: `maximum`. Shorten cues exceeding maximum duration; never lengthen a cue.
- [x] **F0573** Fit duration to reading speed. Original: R1654 (partial). Operation: `reading`. Extend toward the reading target, bounded by maximum duration and the next cue start.
- [x] **F0574** Close small timeline gaps. Original: R1654 (partial). Operation: `close-gaps`. Extend a cue to the next start when a positive gap is within the chosen threshold.
- [x] **F0575** Trim overlapping cue ends. Original: R1654 (partial). Operation: `overlaps`. Trim the earlier end to the next start. Same-start cues require manual resolution.
- [x] **F0576** Extract and rebase a time interval. Original: R1654 (partial). Operation: `crop`. Intersect captions with the chosen interval and shift its start to zero.
- [x] **F0577** Split cues at sentence boundaries. Original: R1654 (partial). Operation: `split`. Split at an interior punctuation boundary near the midpoint, distributing duration by text length.
- [x] **F0578** Merge adjacent identical captions. Original: R1654 (partial). Operation: `merge`. Union adjacent equal-text cues when overlapping or within the gap threshold.
- [x] **F0579** Append a second subtitle document. Original: R1654 (partial). Operation: `append`. Rebase the second document after the current final end plus the gap threshold.
- [x] **F0580** Snap cue times to a frame grid. Original: R1654 (partial). Operation: `frames`. Quantize starts to nearest frame and ends upward; millisecond subtitle precision remains finite.

## Video diagnostics (10)

- [x] **F0541** Black interval detection. Original: R1664 (partial). Operation: `black`. Black intervals after bounded 960×540 downscaling.
- [x] **F0542** Frozen-frame interval detection. Original: R1664 (partial). Operation: `freeze`. Candidate frozen intervals of at least two seconds on downscaled frames.
- [x] **F0543** Interlace and field-order analysis. Original: R1664 (partial). Operation: `fields`. IDET field-order summary on decoded source frames before scaling; heuristic only.
- [x] **F0544** Video signal-statistics timeline. Original: R1664 (partial). Operation: `signal`. Luma/chroma, temporal outlier, vertical repetition and broadcast-range metadata after downscaling.
- [x] **F0545** Frame entropy timeline. Original: R1664 (partial). Operation: `entropy`. Frame-plane entropy metadata on downscaled video; not a perceptual quality score.
- [x] **F0546** Blur candidate scoring. Original: R1664 (partial). Operation: `blur`. Blur metadata after downscaling; thresholds depend on content.
- [x] **F0547** Block artifact candidate scoring. Original: R1664 (partial). Operation: `block`. Blocking metadata after downscaling; not a codec conformance check.
- [x] **F0548** Scene-cut candidate detection. Original: R1664 (partial). Operation: `scenes`. Scene scores and cut candidates at threshold 10 on downscaled frames.
- [x] **F0549** Active-picture crop suggestions. Original: R1664 (partial). Operation: `crop`. Crop candidates in downscaled coordinates; no automatic crop application.
- [x] **F0550** Decoded frame SHA-256 manifest. Original: R1664 (partial). Operation: `framehash`. Framehash manifest for decoded source video frames in the selected excerpt; not a source-file hash.

## Video processing (25)

- [x] **F0506** Offline non-local means denoising. Original: R1392 (partial). Operation: `nlmeans`. Spatial patch denoising; CPU intensive even at bounded resolution.
- [x] **F0507** Offline adaptive temporal averaging. Original: R1392 (partial). Operation: `atadenoise`. Adaptive 9-frame temporal noise reduction.
- [x] **F0508** Offline wavelet denoising. Original: R1392 (partial). Operation: `owdenoise`. Overcomplete wavelet denoising for luma and chroma.
- [x] **F0509** Offline frequency-domain video denoising. Original: R1392 (partial). Operation: `fftdnoiz`. 3D FFT noise reduction with bounded block size.
- [x] **F0510** Offline rank-based grain suppression. Original: R0921 (partial). Operation: `removegrain`. A fixed conservative spatial rank filter (mode 1).
- [x] **F0511** Offline edge-preserving bilateral smoothing. Original: R1392 (partial). Operation: `bilateral`. Smooth similarly colored neighborhoods while retaining edges.
- [x] **F0512** Offline contrast-adaptive sharpening. Original: R0937 (partial). Operation: `cas`. CAS sharpening; no AI or hardware superresolution claim.
- [x] **F0513** Offline threshold-aware smart blur. Original: R0921 (partial). Operation: `smartblur`. Blur above a chosen local threshold with bounded radius.
- [x] **F0514** Offline gradient debanding and dithering. Original: R0959 (partial). Operation: `gradfun`. Reconstruct and dither smooth gradients. Recipes containing gradfun use lossless H.264 encoding to retain dither; files can be large.
- [x] **F0515** Offline shadow/midtone/highlight balance. Original: R1056 (partial). Operation: `colorbalance`. Independent red/blue shadow and highlight corrections with preserved lightness.
- [x] **F0516** Offline opponent-color contrast. Original: R1056 (partial). Operation: `colorcontrast`. Adjust red/cyan, green/magenta and blue/yellow contrast with equal nonzero channel weights.
- [x] **F0517** Offline luma-dependent chroma correction. Original: R1056 (partial). Operation: `colorcorrect`. Separate red/blue chroma corrections for dark and bright areas.
- [x] **F0518** Offline rgb black and white points. Original: R0811 (partial). Operation: `colorlevels`. Apply shared RGB input black/white points; black must be below white.
- [x] **F0519** Offline color-temperature adaptation. Original: R1056 (partial). Operation: `colortemperature`. Relative white-balance adjustment; not display ICC calibration.
- [x] **F0520** Offline histogram contrast equalization. Original: R1055 (partial). Operation: `histeq`. Global histogram equalization can amplify noise and alter intent.
- [x] **F0521** Offline temporal rgb range normalization. Original: R1057 (partial). Operation: `normalize`. Normalize RGB range using a smoothed temporal window.
- [x] **F0522** Offline luminance flicker reduction. Original: R0921 (partial). Operation: `deflicker`. Temporal geometric-mean deflickering.
- [x] **F0523** Offline chroma-only noise reduction. Original: R1392 (partial). Operation: `chromanr`. Suppress chroma noise without applying the same smoothing to luma.
- [x] **F0524** Offline chroma registration correction. Original: R1388 (partial). Operation: `chromashift`. Shift blue/red chroma planes independently to correct registration.
- [x] **F0525** Offline radial lens distortion correction. Original: R1388 (partial). Operation: `lenscorrection`. Manual radial coefficients; no automatic camera calibration.
- [x] **F0526** Offline four-corner perspective correction. Original: R1388 (partial). Operation: `perspective`. Source-coordinate trapezoid correction expressed as fractions of frame dimensions.
- [x] **F0527** Offline rectangular logo removal. Original: R0921 (partial). Operation: `delogo`. Interpolate a chosen rectangle; requires it to lie inside the scaled frame.
- [x] **F0528** Offline near-duplicate frame dropping. Original: R0923 (partial). Operation: `mpdecimate`. Drop near-identical frames while retaining original timestamps (variable frame rate).
- [x] **F0529** Offline temporal frame blending. Original: R0913 (partial). Operation: `tmix`. Uniformly blend a bounded number of adjacent frames, preserving output timing.
- [x] **F0530** Offline decaying temporal trails. Original: R1393 (partial). Operation: `lagfun`. Retain bright pixels with a selected decay.
