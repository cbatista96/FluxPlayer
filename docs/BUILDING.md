# Building FluxPlayer

## Development environment

Use 64-bit Windows and install the SDK selected by `global.json` (currently 10.0.402). The application itself targets .NET 8 for Windows. A framework-dependent build also requires the .NET 8 desktop runtime. Install Visual Studio 2022 or its Build Tools with **Desktop development with C++**, including the x64 MSVC compiler and Windows SDK, for the native 20/31-band DSP module. The Visual Studio editor is optional.

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

The public repository contains source only. Packaging commands below create files on your own machine; they do not upload anything to GitHub.

```powershell
# Self-contained Windows x64 build, without an installer
.\build-release.ps1 -SkipInstaller

# Self-contained build and installer, when Inno Setup 6 or 7 is installed
.\build-release.ps1
```

Output is written to `publish/FluxPlayer/` and `installer/output/`, both ignored by Git. The packaging script validates its output paths before cleanup, removes the unused x86 LibVLC runtime and builds the VLC plugin cache. A self-contained build includes its .NET runtime. Keep the native LibVLC libraries and plugin folders next to the application; this project intentionally disables trimming and single-file publishing.

An internal assembly version is used by local builds and the installer script. It does not represent a published GitHub release or tag.

## Optional FFmpeg tools

Place a compatible `ffmpeg.exe` on `PATH` or beside `FluxPlayer.exe`. The converter discovers it only when needed. It does not download FFmpeg, run it during normal playback or bundle it in the source repository. Available conversion profiles require the matching encoders in that FFmpeg build.

## Troubleshooting

- **SDK not found:** install the SDK listed in `global.json`, then rerun `dotnet --info`.
- **App does not launch:** verify the .NET 8 desktop runtime for framework-dependent builds; use the self-contained profile when appropriate.
- **Native runtime missing:** restore packages and retain the `libvlc/win-x64` directory in local package output.
- **Native DSP build fails:** install the C++ workload above. The script locates MSVC through Visual Studio Installer's `vswhere`; the VLC SDK must match the pinned NuGet version.
- **Material unavailable:** Mica and acrylic behavior depend on Windows and its visual settings. Use another theme if necessary.
- **Converter unavailable:** check `Get-Command ffmpeg.exe` and the installed encoder list.
