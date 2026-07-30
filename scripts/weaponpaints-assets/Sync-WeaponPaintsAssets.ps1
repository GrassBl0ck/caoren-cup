param(
    [string]$CacheRoot,
    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $repoRoot 'runtime\weaponpaints-assets\upstream'
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $scriptRoot 'source-v1.9.0.json'
}

$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$nextDownloadProgress = 10

function Invoke-CheckedGit {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$TrackDownload
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git @Arguments 2>&1 | ForEach-Object {
            $line = [string]$_
            Write-Host $line
            if ($TrackDownload) {
                foreach ($match in [regex]::Matches($line, 'Receiving objects:\s+(\d+)%')) {
                    $percent = [int]$match.Groups[1].Value
                    while ($script:nextDownloadProgress -le $percent -and $script:nextDownloadProgress -le 100) {
                        Write-Host "WeaponPaints download progress: $($script:nextDownloadProgress)%"
                        $script:nextDownloadProgress += 10
                    }
                }
            }
        }
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -ne 0) {
        throw "GIT_COMMAND_FAILED ($exitCode): git $($Arguments -join ' ')"
    }
}

$cacheFull = [System.IO.Path]::GetFullPath($CacheRoot)
if (-not (Test-Path -LiteralPath $cacheFull)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $cacheFull) -Force | Out-Null
    Invoke-CheckedGit -Arguments @(
        'clone', '--filter=blob:none', '--no-checkout', '--single-branch',
        [string]$config.repository, $cacheFull
    )
} elseif (-not (Test-Path -LiteralPath (Join-Path $cacheFull '.git') -PathType Container)) {
    throw "CACHE_IS_NOT_A_GIT_REPOSITORY: $cacheFull"
}

Invoke-CheckedGit -Arguments @('-C', $cacheFull, 'sparse-checkout', 'init', '--cone')
Invoke-CheckedGit -Arguments @('-C', $cacheFull, 'sparse-checkout', 'set', 'website/data', 'website/img/skins')
Invoke-CheckedGit -Arguments @('-C', $cacheFull, 'fetch', 'origin', [string]$config.commit)
Invoke-CheckedGit -Arguments @('-C', $cacheFull, 'checkout', '--progress', '--detach', [string]$config.commit) -TrackDownload

while ($nextDownloadProgress -le 100) {
    Write-Host "WeaponPaints download progress: $nextDownloadProgress%"
    $nextDownloadProgress += 10
}

$head = (& git -C $cacheFull rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne [string]$config.commit) {
    throw "PINNED_COMMIT_MISMATCH: expected $($config.commit), got $head"
}

$imageRoot = Join-Path $cacheFull 'website\img\skins'
$imageFiles = @(Get-ChildItem -LiteralPath $imageRoot -File -Filter '*.png')
$imageBytes = [int64](($imageFiles | Measure-Object Length -Sum).Sum)
if ($imageFiles.Count -ne [int]$config.expectedImageTreeCount -or $imageBytes -ne [int64]$config.expectedImageTreeBytes) {
    throw "PINNED_IMAGE_TREE_MISMATCH: expected $($config.expectedImageTreeCount) files/$($config.expectedImageTreeBytes) bytes, got $($imageFiles.Count) files/$imageBytes bytes"
}

Write-Host "WeaponPaints source ready: $cacheFull"
Write-Host "Commit: $head"
Write-Host "Images: $($imageFiles.Count)"
Write-Host "Bytes: $imageBytes"
