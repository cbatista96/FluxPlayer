# WPF subtitle composition

The main application now enables the `fluxsub` native subtitle filter when its matching VLC 3 module is present. The filter keeps video frames untouched: video decoding can remain D3D11VA while subtitle regions are delivered separately to WPF.

The module owns a proxy subpicture for each decoded cue. Its updater calls the original decoder updater, preserving animated ASS/raster behavior and the original cue's lifetime. Text regions use VLC's text renderer and configured font/style; raster regions use VLC's image converter. Resulting BGRA textures travel through a bounded shared-memory protocol. WPF displays them as image layers over the memory video surface, with source-relative positions, alpha, letterboxing, crop and the same presentation transforms. The mini-player attaches its own layer. Saved snapshots composite the subtitle pixels before crop/rotation/export.

This is GPU **composition** through WPF. Subtitle rasterization/conversion, video memory presentation and texture transfer still use CPU memory; it is not a zero-copy pipeline or a new HDR video output. The video surface remains 8-bit RV32 SDR.

## Validation

`GpuSubtitleChecks` creates actual H.264 Matroska sources with embedded SRT and ASS. On the installed NVIDIA GeForce RTX 4060 Ti it verifies an opaque D3D11VA surface stays active while actual WPF composite pixels contain red SRT glyphs or green ASS raster subtitles. It checks composed PNG snapshots, pause retention and track disable. A deliberately injected bridge error exercises recovery to native software subtitle composition and resumed transport. The older native subtitle-style/fallback tests still cover a build/session without this bridge.

No physical or universal subtitle/GPU matrix is implied by these fixtures. PGS/DVD palette conversion uses the same native image-conversion path but is not separately certified by the SRT/ASS fixtures. Embedded-font behavior depends on the decoder and installed VLC modules.

## Bounds and recovery

The protocol supports eight simultaneously visible cues, sixteen regions per cue and at most 8 MiB of pixels per cue. Slots are assigned on presentation, rather than reserving them for every predecoded future cue. Each slot uses a sequence lock; managed readers reject inconsistent or out-of-range metadata and cache unchanged textures. Filter/subpicture reference counts keep shared mappings and renderers alive until native cue destruction finishes.

Region overflow, texture conversion or renderer initialization errors disable the bridge for that engine instance and restart through the existing native software-composition profile. Unsupported content is not reported as accelerated. A legacy build with native modules skipped uses the previous subtitle fallback.

The module and managed protocol are built together from the pinned VLC 3.0.21 SDK. Updating either protocol requires matching binaries. The build refreshes the VLC plugin cache when a FluxPlayer native module is newer than the existing cache.

Primary implementation reference: [VideoLAN VLC 3.0.21 subpicture implementation](https://github.com/videolan/vlc/blob/3.0.21/src/misc/subpicture.c) and the pinned SDK's `vlc_subpicture.h`, `vlc_filter.h`, `vlc_image.h` and `vlc_text_style.h`.
