param(
    [string]$SourceRoot,
    [string]$OutputRoot,
    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetScriptRoot = Join-Path $PSScriptRoot 'weaponpaints-assets'
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $repoRoot 'runtime\weaponpaints-assets\upstream'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'release-output'
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $assetScriptRoot 'source-v1.9.0.json'
}

$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sourceFull = (Resolve-Path -LiteralPath $SourceRoot).Path
$head = (& git -C $sourceFull rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne [string]$config.commit) {
    throw "PINNED_COMMIT_MISMATCH: expected $($config.commit), got $head"
}

Import-Module (Join-Path $assetScriptRoot 'WeaponPaintsAssets.psm1') -Force
$result = New-WeaponPaintsAssetPackages -SourceRoot $sourceFull -OutputRoot $OutputRoot -ConfigPath $ConfigPath

Write-Host 'Created WeaponPaints asset artifacts:'
Write-Host "  $($result.baseZip)"
Write-Host "  $($result.baseSha256)"
Write-Host "  $($result.stickersZip)"
Write-Host "  $($result.stickersSha256)"
Write-Host "  $($result.report)"
