# Audio extraction and mixing

## Implemented

- [x] **F0197 / R1124** — Extract audio from a local video into a new audio file.
- [x] **F0198 / R1125** — Review and extract audio from multiple local sources in one sequential batch.
- [x] **F0199 / R1096** — Mix multiple local audio sources with independently applied gain and delay.
- [x] **F0200 / R1097** — Export the mix as a new WAV or FLAC file.

## Pending

None of these four scoped capabilities remains pending. Microphone recording, recorded-audio alignment, AI stem separation and live mixing remain outside this batch and retain their original checklist status.

## Extract audio

Open **Settings → Audio → Production → Extract audio from video**. Choose 1–200 local files, an existing output folder, an audio profile and the audio stream number. Stream 1 means the first audio stream, independently of video stream indexes. The same stream number applies to every chosen source.

Select **Preview** to see the exact new filenames. Existing files and same-name sources receive distinct suffixes. Extract all rows or select a subset. Changing the stream, profile or source selection invalidates the preview. Each attempt consumes the reviewed plan; preview again after completion, failure or cancellation.

Profiles reuse the existing FLAC, 24-bit WAV, MP3, Opus, Vorbis, AAC, ALAC and WavPack encoders. Extraction decodes/re-encodes the chosen audio track; it is not a compressed-stream copy. Actual encoder availability depends on the separately installed FFmpeg build. Subtitles, video and data streams are excluded. No DRM bypass is provided.

The batch stops on the first failing or missing stream. Previously completed outputs remain available. Cancellation removes only the current unpublished output. Source size and modification time are checked before processing and before publication. They are stale-file guards, not content fingerprints. Outputs are published by a non-replacing move from a temporary sibling file.

## Mix audio

Open **Settings → Audio → Production → Mix audio to a new file**. Add 2–16 local sources. Select a row, enter its gain and delay, and choose **Apply to selected**. The table shows the applied values used for export. Add the same file more than once if the mix calls for it.

- Gain: −60 to +12 dB; decimal point syntax.
- Delay: 0–3,600,000 milliseconds. Positive delay inserts initial silence.
- Inputs: the first audio stream from each source, converted to 48 kHz stereo floating-point samples.
- Mixing: simultaneous sum with a peak limiter. The limiter can change loud peaks; this is not a bit-perfect operation or loudness mastering.
- Duration: the longest delayed source. Individual sources become silent when they end.
- Output: a new 24-bit PCM WAV or lossless FLAC. A FLAC output preserves the processed mix, not necessarily the original source sample format.

Choose **Export mix** and a new filename. Existing destinations are preserved. Closing the panel cancels its current task. These are explicit offline jobs; no worker or timer runs during ordinary playback because these panels are available.

## Validation

`AudioProductionChecks` verifies bounded previews, profile and gain/delay validation, output conflicts, cancellation and stale-source rejection. Native fixtures contain a real video with separate 330 Hz and 880 Hz audio streams. Extracted tracks are independently decoded and measured. WAV and FLAC mixes are checked for a 500 ms delayed onset, simultaneous overlap, the expected half-amplitude ratio at −6.0206 dB, a delayed tail and 48 kHz audio-only output. Complete and interrupted batches preserve source bytes and clean unpublished temporary files.

Run the focused checks with:

```powershell
dotnet run --project FluxPlayer.Tests -c Release --no-build --filter="Audio production"
```

An installed FFmpeg is required for the native production group. Full encoder/container and physical-device compatibility is not inferred from these fixtures.
