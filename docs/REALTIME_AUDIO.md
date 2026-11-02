# Continuous audio and native DSD

Settings → Audio opens **Continuous PCM, DSP and exclusive output** and **Native DSD · ASIO**. These are local transports. Publication and signing are not required.

## Continuous main queue

Queue 1–32 local audio files. Select the Windows endpoint, explicit speaker layout, sample rate, buffer and optional crossfade, then start the continuous session. FFmpeg/ffprobe must be installed together. The decoder streams into a bounded PCM ring; it does not create a joined media file. Zero crossfade concatenates decoded samples; a nonzero value overlaps adjacent tracks with triangular fades. Each remaining source, accounting for seek and playback speed, must last at least twice the fade duration.

The main pause, resume, stop, seek, volume and mute controls operate this session. Seek/speed changes rebuild the stream at the current queue item. The queue is a snapshot: manual Next or source selection leaves continuous mode. Closing its settings window keeps the main session playing. Repeating the queue or dynamically appending tracks to an already prepared stream is not implemented by this transport.

Available layouts are mono (FC), stereo (FL/FR), 2.1 (FL/FR/LFE), 5.1(side) (FL/FR/FC/LFE/SL/SR) and 7.1 (FL/FR/FC/LFE/BL/BR/SL/SR). Exclusive and multichannel modes reject an unsupported exact format before NAudio can select a fallback. Shared stereo can use the Windows audio engine's conversion. **Inspect endpoint formats** queries all 60 combinations of layout, rate and shared/exclusive mode without playback. Exported JSON identifies these as driver negotiations, not physical speaker or digital loopback verification.

An underrun is counted and reported; the output may insert silence to keep its clock moving. Such a session is not reported as continuous. Increase the buffer or reduce processing load before retrying.

## Ordered PCM effects

The DSP JSON is an ordered array. For example:

```json
[
  { "Kind": "Gain", "Value": 0.8 },
  { "Kind": "Matrix", "Matrix": [[1,0],[0,1]], "Gains": [1,0.9], "DelaysMs": [0,2] },
  { "Kind": "Limiter", "Value": 0.98 }
]
```

Matrix rows are physical outputs; columns are input channels. The matrix must match the chosen output channel count. Per-output gain and delay follow routing. This limiter clamps samples; it is not a look-ahead or true-peak limiter. The separate native LibVLC effect configuration does not automatically carry into this PCM transport.

Up to 32 effects can be loaded. A `Plugin` entry specifies `AssemblyPath` and `TypeName` for a public managed class implementing `FluxPlayer.Services.IPcmAudioEffect` with a public parameterless constructor. Build against the matching FluxPlayer assembly. `Initialize(rate, channels)` runs once; `Process(Span<float>)` receives complete interleaved float frames. It must not block, retain the span or produce non-finite values. `Dispose()` releases the effect. Only load trusted local DLLs: this ABI executes code in-process and is not VST or a sandbox.

## Direct exclusive PCM

Enable Direct PCM for one local integer PCM WAV (16/24/32 bit). The source's original format is negotiated with WASAPI exclusive output and bytes bypass float conversion, software gain, mute, resampling and effects. The rate/layout controls do not replace the source format. Changing software volume, mute or speed stops direct mode before applying the control. Set listening level on the physical device.

Byte preservation is tested at the source provider. An external digital capture is still required to certify a particular device's complete bit-perfect path.

## Native DSD

Choose an uncompressed DSF or DSDIFF file and an installed ASIO driver with packed one-bit DSD output support. This path uses ASIO native DSD I/O, not DoP. It does not decode into PCM or apply DSP, volume or rate conversion. Compressed DST and unsupported driver sample/channel formats are rejected. Select the first physical output channel explicitly.

Disk reads feed a bounded prebuffer outside the driver callback. The callback deinterleaves raw bytes to driver buffers, reverses bit order only when required and uses DSD silence for the final buffer remainder. Missing data is reported as an underrun. Closing the panel, pressing main Stop or selecting another source closes this transport. Its pause/resume button is separate from the main PCM transport.

Automated checks verify DSF/DSDIFF parsing, exact bytes, channel order, bit reversal, callback bounds and tail silence with a simulated driver. No compatible ASIO driver/DAC is installed in the current test environment, so real native DSD device playback remains unverified.

## Linked edition references

Settings → Playback → Ordered and linked chapters reads embedded Matroska chapters and SegmentUIDs, and can discover up to 500 candidate files in the primary source's folder. It skips media payloads and follows bounded metadata SeekHead references, including chapters beyond an unknown-size Cluster. Duplicate UIDs, changed files, invalid metadata and excessive scans are rejected; no disk-wide search or external tool installation occurs. Explicit media mappings `SegmentUID=absolute path` and chapter-document overrides `SegmentUID=absolute XML path` remain available.

A `ChapterSegmentEditionUID` resolves the complete target edition recursively. Ordered editions expand in declared order. Nonordered editions preserve source chronology, with disabled chapter ranges removed; a known source duration is required. A blank primary edition selects its default/first edition, including a document without an EditionUID. Reference times do not clip a whole linked edition, and absent end times on such references are valid. Cycles, missing targets, excessive recursion and programs above 2,000 playback steps are rejected before playback. Track-specific chapter actions and chapter scripts are explicitly unsupported; the ordinary player remains available for files outside this bounded chapter program.

References: [Matroska linked editions](https://www.matroska.org/technical/notes.html#linked-edition), [NAudio 2.2.1 source](https://github.com/naudio/NAudio/tree/v2.2.1), [Steinberg ASIO SDK](https://www.steinberg.net/developers/).
