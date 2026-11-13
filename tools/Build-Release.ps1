[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$OutputDirectory = '',
    [string]$CertificateThumbprint = '',
    [switch]$RequireSigning,
    [switch]$BuildUnsignedMsix,
    [switch]$BuildInstaller,
    [string]$MatroskaToolsDirectory = '',
    [string]$DownloadBaseUrl = 'https://github.com/cbatista96/FluxPlayer/releases/latest/download'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'FluxPlayer/FluxPlayer.csproj'
if ($Version -eq '') {
    $Version = ([xml](Get-Content -LiteralPath $projectFile -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Use a numeric release version.' }
$packageVersion = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }
if (($packageVersion.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count -gt 0) { throw 'MSIX version fields must fit in 16 bits.' }
if ($OutputDirectory -eq '') { $OutputDirectory = Join-Path $projectRoot "artifacts/releases/$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory to preserve earlier artifacts.' }
if ($RequireSigning -and $CertificateThumbprint -eq '') { throw 'A real signing certificate thumbprint is required.' }
$certificate = $null
if ($CertificateThumbprint -ne '') {
    if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Invalid certificate thumbprint.' }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) { throw 'The signing certificate needs a valid private key and expiration date.' }
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$publishPath = Join-Path $OutputDirectory 'app'
& dotnet publish $projectFile -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($required in @('FluxPlayer.exe','FluxPlayer.dll','coreclr.dll','libvlc/win-x64/libvlc.dll',
    'libvlc/win-x64/plugins/audio_filter/libfluxdsp_plugin.dll','libvlc/win-x64/plugins/spu/libfluxsub_plugin.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $required) -PathType Leaf)) { throw "Missing release file: $required" }
}
$unused = [IO.Path]::GetFullPath((Join-Path $publishPath 'libvlc/win-x86'))
if (-not $unused.StartsWith([IO.Path]::GetFullPath($publishPath).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path.' }
if (Test-Path -LiteralPath $unused) { Remove-Item -LiteralPath $unused -Recurse -Force }
Get-ChildItem -LiteralPath (Join-Path $publishPath 'libvlc/win-x64') -File -Filter '*.lib' | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $projectRoot 'docs/PRIVACY.md') -Destination $publishPath
if ($MatroskaToolsDirectory -ne '') {
    $MatroskaToolsDirectory = [IO.Path]::GetFullPath($MatroskaToolsDirectory)
    foreach ($required in @('mkvextract.exe','mkvpropedit.exe','doc/COPYING.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $MatroskaToolsDirectory $required))) { throw "Missing portable MKVToolNix dependency: $required" }
    }
    $matroskaDestination = Join-Path $publishPath 'Tools/MKVToolNix'
    New-Item -ItemType Directory -Path $matroskaDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $MatroskaToolsDirectory 'mkvextract.exe'),(Join-Path $MatroskaToolsDirectory 'mkvpropedit.exe') -Destination $matroskaDestination
    Copy-Item -LiteralPath (Join-Path $MatroskaToolsDirectory 'doc') -Destination $matroskaDestination -Recurse
}
$sdkRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages/microsoft.windows.sdk.buildtools'
function Find-SdkTool([string]$name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $tool = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter $name -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "Windows SDK tool missing: $name" }
    return $tool.FullName
}
if ($certificate) {
    $signTool = Find-SdkTool 'signtool.exe'
    $ownBinaries = @((Join-Path $publishPath 'FluxPlayer.exe'), (Join-Path $publishPath 'FluxPlayer.dll')) + @(Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object Name -in @('libfluxdsp_plugin.dll','libfluxsub_plugin.dll') | Select-Object -ExpandProperty FullName)
    foreach ($binary in $ownBinaries) {
        & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $binary
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $binary" }
        & $signTool verify /pa $binary
        if ($LASTEXITCODE -ne 0) { throw "Signature trust verification failed: $binary" }
    }
}
$zipPath = Join-Path $OutputDirectory "FluxPlayer-$Version-portable-x64.zip"
Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -CompressionLevel Optimal
$files = @($zipPath)
if ($BuildInstaller) {
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    if (-not $iscc) { throw 'Install Inno Setup 6 or put ISCC.exe on PATH.' }
    $arguments = @('/Qp', "/DMyAppVersion=$Version", "/DPublishDir=$publishPath", "/DInstallerOutputDir=$OutputDirectory")
    if ($certificate) {
        $arguments += '/DSignRelease'
        $arguments += ('/Sfluxsign=$q{0}$q sign /sha1 {1} /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $f' -f $signTool, $CertificateThumbprint)
    }
    & $iscc @arguments (Join-Path $projectRoot 'installer/FluxPlayerSetup.iss')
    $setup = Join-Path $OutputDirectory "FluxPlayerSetup-$Version-x64.exe"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Installer build failed.' }
    if ($certificate) {
        & $signTool verify /pa $setup
        if ($LASTEXITCODE -ne 0) { throw 'Installer signature verification failed.' }
    }
    $files += $setup
}
if ($certificate -or $BuildUnsignedMsix) {
    $publisher = if ($certificate) { $certificate.Subject } else { 'CN=FluxPlayer.Development' }
    $assets = Join-Path $publishPath 'PackageAssets'; New-Item -ItemType Directory -Path $assets | Out-Null
    Add-Type -AssemblyName System.Drawing
    $logo = [Drawing.Image]::FromFile((Join-Path $projectRoot 'FluxPlayer/Assets/logo.png'))
    try {
        foreach ($size in @(44, 50, 150)) {
            $bitmap = [Drawing.Bitmap]::new($size, $size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $graphics.DrawImage($logo, 0, 0, $size, $size); $bitmap.Save((Join-Path $assets "Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png) }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $logo.Dispose() }
    $publisherXml = [Security.SecurityElement]::Escape($publisher)
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
 <Identity Name="FluxPlayer.Desktop" Publisher="$publisherXml" Version="$packageVersion" ProcessorArchitecture="x64" />
 <Properties><DisplayName>FluxPlayer</DisplayName><PublisherDisplayName>FluxPlayer</PublisherDisplayName><Logo>PackageAssets\Logo50.png</Logo></Properties>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Resources><Resource Language="en-us" /></Resources>
 <Applications><Application Id="FluxPlayer" Executable="FluxPlayer.exe" EntryPoint="Windows.FullTrustApplication"><uap:VisualElements DisplayName="FluxPlayer" Description="Audio and video player" BackgroundColor="transparent" Square150x150Logo="PackageAssets\Logo150.png" Square44x44Logo="PackageAssets\Logo44.png" /></Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
    [IO.File]::WriteAllText((Join-Path $publishPath 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
    $makeAppx = Find-SdkTool 'makeappx.exe'; $msixPath = Join-Path $OutputDirectory "FluxPlayer-$Version-win-x64.msix"
    & $makeAppx pack /d $publishPath /p $msixPath /o *> (Join-Path $OutputDirectory 'makeappx.log')
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $OutputDirectory 'makeappx.log') -Tail 20; throw 'MSIX validation/packing failed.' }
    $files += $msixPath
    if ($certificate) {
        & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $msixPath
        if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
        & $signTool verify /pa $msixPath
        if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }
        $base = [Uri]::new($DownloadBaseUrl.TrimEnd('/') + '/')
        if ($base.Scheme -ne 'https' -or $base.UserInfo -ne '') { throw 'App Installer updates require an HTTPS download location.' }
        $installerUri = [Security.SecurityElement]::Escape([Uri]::new($base, 'FluxPlayer.appinstaller').AbsoluteUri)
        $packageUri = [Security.SecurityElement]::Escape([Uri]::new($base, [IO.Path]::GetFileName($msixPath)).AbsoluteUri)
        $installer = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Uri="$installerUri" Version="$packageVersion">
 <MainPackage Name="FluxPlayer.Desktop" Publisher="$publisherXml" Version="$packageVersion" ProcessorArchitecture="x64" Uri="$packageUri" />
 <UpdateSettings><OnLaunch HoursBetweenUpdateChecks="0" ShowPrompt="true" UpdateBlocksActivation="false" /><AutomaticBackgroundTask /></UpdateSettings>
</AppInstaller>
"@
        $installerPath = Join-Path $OutputDirectory 'FluxPlayer.appinstaller'; [IO.File]::WriteAllText($installerPath, $installer, [Text.UTF8Encoding]::new($false)); $files += $installerPath
    }
}
$hashes = @($files | ForEach-Object { [ordered]@{file=[IO.Path]::GetFileName($_); bytes=(Get-Item -LiteralPath $_).Length; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} })
$release = [ordered]@{version=$Version; platform='win-x64'; selfContained=$true; signed=($null -ne $certificate); publisher=if($certificate){$certificate.Subject}else{''}; automaticUpdates=($null -ne $certificate); files=$hashes}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'release-manifest.json'), ($release | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$hashes | ForEach-Object { "$($_.sha256)  $($_.file)" } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Release artifacts: $OutputDirectory"
