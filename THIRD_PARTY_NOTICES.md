# Third-party notices

FluxPlayer source uses the [GNU GPL v3.0](LICENSE). External packages and tools retain their own licenses. This repository does not check in native playback binaries, the .NET runtime or FFmpeg executables; NuGet restores build dependencies.

## Direct runtime dependencies

| Package | Referenced version | License declared by the package | Upstream |
|---|---|---|---|
| WPF-UI | 3.0.4 | MIT | [WPF UI](https://github.com/lepoco/wpfui) |
| LibVLCSharp | 3.8.5 | LGPL-2.1-or-later | [VideoLAN LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) |
| VideoLAN.LibVLC.Windows | 3.0.21 | LGPL-2.1-or-later | [LibVLC NuGet packaging](https://code.videolan.org/videolan/libvlc-nuget) |
| CommunityToolkit.Mvvm | 8.3.2 | MIT | [Community Toolkit](https://github.com/CommunityToolkit/dotnet) |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT | [.NET runtime](https://github.com/dotnet/runtime) |

The entries above reflect the pinned packages' NuGet metadata. Consult the exact restored packages for their copyright notices and license files, including transitive dependencies and native plugins. The WPF UI license is available [upstream](https://github.com/lepoco/wpfui/blob/main/LICENSE).

## Build tools and optional tools

Windows SDK / Windows App SDK package references provide build assets or resolve platform dependencies. The project explicitly excludes the Windows App SDK runtime assets. .NET, Windows SDK tools, Inno Setup and any FFmpeg installation have their own terms.

FFmpeg is optional, discovered locally and not downloaded or redistributed by this repository. The license of a particular FFmpeg build depends on its enabled components. If distributing a compiled FluxPlayer package, review the exact runtime, plugins and tools included and supply their applicable notices and source materials. A source-only checkout does not include those distributions.
