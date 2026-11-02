# One hundred completed and verified capabilities

Acceptance date: **2026-10-28**. **100 of 100 pass; none of this selected batch awaits testing.** This completes source implementations previously held in the unverified queue. It does not add duplicate capability IDs or count shared infrastructure as extra features.

Selected scope: **F0201–F0260, F0291–F0320 and F0371–F0380**. Public catalog after this batch: **279 implemented, 80 implemented but unverified, 21 pending; 380 total**. The other 80 source capabilities are outside this 100-capability batch.

## Implementation corrections

- Cubic resampling now uses a four-point kernel. Reusing the 64-tap sinc setting caused a measured loss of tone amplitude.
- HDR processing declares both the linear RGB matrix and output primaries, fixing zimg conversion failures for sources without color tags.
- Video processing explicitly publishes the selected full/limited range in H.264 VUI, including when the source has no color description.
- Classical works require an actual Composer profile; arbitrary people references are rejected.
- Crossfade jobs check bounded source excerpts and reject overlaps longer than either source before publishing a header-only file.
- Focused editor windows fit the owner monitor working area so their title bar and footer remain accessible. Obsolete untested labels were removed from the verified panels.

## Evidence and reproduction

[Machine-readable acceptance results](hundred-feature-results.json) contain one passing result per stable capability ID. [Acceptance implementation](../FluxPlayer.Tests/HundredFeatureChecks.cs) uses isolated profile documents and generated media, independent ffprobe inspection, decoded PCM and actual video frames. Source hashes are checked after the complete batch. Cancellation, conflict refusal, temporary-file cleanup and failed catalog publication are exercised.

Release x64 builds with zero warnings/errors. The complete regression passed **192 groups** (the existing 92 plus these 100), archived locally at `session-state/tests/hundred-full-results.json`. After the final screen-fit and acceptance-assertion refinements, all **100 focused groups** passed again; final archive: `session-state/tests/hundred-final-results.json`. The full archive precedes only those presentation/test refinements.

```powershell
dotnet restore FluxPlayer.sln -p:Platform=x64
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --hundred-only
# Complete regression, including the same 100 acceptance checks:
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build
```

Tests require a Windows desktop, .NET 8 desktop runtime, the restored LibVLC runtime and installed FFmpeg/ffprobe. They do not download tools or access personal media. Conditional encoder profiles were actually exported and decoded with the installed FFmpeg build; this does not promise those encoders in every FFmpeg distribution. The declipping fixture deliberately exercises reconstruction and takes about 15 seconds on this machine.

The actual Release executable was also opened with the isolated `session-state/ui-hundred` profile for desktop inspection of player/settings navigation, categorized movie-library access and the catalog controls. The review application was closed normally. This supplements acceptance checks; it does not imply a complete theme, DPI or monitor matrix.

## Coverage and operational limits

Catalog checks cover persistence, independent artwork decoding/cache, people and credits, relationships, discography and ordered movement playback, status filters, forty-card paging, trailer ordering and native resume. Processing checks measure FIR pass/stop bands, sample rates, PCM precision, sample-peak normalization, pitch/tempo independence, DC removal and source FFT descriptors. Video outputs exercise scaling, restoration/interpolation, overlays, declared HDR/SDR transforms and color-range tags. Mastering checks measure actual channel gain/delay/routing, crossover/LFE, two-source mixing, channel count and ten real encoder outputs. Restoration checks measure click reduction, changed clipped PCM, noise attenuation, gating, speech gain, discrete center isolation, crossfade duration and independently measured -16 LUFS output.

These are bounded local/offline features. No physical surround calibration, monitor calibration, HDR display certification, universal codec support or AI stem recovery is claimed. A successful restoration attempt does not guarantee aesthetic improvement for arbitrary recordings. Similarity remains a heuristic over source excerpts. Original parent requirements with broader partial coverage retain their partial status.

## Acceptance checklist

| Result | Capability | Acceptance check |
|---|---|---|
| [x] F0201 | Local movie library with editable records and explicit playback. | movie library survives profile restart and rejects stale editor |
| [x] F0202 | Local television library with series-linked episode records. | series and episodes survive restart |
| [x] F0203 | Manually assigned local movie and episode posters. | poster association decodes actual frozen bitmap |
| [x] F0204 | Manually assigned fanart for local video records. | fanart association decodes independently |
| [x] F0205 | Local actor information profiles. | actor profile and biography persist |
| [x] F0206 | Editable movie and episode synopses. | synopsis edits are saved and searchable |
| [x] F0207 | Local release-date metadata with unknown-date handling. | optional release date round trip |
| [x] F0208 | Editable content classifications for catalog videos. | classification filter |
| [x] F0209 | Explicit watched-movie marking and watched filtering. | watched movie state clears captured position |
| [x] F0210 | Movie watchlist and unwatched views. | watchlist and unwatched filters |
| [x] F0211 | Navigation by series and season. | series-season dependent query excludes other seasons |
| [x] F0212 | Numbered episode records with season membership. | numbered episodes enforce unique identities |
| [x] F0213 | Local season cover associations and display. | season cover loads |
| [x] F0214 | Local series banners. | series banner loads |
| [x] F0215 | Linked cast credits and character roles. | cast characters and reverse profile credits |
| [x] F0216 | Explicit watched-episode tracking. | watched episodes advance series |
| [x] F0217 | Continue a series from its first unfinished episode. | series continuation selects earliest unfinished episode |
| [x] F0218 | Paged poster-wall browsing for the local video catalog. | poster wall displays forty cards and next page |
| [x] F0219 | Optional local trailer playback before a selected movie. | trailer playback queues feature immediately next |
| [x] F0220 | Large-type TV library navigation with directional keyboard controls. | TV library has large transport and keyboard list |
| [x] F0221 | Manually maintained artist biographies. | artist biography survives edit and restart |
| [x] F0222 | Local composer information profiles. | composer linked to saved work |
| [x] F0223 | Production credits linked to local catalog titles. | production credits include job and role |
| [x] F0224 | Named relationships between artist/person profiles. | person relationships round trip and reject self-links |
| [x] F0225 | Local artist discographies with playable release tracks. | discography ordered local release playback |
| [x] F0226 | Multiple values per custom metadata field. | multiple metadata values round trip and duplicate rejection |
| [x] F0227 | Named classical music collections with composer, work and movement order. | classical movement order survives restart and playback |
| [x] F0228 | Video catalog ordering by multiple criteria. | combined rating/date/attribute ordering |
| [x] F0229 | On-demand reduced-size local artwork thumbnails. | thumbnail cache reuses frozen reduced artwork and releases source |
| [x] F0230 | Explicit per-entry viewing status and captured playback position. | captured viewing position persists and resumes native playback |
| [x] F0231 | Windowed-sinc FIR impulse design for offline audio filtering. | FIR low/high/band-pass measured frequency responses |
| [x] F0232 | Offline impulse-response convolution into a new audio copy. | offline convolution attenuates a rejected source frequency |
| [x] F0233 | Selectable cubic, Blackman-Nuttall and Kaiser resampling filters for offline audio. | three resamplers render requested sample rates |
| [x] F0234 | Configurable 16-bit rectangular/triangular dither during offline export. | rectangular/triangular dither produce 16-bit PCM |
| [x] F0235 | Noise-shaped 16-bit dither for offline audio copies. | three noise-shaped quantizers produce valid audio |
| [x] F0236 | Two-pass processed sample-peak normalization. | two-pass sample normalization reaches target peak |
| [x] F0237 | Independent offline pitch shifting by semitones. | pitch changes preserve duration and double frequency |
| [x] F0238 | Offline tempo changes independent of pitch selection. | tempo doubles speed while retaining pitch |
| [x] F0239 | RIAA reproduction equalization for appropriate raw recordings. | RIAA reproduction changes real source response |
| [x] F0240 | Offline DC and subsonic cleanup. | DC and subsonic cleanup removes offset |
| [x] F0241 | On-demand source FFT spectrum and peak-envelope analyzer. | FFT spectrum locates known tone and peak envelope |
| [x] F0242 | Explicit bounded sonic analysis of local library tracks. | selected sonic analysis persists descriptors |
| [x] F0243 | Track similarity from bounded source-audio descriptors. | track similarity ranks available sources and excludes seed |
| [x] F0244 | Album similarity using analyzed local track aggregates. | album aggregate similarity |
| [x] F0245 | Artist similarity using analyzed local track aggregates. | artist aggregate similarity |
| [x] F0246 | Bilinear scaling for offline video exports. | Bilinear scaling |
| [x] F0247 | Bicubic scaling for offline video exports. | Bicubic scaling |
| [x] F0248 | Lanczos scaling for offline video exports. | Lanczos scaling |
| [x] F0249 | Spline scaling for offline video exports. | Spline scaling |
| [x] F0250 | Aspect-preserving downscaling limits for offline video. | aspect preserving reduction and no upscale |
| [x] F0251 | Configurable offline video debanding. | deband renders decodable copy |
| [x] F0252 | Offline spatiotemporal video noise reduction. | spatiotemporal denoise renders copy |
| [x] F0253 | Offline small-camera-shake stabilization attempt. | small motion stabilization renders copy |
| [x] F0254 | Motion-compensated frame interpolation in offline exports. | motion interpolation emits requested frame rate |
| [x] F0255 | Text and local-logo overlays burned into offline video copies. | local logo and rasterized caption overlays |
| [x] F0256 | Explicit offline HDR-to-SDR export pipeline. | declared PQ HDR becomes tagged SDR |
| [x] F0257 | Reinhard tone mapping for offline HDR-to-SDR copies. | Reinhard tone mapping output |
| [x] F0258 | Hable tone mapping for offline HDR-to-SDR copies. | Hable tone mapping output |
| [x] F0259 | Reference target luminance for offline tone mapping. | reference luminance controls tone mapping |
| [x] F0260 | Explicit input/output color-range handling for offline video copies. | explicit full input and limited output ranges |
| [x] F0291 | Offline bass management with main-speaker high-pass and LFE redirection. | bass crossover redirects low frequency into LFE |
| [x] F0292 | Explicit named speaker layouts for multichannel file processing. | named mono stereo 5.1 and 7.1 layouts |
| [x] F0293 | Independent per-output-channel mastering gain. | independent output gain measured in PCM |
| [x] F0294 | Independent speaker delays in multichannel file exports. | independent output delay measured at start |
| [x] F0295 | Editable stereo-to-surround spread preset for offline mastering. | stereo surround spread creates center and rear differences |
| [x] F0296 | Bounded arbitrary input-to-output multichannel routing matrix. | bounded arbitrary multichannel matrix |
| [x] F0297 | Two-source multichannel file mixing before output routing. | two-source multichannel mix retains both inputs |
| [x] F0298 | Multichannel PCM WAV and compatible FLAC mastering exports. | multichannel WAV and FLAC exports decode |
| [x] F0299 | Requested double-precision stages in the offline multichannel graph. | double precision graph retains tiny routed coefficients |
| [x] F0300 | Explicit post-matrix channel permutation. | post-matrix channel permutation |
| [x] F0301 | Offline .cube 3D LUT grading with interpolation selection. | local LUT trilinear and tetrahedral interpolation |
| [x] F0302 | Selectable offline software deinterlacing and field order. | three software deinterlacers and field orders |
| [x] F0303 | Offline stereoscopic packing and red/cyan conversion. | stereo packed source to Dubois anaglyph |
| [x] F0304 | Independent four-edge overscan cropping in finished copies. | four-edge overscan crop |
| [x] F0305 | Explicit anamorphic sample-aspect ratio in finished copies. | anamorphic sample aspect metadata |
| [x] F0306 | Exact image-origin placement within a selected output canvas. | exact image placement within canvas |
| [x] F0307 | Configurable 3-by-3 RGB channel transforms in finished copies. | nine coefficient RGB transform |
| [x] F0308 | Bounded excerpt luminance analysis with new-copy JSON report. | excerpt luminance analysis and new-copy report |
| [x] F0309 | Optional limited-range luma/chroma legalization for finished SDR copies. | SDR code value legalization |
| [x] F0310 | Explicit BT.2020 SDR to BT.709 gamut conversion for finished copies. | declared BT2020 SDR gamut conversion |
| [x] F0311 | Conditional ProRes 422 HQ / PCM MOV export. | ProRes 422 HQ actual encoder export |
| [x] F0312 | Conditional HEVC / AAC MP4 export. | HEVC / AAC actual encoder export |
| [x] F0313 | Conditional AV1 / Opus Matroska export. | AV1 / Opus actual encoder export |
| [x] F0314 | MPEG-2 / MP2 program-stream export. | MPEG-2 / MP2 actual encoder export |
| [x] F0315 | Conditional MXF / DNxHR HQ mastering export. | MXF / DNxHR HQ actual encoder export |
| [x] F0316 | Stereo AC-3 elementary-stream export. | AC-3 stereo actual encoder export |
| [x] F0317 | Stereo E-AC-3 elementary-stream export. | E-AC-3 stereo actual encoder export |
| [x] F0318 | Always-RF64 24-bit PCM file export. | RF64 / 24-bit PCM actual encoder export |
| [x] F0319 | AIFF big-endian 24-bit PCM export. | AIFF / 24-bit PCM actual encoder export |
| [x] F0320 | DXD-rate 352.8 kHz / 24-bit PCM export. | DXD-rate / 24-bit PCM actual encoder export |
| [x] F0371 | Allowlisted ordered offline audio-restoration chains. | allowlisted ordered restoration chains reject arbitrary filters |
| [x] F0372 | Reusable named restoration recipes with portable JSON import/export. | named recipes persist and portable JSON round trips |
| [x] F0373 | Offline impulsive click-removal attempts. | impulsive click removal processes actual impulses |
| [x] F0374 | Offline clipped-sample reconstruction attempts. | clipped-sample reconstruction produces finite changed PCM |
| [x] F0375 | Offline spectral audio denoising with selected reduction. | spectral denoise reduces stationary noise energy |
| [x] F0376 | Offline noise gating for quiet audio passages. | noise gate attenuates quiet passages |
| [x] F0377 | Offline speech-oriented half-cycle normalization. | speech half-cycle normalization alters envelope |
| [x] F0378 | Isolation of an existing discrete center channel into a stereo copy. | discrete center extraction isolates actual center signal |
| [x] F0379 | Offline two-source crossfade joining to a new audio file. | two-source crossfade has expected duration and source content |
| [x] F0380 | Measured two-pass EBU loudness normalization at the end of a restoration chain. | two-pass EBU loudness reaches measured target |
