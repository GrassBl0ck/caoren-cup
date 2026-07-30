param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$webRoot = Join-Path $repoRoot 'web-command-center'
$pluginRoot = Join-Path $webRoot 'CaorenCupPlugin'
$weaponPaintsDataRoot = Join-Path $repoRoot 'weaponpaints-plugin\data'
$releaseOutput = Join-Path $repoRoot 'release-output'
$releaseBuild = Join-Path $repoRoot 'release-build'
$webStage = Join-Path $releaseBuild 'web-command-center'
$webWeaponPaintsDataStage = Join-Path $webStage 'weaponpaints-data'
$pluginPublish = Join-Path $releaseBuild 'CaorenCupPlugin-publish'

$utf8 = [System.Text.Encoding]::UTF8
$webPackageLabel = $utf8.GetString([Convert]::FromBase64String('572R6aG156uv'))
$pluginPackageLabel = $utf8.GetString([Convert]::FromBase64String('572R6aG156uv5pyN5Yqh5Zmo5o+S5Lu2'))

$webZip = Join-Path $releaseOutput "CaorenCupWeb-$webPackageLabel-$Version.zip"
$pluginZip = Join-Path $releaseOutput "CaorenCupWebPlugin-$pluginPackageLabel-$Version.zip"

$excludedDirNames = @(
    '.git', '.vs', 'bin', 'obj', 'node_modules', 'runtime',
    'release-build', 'release-output'
)

$excludedFilePatterns = @(
    '*.zip', '*.rar', '*.7z',
    '*.bak', '*.bak-*', '*.backup', '*.bak_*', '*.broken*',
    '*.log',
    'postmatch-mock-test.html',
    '.env', 'caoren_config.json', 'ecosystem.config.cjs'
)

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $targetFull = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = New-Object System.Uri($baseFull)
    $targetUri = New-Object System.Uri($targetFull)
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Test-ExcludedPath {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileSystemInfo]$Item,
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot
    )

    $relative = Get-RelativePathCompat -BasePath $SourceRoot -TargetPath $Item.FullName
    $parts = $relative -split '[\\/]'

    foreach ($part in $parts) {
        if ($excludedDirNames -contains $part) { return $true }
        if ($part -like 'backup_*' -or $part -like 'backup-*' -or $part -like 'backup_before_*' -or $part -like '*_backup*') {
            return $true
        }
    }

    if (-not $Item.PSIsContainer) {
        foreach ($pattern in $excludedFilePatterns) {
            if ($Item.Name -like $pattern) { return $true }
        }
    }

    return $false
}

function Copy-CleanTree {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [string[]]$AdditionalTopLevelExcludes = @(),
        [string[]]$AdditionalRelativeExcludes = @()
    )

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    $sourceFull = (Resolve-Path $Source).Path
    Get-ChildItem -LiteralPath $sourceFull -Force -Recurse | ForEach-Object {
        $relative = Get-RelativePathCompat -BasePath $sourceFull -TargetPath $_.FullName
        $top = ($relative -split '[\\/]')[0]
        $normalizedRelative = $relative.Replace('/', '\').TrimEnd('\')

        if ($AdditionalTopLevelExcludes -contains $top) { return }
        foreach ($excludedRelative in $AdditionalRelativeExcludes) {
            $normalizedExcluded = $excludedRelative.Replace('/', '\').TrimEnd('\')
            if ($normalizedRelative -eq $normalizedExcluded -or $normalizedRelative.StartsWith($normalizedExcluded + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                return
            }
        }
        if (Test-ExcludedPath -Item $_ -SourceRoot $sourceFull) { return }

        $target = Join-Path $Destination $relative
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
        } else {
            $targetDir = Split-Path -Parent $target
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Assert-ZipClean {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ZipPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $badEntries = @()
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if ($name.Contains('\') -or
                $name -match '(^|/)(node_modules|\.vs|bin|obj|runtime|release-build|release-output)(/|$)' -or
                $name -match '^public/weaponpaints(/|$)' -or
                $name -match '(^|/)backup(_|-|_before_)' -or
                $name -match '\.(bak|backup|log|zip|rar|7z)(-|$|\.)' -or
                $name -match '(^|/)(\.env|caoren_config\.json|ecosystem\.config\.cjs)$') {
                $badEntries += $name
            }
        }

        if ($badEntries.Count -gt 0) {
            throw "Zip contains excluded entries:`n$($badEntries -join "`n")"
        }
    } finally {
        $zip.Dispose()
    }
}

function Compress-DirectoryPortable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceFull = (Resolve-Path $Source).Path
    $archive = [System.IO.Compression.ZipFile]::Open(
        $Destination,
        [System.IO.Compression.ZipArchiveMode]::Create
    )
    try {
        Get-ChildItem -LiteralPath $sourceFull -File -Recurse | Sort-Object FullName | ForEach-Object {
            $entryName = (Get-RelativePathCompat -BasePath $sourceFull -TargetPath $_.FullName).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $_.FullName,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $releaseOutput | Out-Null
New-Item -ItemType Directory -Force -Path $releaseBuild | Out-Null

if (Test-Path -LiteralPath $webZip) {
    throw "Output already exists: $webZip"
}
if (Test-Path -LiteralPath $pluginZip) {
    throw "Output already exists: $pluginZip"
}

Copy-CleanTree -Source $webRoot -Destination $webStage `
    -AdditionalTopLevelExcludes @('CaorenCupPlugin', 'CaorenCupPlugin.Tests') `
    -AdditionalRelativeExcludes @('public\weaponpaints')
Copy-CleanTree -Source $weaponPaintsDataRoot -Destination $webWeaponPaintsDataStage

if (Test-Path -LiteralPath $pluginPublish) {
    Remove-Item -LiteralPath $pluginPublish -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $pluginPublish | Out-Null

Push-Location $pluginRoot
try {
    dotnet publish -c Release -o $pluginPublish
} finally {
    Pop-Location
}

Compress-DirectoryPortable -Source $webStage -Destination $webZip
Compress-DirectoryPortable -Source $pluginPublish -Destination $pluginZip

Assert-ZipClean -ZipPath $webZip
Assert-ZipClean -ZipPath $pluginZip

Write-Host "Created:"
Write-Host "  $webZip"
Write-Host "  $pluginZip"
