[CmdletBinding()]
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

$repoRoot = $PSScriptRoot
$projectFile = Join-Path $repoRoot "FluxPlayer\FluxPlayer.csproj"
$publishProfile = "win-x64-self-contained"
$publishDir = Join-Path $repoRoot "publish\FluxPlayer"
$installerScript = Join-Path $repoRoot "installer\FluxPlayerSetup.iss"
$installerOutputDir = Join-Path $repoRoot "installer\output"

function Assert-ProjectDirectory {
    param([string]$Candidate)
    $absolute = [IO.Path]::GetFullPath($Candidate)
    $workspacePrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    if (-not $absolute.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The output path is outside the project: $absolute"
    }
    if ((Test-Path -LiteralPath $absolute) -and ((Get-Item -LiteralPath $absolute -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "The output cannot be a symbolic link or junction: $absolute"
    }
    return $absolute
}

if (-not (Test-Path $projectFile)) {
    throw "Project not found: $projectFile"
}

Get-Command dotnet -ErrorAction Stop | Out-Null

[xml]$projectXml = Get-Content -LiteralPath $projectFile
$appVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    $appVersion = "unknown"
}

Write-Step "Publishing a self-contained x64 build of FluxPlayer"
if (Test-Path $publishDir) {
    $publishDir = Assert-ProjectDirectory $publishDir
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

$publishArguments = @(
    $projectFile,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:PublishProfile=$publishProfile",
    "-p:Platform=x64",
    "-p:PublishDir=$publishDir"
)
& dotnet publish @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish returned exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir "FluxPlayer.exe"
if (-not (Test-Path $exePath)) {
    throw "Publishing did not produce the expected executable: $exePath"
}

$requiredRuntimeFiles = @(
    "FluxPlayer.dll",
    "FluxPlayer.runtimeconfig.json",
    "hostfxr.dll",
    "hostpolicy.dll",
    "coreclr.dll",
    "clrjit.dll"
)
foreach ($fileName in $requiredRuntimeFiles) {
    $filePath = Join-Path $publishDir $fileName
    if (-not (Test-Path $filePath -PathType Leaf)) {
        throw "The publication is not self-contained: missing $fileName in $publishDir"
    }
}

Write-Step "Removing unused LibVLC build files"
$x86Dir = Join-Path $publishDir "libvlc\win-x86"
if (Test-Path $x86Dir) {
    $x86Dir = Assert-ProjectDirectory $x86Dir
    Remove-Item -LiteralPath $x86Dir -Recurse -Force
}

$libDir = Join-Path $publishDir "libvlc\win-x64"
if (-not (Test-Path $libDir -PathType Container)) {
    throw "The publication is missing the x64 LibVLC binaries: $libDir"
}
if (-not (Test-Path (Join-Path $libDir "libvlc.dll") -PathType Leaf)) {
    throw "The publication is missing x64 libvlc.dll: $libDir"
}
$pluginsDir = Join-Path $libDir "plugins"
if (-not (Test-Path $pluginsDir -PathType Container)) {
    throw "The publication is missing the LibVLC plugins: $pluginsDir"
}
Get-ChildItem -Path $libDir -Filter "*.lib" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-Step "Building the LibVLC plugin cache for faster startup"
# Without plugins.dat, VLC scans the plugin libraries again at each startup.
$cacheProcess = Start-Process -FilePath $exePath -ArgumentList "--build-plugin-cache" -PassThru -Wait -WindowStyle Hidden
$pluginCache = Join-Path $pluginsDir "plugins.dat"
if ($cacheProcess.ExitCode -ne 0 -or -not (Test-Path $pluginCache -PathType Leaf)) {
    Write-Warning "Could not generate plugins.dat; the installer will retry building the cache."
}
else {
    Write-Host "Generated plugins.dat." -ForegroundColor Green
}

Write-Host "Executable ready: $exePath" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "Installer skipped because -SkipInstaller was specified." -ForegroundColor Yellow
    exit 0
}

$isccPath = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $isccPath) {
    Write-Warning "Inno Setup is not installed. The portable build is ready; no installer was generated."
    exit 0
}

if (-not (Test-Path $installerScript)) {
    throw "Installer script not found: $installerScript"
}

Write-Step "Compiling the installer with Inno Setup"
if (-not (Test-Path $installerOutputDir)) {
    New-Item -ItemType Directory -Path $installerOutputDir | Out-Null
}

Push-Location (Split-Path -Parent $installerScript)
try {
    & $isccPath "/DMyAppVersion=$appVersion" (Split-Path -Leaf $installerScript)
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup returned exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$installerExe = Join-Path $installerOutputDir ("FluxPlayerSetup-{0}-x64.exe" -f $appVersion)
if (-not (Test-Path $installerExe)) {
    $installerExe = Get-ChildItem -Path $installerOutputDir -Filter "FluxPlayerSetup-*-x64.exe" -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -ExpandProperty FullName -First 1
}

if (-not $installerExe) {
    throw "Build finished, but the generated installer could not be found."
}

Write-Host "Installer ready: $installerExe" -ForegroundColor Green
