# Building FluxPlayer

## Current local build and package

The current source compiles successfully in Release x64. Run `dotnet build FluxPlayer.sln -c Release -p:Platform=x64`, then launch `FluxPlayer/bin/x64/Release/net8.0-windows10.0.19041.0/FluxPlayer.exe`. To create a local ZIP that includes the .NET runtime, run `pwsh -NoProfile -File tools/Build-Release.ps1 -OutputDirectory artifacts/releases/my-local-build`. Choose a new output directory; the script preserves existing artifacts. No certificate/PFX is needed for local use.

The optional `-BuildUnsignedMsix` path validates package schema and packing with Windows SDK MakeAppx. Unsigned MSIX is for packaging validation. Real signed distribution and App Installer update acceptance are deferred by the user. The release workflow is manual and has not been dispatched. [Current implementation and limitations](LOCAL_COMPLETION_PROGRESS.md).

## Historical optional interoperability and processing checkpoint (unverified)

F0681–F1180 add embedded JSON schemas/catalogs and managed client/control code, without new NuGet dependencies. The source has not been compiled or tested. Kodi requires an existing reachable JSON-RPC server, MPD an existing daemon (a certificate-validating TLS proxy for non-loopback access), mpv a separately installed trusted mpv.exe, and the new offline algorithms a separately installed FFmpeg containing their filters/encoders. None is bundled or automatically installed. Main playback still uses the existing LibVLC runtime. See UNTESTED_FIVE_HUNDRED.md and THIRD_PARTY_INTEROP.md. No new executable was generated for this batch.

## Development environment

Use 64-bit Windows and install the SDK selected by `global.json` (currently 10.0.402). The application itself targets .NET 8 for Windows. A framework-dependent build also requires the .NET 8 desktop runtime. Install Visual Studio 2022 or its Build Tools with **Desktop development with C++**, including the x64 MSVC compiler and Windows SDK, for the native 15/20/31-band DSP module. The Visual Studio editor is optional.

Restore downloads managed dependencies and the native LibVLC runtime from NuGet. No globally installed VLC player is required.

```powershell
dotnet --info
dotnet restore FluxPlayer.sln -p:Platform=x64
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 -warnaserror
dotnet run --project FluxPlayer/FluxPlayer.csproj -c Release -p:Platform=x64
```

The solution includes the application and executable test harness. Use the x64 configuration because the native playback runtime and renderer are built for x64.

The build invokes `tools/Build-NativeDsp.ps1` when the native C source or build script changes. It uses the plugin SDK from the pinned VideoLAN.LibVLC.Windows 3.0.21 package and copies `libfluxdsp_plugin.dll` into the application's VLC audio-filter folder. The module uses the static C runtime. Native objects and binaries remain ignored by Git; no additional runtime is downloaded by the app.

For a legacy-only build without the C++ tools, add `-p:SkipNativeDsp=true` to build/run commands and use a clean output directory. It retains the ten-band equalizer; extended choices are unavailable when the module is absent. Do not carry an old native DLL into that output. The full native regression suite expects the standard build with DSP enabled.

## Tests

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release -- --managed-only
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release
```

The first command matches the managed service checks used in CI. The second additionally exercises LibVLC and, when available, FFmpeg. See [Testing](TESTING.md).

## Optional local packaging

Public downloads are listed under GitHub Releases. Packaging commands below create files on your own machine; they do not upload anything to GitHub.

```powershell
# Self-contained Windows x64 build, without an installer
.\build-release.ps1 -SkipInstaller

# Self-contained build and installer, when Inno Setup 6 or 7 is installed
.\build-release.ps1
```

Output is written to `publish/FluxPlayer/` and `installer/output/`, both ignored by Git. The packaging script validates its output paths before cleanup, removes the unused x86 LibVLC runtime and builds the VLC plugin cache. A self-contained build includes its .NET runtime. Keep the native LibVLC libraries and plugin folders next to the application; this project intentionally disables trimming and single-file publishing.

To reproduce the release layout with an installer and portable ZIP, check out the corresponding GitHub tag and run `pwsh -File tools/Build-Release.ps1 -BuildInstaller`. The script uses the project version by default and writes packages and SHA-256 sums under `artifacts/releases/`. For the Store package, see [Microsoft Store submission](MICROSOFT_STORE.md).

## Optional FFmpeg tools

Place compatible `ffmpeg.exe` and `ffprobe.exe` together on `PATH` or beside `FluxPlayer.exe`. Conversion and the explicitly selected continuous PCM transport discover these tools when needed; the ordinary LibVLC transport does not use FFmpeg executables. They are not downloaded or bundled. Available conversion profiles require the matching encoders. Native ASIO DSD uses its own bitstream reader and a compatible installed device driver. See [audio transport guide](REALTIME_AUDIO.md).

## Troubleshooting

- **SDK not found:** install the SDK listed in `global.json`, then rerun `dotnet --info`.
- **App does not launch:** verify the .NET 8 desktop runtime for framework-dependent builds; use the self-contained profile when appropriate.
- **Native runtime missing:** restore packages and retain the `libvlc/win-x64` directory in local package output.
- **Native DSP build fails:** install the C++ workload above. The script locates MSVC through Visual Studio Installer's `vswhere`; the VLC SDK must match the pinned NuGet version.
- **Material unavailable:** Mica and acrylic behavior depend on Windows and its visual settings. Use another theme if necessary.
- **Converter unavailable:** check `Get-Command ffmpeg.exe` and the installed encoder list.
