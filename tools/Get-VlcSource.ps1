[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = [xml](Get-Content -LiteralPath (Join-Path $root 'FluxPlayer/FluxPlayer.csproj') -Raw)
$version = $project.SelectSingleNode('//PackageReference[@Include="VideoLAN.LibVLC.Windows"]/@Version').Value
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Review the source archive for this LibVLC version.' }
$name = "vlc-$version.tar.xz"
$uri = "https://download.videolan.org/pub/videolan/vlc/$version/$name"
$expected = ((Invoke-RestMethod -Uri "$uri.sha256") -split '\s+')[0]
if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Unexpected upstream checksum format.' }
$directory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$file = Join-Path $directory $name
if (-not (Test-Path -LiteralPath $file)) { Invoke-WebRequest -Uri $uri -OutFile $file }
$actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'The VLC source did not match the upstream SHA-256 hash.' }
"$actual  $name" | Set-Content -LiteralPath (Join-Path $directory 'VLC-SOURCE-SHA256.txt') -Encoding ascii
