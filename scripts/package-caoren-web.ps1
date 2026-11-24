[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version,
    [string]$SkinImageRoot,
    [string]$ClientImageBundle,
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
$webEntries = @('src/server.ts', 'public/index.html', 'pages/room.html', 'pages/login.html', 'public/js/web-room.js', 'public/css/web-room.css', 'pages/cs.html', 'pages/mc.html', 'pages/profile.html', 'public/js/portal.js', 'public/js/mc-board.js', 'public/css/portal.css', '.env.example', 'ACCOUNT-SETUP.md')
foreach ($entry in $webEntries) {
    if (-not (Test-Path -LiteralPath (Resolve-ReleasePath $repoRoot ('web-command-center/' + $entry)) -PathType Leaf)) { throw "Missing unified portal entry: $entry" }
}
if ([bool]$SkinImageRoot -ne [bool]$ClientImageBundle) { throw 'Provide both SkinImageRoot and ClientImageBundle.' }
if ($ValidateOnly) {
    Write-Output 'PASS: web release boundary and item data; no files or ZIP generated.'
    exit 0
}
$zipRelative = "release-output/CaorenCupWeb-网页端-$Version.zip"
if (Test-Path -LiteralPath (Resolve-ReleasePath $repoRoot $zipRelative)) { throw 'Output already exists; do not overwrite' }
$stage = Resolve-ReleasePath $repoRoot ('release-build/web-package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $stage
Copy-ReleaseWebTree $repoRoot $stage $Version
$required = @('package.json', 'package-lock.json', 'tsconfig.json', 'pages/room.html', 'pages/login.html', 'public/js/web-room.js', 'public/css/web-room.css', 'pages/cs.html', 'pages/mc.html', 'pages/profile.html', 'public/js/portal.js', 'public/js/mc-board.js', 'public/css/portal.css', 'src/server.ts', 'public/index.html', 'weaponpaints-data/skin-rarities.json')
# Ship the same approved WebP thumbnails as the Windows installer. Preserve original
# catalog URLs so installed clients can still satisfy requests from their own bundle.
if ($SkinImageRoot) {
    $bundleIndex = Get-Content (Join-Path $ClientImageBundle 'index.json') -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $archive = [IO.File]::OpenRead((Join-Path $ClientImageBundle 'images.pack'))
    try {
        if ($archive.Length -ne $bundleIndex.size) { throw 'Incomplete client image bundle.' }
        foreach ($pack in @('base','stickers')) {
            $sourceManifest = Join-Path $SkinImageRoot "$pack/manifest.json"
            $items = (Get-Content -LiteralPath $sourceManifest -Raw -Encoding utf8 | ConvertFrom-Json -Depth 20).items
            $target = Join-Path $stage "public/weaponpaints/$pack"
            $null = New-Item -ItemType Directory -Path (Join-Path $target 'images') -Force
            $manifest = foreach ($item in $items) {
                if ($item.available -ne $true) { continue }
                $name = [string]$item.image
                if ($name -notmatch '^images/[A-Za-z0-9_-]+\.(png|jpg|jpeg|webp)$') { throw "Invalid image path: $name" }
                $entry = $bundleIndex.images["/weaponpaints/$pack/$name"]
                if (-not $entry -or $entry[2] -ne 'image/webp' -or $entry[0] -lt 0 -or $entry[1] -le 0 -or
                    $entry[0] + $entry[1] -gt $archive.Length) { throw "Image bundle mismatch: $pack/$name" }
                $null = $archive.Seek($entry[0], [IO.SeekOrigin]::Begin)
                $bytes = [byte[]]::new($entry[1])
                $archive.ReadExactly($bytes, 0, $bytes.Length)
                if ([Text.Encoding]::ASCII.GetString($bytes, 8, 4) -ne 'WEBP') { throw "Invalid WebP: $name" }
                [IO.File]::WriteAllBytes((Join-Path $target ($name + '.webp')), $bytes)
                @{ category=$item.category; key=$item.key; available=$true; image=$name }
            }
            if (-not $manifest.Count) { throw "Empty image collection: $pack" }
            [IO.File]::WriteAllText((Join-Path $target 'manifest.json'), (@{items=@($manifest)} | ConvertTo-Json -Depth 5 -Compress))
        }
    } finally { $archive.Dispose() }
}
Assert-ReleaseTree $stage $required
Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE')
Copy-DeploymentGuide -RepoRoot $repoRoot -Stage $stage -Version $Version
Write-ReleaseFileManifest $stage $Version @('CaorenCupWeb') @()
$result = New-PortableReleaseZip $repoRoot $stage $zipRelative
Write-Output "Created: $($result.Path)"
Write-Output "SHA256: $($result.SHA256)"
Write-Output 'Bridge DLL is provided by CaorenCupServer; Minecraft content is provided separately by CaorenCupMC.'
