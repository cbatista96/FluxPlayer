# Native equalizer implementation and measurements

The 15/20/31-band layouts are implemented in `Native/FluxDsp/fluxdsp.c`, compiled against the pinned VLC 3.0.21 plugin SDK. They process actual decoded audio in independent native biquads. The ten-band layout uses VLC's original equalizer when the new native effects/meters are bypassed, and shares the native filter when either is enabled. See [Architecture](ARCHITECTURE.md) for configuration and lifetime details and [Building](BUILDING.md) for the C++ prerequisites.

## Response checks

The native regression group captures processed signed 16-bit stereo PCM from synthetic WAV inputs, without playing the signal through speakers. It compares bypass and flat levels, +6 dB at a selected center, and the same boost with native gain compensation. Centers cover the first, middle and last band in each extended layout, including 20 Hz and 20 kHz. It also checks a −20 dB cut and a +20 dB preamp with low-amplitude input.

Separate 20 Hz cases at 192 and 384 kHz exercise low-frequency coefficient/response precision. The 384 kHz capture uses VLC's audio setup callback because its fixed-rate memory-output configuration stores the rate in an 18-bit field; see the [pinned output implementation](https://github.com/videolan/vlc/blob/3.0.21/modules/audio_output/amem.c#L52). This is a test-only capture path. The application processes audio entirely in the native plugin and does not route playback samples through managed callbacks.

These are targeted response checks, not a complete swept-response, distortion, multichannel, device or sample-rate certification. Supported algorithm bounds do not mean that every decoder or output device accepts those formats. Gain compensation estimates headroom; it does not guarantee transient or intersample peak containment.

```powershell
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 -warnaserror
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --filter="Native 15/20/31-band DSP"
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --filter="Native EQ layouts"
```

The layout scenario switches live between 31, 20 and ten-band processing, restores a prior curve, recalls a full multiband output profile, reloads unrelated settings and disables the filter while the source continues. A desktop visual review inspected the complete 31-band panel, all frequency labels and controls, and acknowledged native processing with −6.3 dB effective gain for a +6 dB band and compensation enabled. This does not substitute for a full DPI/theme or manual interaction matrix.

The fifteen-band extension is also measured at its first, middle and last centers (25 Hz, 1 kHz and 16 kHz). Observed flat/bypass ratios were 1.000, +6 dB ratios 1.995, and compensated ratios 0.971–0.972. Managed tests persist all 15 values and presets; the live layout scenario visits fifteen-band processing and restores the prior twenty-band curve. A separate visual review inspected its 15 labeled controls and native acknowledgement. The processing-cost table below measures the original 20/31-band cases, not a separate fifteen-band benchmark.

## Processing-cost probe

`benchmark.c` includes the production filter functions and processes 6,000 blocks of 480 frames: the equivalent of 60 seconds of 48 kHz stereo. It runs offline, copies a small source block, invokes the in-place filter and consumes a checksum. It measures wall time with QueryPerformanceCounter and thread CPU time with GetThreadTimes. It does not include decoding, an audio device, UI composition, startup, scheduling deadlines or garbage collection in the application.

One local run on 22 October 2026 used Windows x64, MSVC `/O2 /MT`, and an AMD Ryzen 7 9850X3D with 8 physical cores / 16 logical processors:

| Layout | Nonzero sections | Wall time for 60 s equivalent | Thread CPU time | One-core CPU equivalent |
|---|---:|---:|---:|---:|
| 31 bands, flat | 0 | 0.111 ms | Below timer resolution | Not resolved |
| 31 bands, sparse | 3 | 19.265 ms | 31.250 ms | 0.0521% |
| 20 bands | 20 | 126.580 ms | 125.000 ms | 0.2083% |
| 31 bands | 31 | 215.579 ms | 218.750 ms | 0.3646% |

Thread CPU accounting is quantized, which can make a short case's reported CPU time exceed its measured wall time. The flat case still has call/control overhead; a zero timer reading is not zero cost. One-core equivalent is thread CPU milliseconds divided by 60,000 milliseconds, expressed as a percentage. These single-run figures are reproducible observations on this machine, not a performance promise for other machines or the whole player.

The same probe timed 100 configuration updates per case, including response estimation. Mean durations were 11.5, 17.6, 29.3 and 38.9 microseconds respectively; the largest observed update was 47.7 microseconds. Updates happen on explicit settings changes rather than on every block. A full startup/playback baseline and deadline/jitter matrix remain pending.

To reproduce, restore/build first, then run from the repository root:

```powershell
$packageRoot = Join-Path $env:USERPROFILE '.nuget/packages'
& tools/Build-NativeDsp.ps1 -NuGetRoot $packageRoot `
    -OutputPath Native/FluxDsp/bin/x64/Release/libfluxdsp_plugin.dll `
    -BenchmarkOutput session-state/native-benchmark/fluxdsp-benchmark.exe
$previousPath = $env:PATH
try {
    $env:PATH = [IO.Path]::GetFullPath('FluxPlayer/bin/x64/Release/net8.0-windows10.0.19041.0/libvlc/win-x64') + ';' + $env:PATH
    & session-state/native-benchmark/fluxdsp-benchmark.exe
} finally { $env:PATH = $previousPath }
```

Use your configured NuGet global-packages folder if it differs. Close the development player before replacing a loaded native DLL. Probe objects, binaries and local logs remain ignored; the public repository contains their source only.

## Native effect and meter checkpoint

The thirty-capability batch adds ten-band parametric peaking EQ, bass shelf, crossfeed, linked compressor and sample-peak limiter, balance, per-channel gain/delay, width, echo, short reverb, flanger, chorus and center reduction. Actual stereo PCM fixtures verify peaking gain, bass/high-frequency isolation, opposite-ear crossfeed, loud-tone compression, a -12 dBFS sample ceiling, independent channel level/delay, width and center behavior, delayed impulses and finite modulation output. Live checks activate effects while paused, retain position/volume, update them beside 31-band EQ, bypass them while the graphic EQ continues and reject changes after failed settings publication.

Native input/output peak and RMS are measured per block only when requested by the open signal-path window. Protocol 2 reports these values with active effect flags and the applied configuration sequence. These are DSP-stage measurements, before device/mixer volume. Effects support up to eight logical decoded channels at 192 kHz; stereo PCM measurements do not establish the entire channel/rate/device matrix. Graphic-only processing retains its previous bounds and 384 kHz response check.

The coefficient equations follow the primary [W3C Audio EQ Cookbook](https://www.w3.org/TR/audio-eq-cookbook/). Offline source measurements use documented [FFmpeg filters](https://ffmpeg.org/ffmpeg-filters.html), independently of the live native processor.

A fresh local probe on 25 October 2026 processed the same 60-second equivalent and also tested meters plus a combined 31-band/14-effect chain. Combined effects use different left/right tones so center reduction cannot turn the entire workload into silence.

| Case | Wall time | Thread CPU | One-core equivalent |
|---|---:|---:|---:|
| Flat 31-band, effects/meters off | 0.115 ms | Below timer resolution | Unresolved |
| Three nonzero graphic bands | 20.494 ms | 15.625 ms | 0.0260% |
| Twenty nonzero graphic bands | 124.792 ms | 125.000 ms | 0.2083% |
| Thirty-one nonzero graphic bands | 212.800 ms | 218.750 ms | 0.3646% |
| Flat graphic EQ with live meters | 44.812 ms | 46.875 ms | 0.0781% |
| Thirty-one bands, fourteen effects and meters | 674.969 ms | 671.875 ms | 1.1198% |

The bypass fast path is retained; enabled effects have measurable cost. This offline single-run probe excludes decoding, startup, UI, audio devices and scheduling jitter. It is not a zero-cost promise or a whole-player performance benchmark. Raw local evidence is `session-state/tests/thirty-features-benchmark.txt`; production source and benchmark source remain in the repository.

## Matrix and adaptive leveling checkpoint

Protocol 3 retains the prior prefix and appends an 8x8 row-output/column-input matrix at byte 604, then RMS target, maximum boost and response at bytes 860, 864 and 868. The configuration is 872 bytes in the existing 4,096-byte map; acknowledgement/meter status remains at byte 1,536. Both managed and native sides validate finite bounded parameters. The identity matrix and disabled leveling preserve defaults. Effect flags now include matrix (32768) and leveling (65536); real-processing detection excludes the meter-only flag.

Matrix processing copies up to eight logical input channels into a stack buffer before each output-row dot product. Linked RMS leveling follows time effects and precedes compressor/limiter. Its smoothed energy envelope drives bounded gain, with a silence gate, faster reduction and sample protection. Gating uses the envelope rather than instantaneous sample amplitude so waveform zero crossings do not cause gain pumping. Flush resets envelope/gain state. These are logical-channel/RMS features; physical speaker routing, LUFS/EBU R128 compliance and true-peak certification remain separate work.

Actual PCM tests verify channel swapping, quiet/loud convergence and maximum boost. The native benchmark source adds matrix-only, leveling-only and all-sixteen-effect cases. On 25 October 2026, each processed 60 seconds equivalent of 48 kHz stereo on this machine:

| Case | Wall time | Thread CPU | One-core equivalent |
|---|---:|---:|---:|
| Flat, processing/meters off | 0.132 ms | Below timer resolution | Unresolved |
| Matrix only | 57.246 ms | 62.500 ms | 0.1042% |
| Adaptive RMS leveling only | 97.951 ms | 93.750 ms | 0.1562% |
| 31 graphic bands, sixteen effects and meters | 787.986 ms | 796.875 ms | 1.3281% |

The combined case uses different left/right tones. Configuration updates averaged 11.7–38.8 microseconds; the largest measured update was 53.7 microseconds. Timer quantization explains CPU time exceeding wall time in some cases. These are single-run offline DSP observations, excluding startup, decoder/UI/device costs and deadlines. Disabled costs below timer resolution are not zero cost. Evidence: `session-state/tests/more-thirty-benchmark.txt`.
