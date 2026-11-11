[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version,
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'CaorenRelease.psm1') -Force

# Fail before creating output; do not silently rewrite deferred authentication.
Assert-WebReleaseBoundary $repoRoot $Version
Copy-DeploymentGuide -RepoRoot $repoRoot -Version $Version -ValidateOnly
$dataRoot = Resolve-ReleasePath $repoRoot 'cs/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/data'
if (-not (Test-Path -LiteralPath (Join-Path $dataRoot 'skin-rarities.json'))) { throw 'Missing webpage item data' }
$webEntries = @('src/server.ts', 'public/index.html', 'pages/cs.html', 'pages/mc.html', 'public/js/portal.js', 'public/js/mc-board.js', 'public/css/portal.css', '.env.example', 'ACCOUNT-SETUP.md')
foreach ($entry in $webEntries) {
    if (-not (Test-Path -LiteralPath (Resolve-ReleasePath $repoRoot ('web-command-center/' + $entry)) -PathType Leaf)) { throw "Missing unified portal entry: $entry" }
}
if ($ValidateOnly) {
    Write-Output 'PASS: web release boundary and item data; no files or ZIP generated.'
    exit 0
}
$zipRelative = "release-output/CaorenCupWeb-网页端-$Version.zip"
if (Test-Path -LiteralPath (Resolve-ReleasePath $repoRoot $zipRelative)) { throw 'Output already exists; do not overwrite' }
$stage = Resolve-ReleasePath $repoRoot ('release-build/web-package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $stage
Copy-ReleaseWebTree $repoRoot $stage $Version
$required = @('package.json', 'package-lock.json', 'tsconfig.json', 'pages/cs.html', 'pages/mc.html', 'public/js/portal.js', 'public/js/mc-board.js', 'public/css/portal.css', 'src/server.ts', 'public/index.html', 'weaponpaints-data/skin-rarities.json')
Assert-ReleaseTree $stage $required
Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE')
Copy-DeploymentGuide -RepoRoot $repoRoot -Stage $stage -Version $Version
Write-ReleaseFileManifest $stage $Version @('CaorenCupWeb') @()
$result = New-PortableReleaseZip $repoRoot $stage $zipRelative
Write-Output "Created: $($result.Path)"
Write-Output "SHA256: $($result.SHA256)"
Write-Output 'Bridge DLL is provided by CaorenCupServer; Minecraft content is provided separately by CaorenCupMC.'
