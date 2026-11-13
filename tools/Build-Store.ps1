[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$metadata = Get-Content -LiteralPath (Join-Path $release 'release-manifest.json') -Raw | ConvertFrom-Json
if ($metadata.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Store builds need a three-part release version; the fourth field is reserved.' }
$app = Join-Path $release 'app'
if (-not (Test-Path -LiteralPath (Join-Path $app 'FluxPlayer.exe') -PathType Leaf)) { throw 'Build the application with tools/Build-Release.ps1 first.' }
if ($Preview) {
    if ($IdentityName -or $Publisher -or $PublisherDisplayName) { throw 'Do not mix preview and Partner Center identity values.' }
    $IdentityName = 'FluxPlayer.Development'
    $Publisher = 'CN=FluxPlayer.Development'
    $PublisherDisplayName = 'FluxPlayer Development'
} elseif ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or $Publisher -notmatch '^CN=' -or [string]::IsNullOrWhiteSpace($PublisherDisplayName) -or $IdentityName -eq 'FluxPlayer.Development') {
    throw 'Supply Package/Identity/Name, Package/Identity/Publisher and Package/Properties/PublisherDisplayName from Partner Center, or use -Preview.'
}
$output = Join-Path $release $(if ($Preview) { 'store-preview' } else { 'store' })
New-Item -ItemType Directory -Path $output | Out-Null
$stage = Join-Path $output 'layout'
Copy-Item -LiteralPath $app -Destination $stage -Recurse
$assets = Join-Path $stage 'PackageAssets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$logo = [Drawing.Image]::FromFile((Join-Path $root 'FluxPlayer/Assets/logo.png'))
try {
    foreach ($size in @(44, 50, 150)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($logo, 0, 0, $size, $size)
            $bitmap.Save((Join-Path $assets "Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $logo.Dispose() }
$xml = [xml](Get-Content -LiteralPath (Join-Path $root 'installer/store/AppxManifest.xml') -Raw)
$ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
$ns.AddNamespace('p', $xml.DocumentElement.NamespaceURI)
$identity = $xml.SelectSingleNode('/p:Package/p:Identity', $ns)
$identity.SetAttribute('Name', $IdentityName)
$identity.SetAttribute('Publisher', $Publisher)
$identity.SetAttribute('Version', "$($metadata.version).0")
$xml.SelectSingleNode('/p:Package/p:Properties/p:PublisherDisplayName', $ns).InnerText = $PublisherDisplayName
$xml.Save((Join-Path $stage 'AppxManifest.xml'))
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
$makeAppx = (Get-Command makeappx.exe -ErrorAction SilentlyContinue).Source
if (-not $makeAppx) {
    $makeAppx = Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Recurse -Filter MakeAppx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $makeAppx) { throw 'Install Windows SDK build tools (MakeAppx.exe).' }
$suffix = if ($Preview) { 'store-preview' } else { 'store' }
$package = Join-Path $output "FluxPlayer-$($metadata.version)-$suffix-x64.msix"
& $makeAppx pack /d $stage /p $package /o
if ($LASTEXITCODE) { throw "MakeAppx failed: $LASTEXITCODE" }
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($package))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output $package
