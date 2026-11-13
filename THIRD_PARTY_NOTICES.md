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
| TagLibSharp | 2.3.0 | LGPL-2.1-only | [TagLib#](https://github.com/mono/taglib-sharp) |
| NAudio.Wasapi / NAudio.Asio (including NAudio.Core / NAudio.WinMM dependencies) | 2.2.1 | MIT | [NAudio](https://github.com/naudio/NAudio/tree/v2.2.1) |

The entries above reflect the pinned packages' NuGet metadata. Consult the exact restored packages for their copyright notices and license files, including transitive dependencies and native plugins. The WPF UI license is available [upstream](https://github.com/lepoco/wpfui/blob/main/LICENSE).

## Source for the distributed player

FluxPlayer and its native plugins are built from the source tag corresponding to the release. Source for the playback runtime is available from [VideoLAN VLC 3.0.21](https://download.videolan.org/pub/videolan/vlc/3.0.21/); the VLC source archive is also offered alongside the first release. See its COPYING and COPYING.LIB files and the [LibVLC NuGet build project](https://code.videolan.org/videolan/libvlc-nuget). Source for the managed bindings and tag library is available from the upstream projects listed above, at the listed versions. The .NET runtime and Windows desktop components come from [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/wpf](https://github.com/dotnet/wpf) and [dotnet/winforms](https://github.com/dotnet/winforms).

## Build tools and optional tools

Local packages may optionally include the unmodified MKVToolNix `mkvextract` and `mkvpropedit` executables under `Tools/MKVToolNix`, together with the upstream documentation and `doc/COPYING.txt`. The current local package uses version 103.0 from the [official portable download](https://mkvtoolnix.download/downloads.html#windows); copyright Moritz Bunkus and contributors, GNU GPL v2 with the additional component notices supplied by upstream. Corresponding source is available from the [official source downloads](https://mkvtoolnix.download/sources.html). The repository does not check these executables in. The optional packaging directory must be explicitly supplied to `tools/Build-Release.ps1`.

Windows SDK / Windows App SDK package references provide build assets or resolve platform dependencies. The project explicitly excludes the Windows App SDK runtime assets. .NET, Windows SDK tools, Inno Setup and any FFmpeg installation have their own terms.

FFmpeg is optional, discovered locally and not downloaded or redistributed by this repository. The license of a particular FFmpeg build depends on its enabled components. If distributing a compiled FluxPlayer package, review the exact runtime, plugins and tools included and supply their applicable notices and source materials. A source-only checkout does not include those distributions.

## NAudio license

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
