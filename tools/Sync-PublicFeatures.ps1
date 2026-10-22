[CmdletBinding()]
param([switch]$Verify)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$catalogPath = Join-Path $projectRoot 'docs/feature-catalog.json'
$outputPath = Join-Path $projectRoot 'FEATURES.md'
$items = @(Get-Content -LiteralPath $catalogPath -Raw -Encoding utf8 | ConvertFrom-Json)
if ($items.Count -eq 0) { throw 'The feature catalog must not be empty.' }
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($item in $items) {
    if (-not $ids.Add($item.Id)) { throw "Duplicate feature ID: $($item.Id)" }
    if ($item.Status -notin @('implemented', 'pending')) { throw "Invalid status: $($item.Id)" }
    if ($item.Status -eq 'implemented' -and [string]::IsNullOrWhiteSpace($item.Evidence)) {
        throw "Implemented features need source or validation evidence: $($item.Id)"
    }
}
$done = @($items | Where-Object Status -eq 'implemented').Count
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Feature checklist')
$lines.Add('')
$lines.Add("**$done implemented · $($items.Count - $done) pending · $($items.Count) tracked capabilities.**")
$lines.Add('')
$lines.Add('Each capability appears exactly once. Completing it moves the same stable ID from Pending to Implemented. A setting or catalog entry alone does not complete a feature. Source references describe the implementation; validation limits are documented in [Testing](docs/TESTING.md).')
$lines.Add('')
$lines.Add('Maintained in `docs/feature-catalog.json`. Regenerate with `pwsh -File tools/Sync-PublicFeatures.ps1`; CI rejects an outdated checklist.')
foreach ($status in @('implemented', 'pending')) {
    $sectionItems = @($items | Where-Object Status -eq $status)
    $title = if ($status -eq 'implemented') { 'Implemented' } else { 'Pending' }
    $check = if ($status -eq 'implemented') { 'x' } else { ' ' }
    $lines.Add('')
    $lines.Add("## $title ($($sectionItems.Count))")
    foreach ($area in @($sectionItems.Area | Select-Object -Unique)) {
        $lines.Add('')
        $lines.Add("### $area")
        $lines.Add('')
        foreach ($item in $sectionItems | Where-Object Area -eq $area) {
            $evidence = if ($item.Evidence) { " — $($item.Evidence)" } else { '' }
            $lines.Add("- [$check] **$($item.Id)** $($item.Description)$evidence")
        }
    }
}
$lines.Add('')
$lines.Add('## Compatibility notes')
$lines.Add('')
$lines.Add('Playback depends on the selected LibVLC runtime, plugins, input and hardware. Universal format support, native DSD, bit-perfect output and a complete hardware matrix are not claimed. The 20/31-band equalizer requires the native DSP module built from source; a legacy-only build retains ten-band processing. UI status identifies actual native acknowledgement.')
$content = ($lines -join "`n") + "`n"
if ($Verify) {
    if (-not (Test-Path -LiteralPath $outputPath)) { throw 'FEATURES.md is missing.' }
    $existing = [IO.File]::ReadAllText($outputPath).Replace("`r`n", "`n")
    if ($existing -ne $content) { throw 'FEATURES.md is outdated. Run Sync-PublicFeatures.ps1.' }
} else {
    [IO.File]::WriteAllText($outputPath, $content, [Text.UTF8Encoding]::new($false))
}
Write-Output "$done implemented; $($items.Count - $done) pending; $($items.Count) capabilities."
