# Native equalizer implementation and measurements

The 15/20/31-band layouts are implemented in `Native/FluxDsp/fluxdsp.c`, compiled against the pinned VLC 3.0.21 plugin SDK. They process actual decoded audio in independent native biquads. The ten-band layout continues to use VLC's original equalizer. See [Architecture](ARCHITECTURE.md) for configuration and lifetime details and [Building](BUILDING.md) for the C++ prerequisites.

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
