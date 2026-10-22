param(
    [Parameter(Mandatory=$true)][string]$NuGetRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$BenchmarkOutput
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'Native/FluxDsp/fluxdsp.c'
$sdk = Join-Path $NuGetRoot 'videolan.libvlc.windows/3.0.21/build/x64'
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'include/vlc/plugins/vlc_filter.h'))) {
    throw 'Restore the pinned VideoLAN.LibVLC.Windows 3.0.21 package before building native DSP.'
}
$locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $locator)) { throw 'Install Visual Studio Build Tools with Desktop development with C++ to build native DSP.' }
$installation = & $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'The x64 MSVC C++ build tools are required for native DSP.' }
Import-Module (Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
$env:VSLANG = '1033'
$output = [IO.Path]::GetFullPath($OutputPath)
$folder = Split-Path -Parent $output
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$nativeArguments = @('/nologo', '/LD', '/MT', '/O2', '/std:c17', '/W4', '/WX', '/volatile:ms',
    ('/external:I' + (Join-Path $sdk 'include/vlc/plugins')), ('/external:I' + (Join-Path $sdk 'include')), '/external:W0',
    ('/Fo' + $folder + '/'), $source, '/link', ('/LIBPATH:' + $sdk), 'libvlccore.lib', 'libvlc.lib', 'Ws2_32.lib',
    ('/OUT:' + $output), ('/IMPLIB:' + (Join-Path $folder 'fluxdsp.lib')))
& cl.exe @nativeArguments
if ($LASTEXITCODE -ne 0) { throw "Native DSP compilation failed ($LASTEXITCODE)." }
if ($BenchmarkOutput) {
    $benchmark = [IO.Path]::GetFullPath($BenchmarkOutput)
    $benchmarkFolder = Split-Path -Parent $benchmark
    New-Item -ItemType Directory -Path $benchmarkFolder -Force | Out-Null
    $arguments = @('/nologo', '/MT', '/O2', '/std:c17', '/W4', '/WX', '/volatile:ms',
        ('/external:I' + (Join-Path $sdk 'include/vlc/plugins')), ('/external:I' + (Join-Path $sdk 'include')), '/external:W0',
        ('/Fo' + $benchmarkFolder + '/'), (Join-Path $projectRoot 'Native/FluxDsp/benchmark.c'), '/link',
        ('/LIBPATH:' + $sdk), 'libvlccore.lib', 'libvlc.lib', 'Ws2_32.lib', ('/OUT:' + $benchmark),
        ('/IMPLIB:' + (Join-Path $benchmarkFolder 'fluxdsp-benchmark.lib')))
    & cl.exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Native DSP benchmark compilation failed ($LASTEXITCODE)." }
}
