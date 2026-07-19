[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$componentRoot = Join-Path $repoRoot 'mini-games-plugin'
$solutionPath = Join-Path $componentRoot 'CS2MiniGames.sln'
$projectPath = Join-Path $componentRoot 'src\CS2MiniGames\CS2MiniGames.csproj'
$verifierPath = Join-Path $componentRoot 'scripts\Verify-Package.ps1'
$licensePath = Join-Path $componentRoot 'LICENSE'
$releaseBuild = Join-Path $repoRoot 'release-build'
$releaseOutput = Join-Path $repoRoot 'release-output'
$stagePath = Join-Path $releaseBuild 'CS2MiniGames-publish'
$utf8 = [System.Text.Encoding]::UTF8
$packageLabel = $utf8.GetString(
    [Convert]::FromBase64String('5bCP5ri45oiP5o+S5Lu2'))
$zipPath = Join-Path $releaseOutput "CS2MiniGames-$packageLabel-$Version.zip"

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): dotnet $($Arguments -join ' ')"
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + `
        [System.IO.Path]::DirectorySeparatorChar
    $childFull = [System.IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith(
        $parentFull,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path outside expected parent: $childFull"
    }
}

function Assert-NotReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)

    $attributes = Get-EntryAttributes -Path $Path
    if ($null -eq $attributes) {
        return $false
    }

    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing reparse point: $Path"
    }

    return $true
}

function Get-EntryAttributes {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        return [System.IO.File]::GetAttributes($Path)
    }
    catch [System.IO.FileNotFoundException] {
        return $null
    }
    catch [System.IO.DirectoryNotFoundException] {
        return $null
    }
}

function Assert-NoReparsePointsInDirectoryTree {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    $rootAttributes = Get-EntryAttributes -Path $RootPath
    if ($null -eq $rootAttributes) {
        return $false
    }
    if (($rootAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing reparse point: $RootPath"
    }
    if (($rootAttributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
        throw "Expected staging path to be a directory: $RootPath"
    }

    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($RootPath)
    while ($pendingDirectories.Count -gt 0) {
        $directoryPath = $pendingDirectories.Pop()
        try {
            $entries = [System.IO.DirectoryInfo]::new($directoryPath).GetFileSystemInfos()
        }
        catch {
            throw "Unable to safely inspect staging path: $directoryPath. $($_.Exception.Message)"
        }

        foreach ($entry in $entries) {
            $entryAttributes = Get-EntryAttributes -Path $entry.FullName
            if ($null -eq $entryAttributes) {
                throw "Staging entry disappeared during safety preflight: $($entry.FullName)"
            }
            if (($entryAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing reparse point: $($entry.FullName)"
            }
            if (($entryAttributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                $pendingDirectories.Push($entry.FullName)
            }
        }
    }

    return $true
}

function Test-IsNonRuntimeConfigurationArtifact {
    param([Parameter(Mandatory = $true)][string]$Name)

    return $Name -match '(?i)(?:^|[._-])(?:example|sample|template|schema)(?:$|\.)'
}

foreach ($requiredPath in @(
    $solutionPath,
    $projectPath,
    $verifierPath,
    $licensePath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required input is missing: $requiredPath"
    }
}

Assert-ChildPath -Parent $repoRoot -Child $releaseBuild
Assert-ChildPath -Parent $repoRoot -Child $releaseOutput
Assert-ChildPath -Parent $releaseBuild -Child $stagePath
Assert-ChildPath -Parent $releaseOutput -Child $zipPath
$null = Assert-NotReparsePoint -Path $releaseBuild
$null = Assert-NotReparsePoint -Path $releaseOutput
$stageExists = Assert-NoReparsePointsInDirectoryTree -RootPath $stagePath
New-Item -ItemType Directory -Force -Path $releaseBuild | Out-Null
New-Item -ItemType Directory -Force -Path $releaseOutput | Out-Null
$null = Assert-NotReparsePoint -Path $releaseBuild
$null = Assert-NotReparsePoint -Path $releaseOutput

if (Assert-NotReparsePoint -Path $zipPath) {
    throw "Output already exists: $zipPath"
}

if ($stageExists) {
    Remove-Item -LiteralPath $stagePath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagePath | Out-Null

Invoke-DotNet @('restore', $solutionPath)
Invoke-DotNet @(
    'test', $solutionPath,
    '--no-restore',
    '--logger', 'console;verbosity=minimal'
)
Invoke-DotNet @(
    'publish', $projectPath,
    '-c', 'Release',
    '--no-restore',
    '-warnaserror',
    '-o', $stagePath
)

Copy-Item -LiteralPath $licensePath -Destination (Join-Path $stagePath 'LICENSE')

& powershell -NoProfile -ExecutionPolicy Bypass `
    -File $verifierPath -OutputPath $stagePath
if ($LASTEXITCODE -ne 0) {
    throw "Package tree verification failed with exit code $LASTEXITCODE."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tempZipPath = Join-Path $releaseOutput (
    "$(Split-Path -Leaf $zipPath).tmp-$([System.Guid]::NewGuid().ToString('N'))")
Assert-ChildPath -Parent $releaseOutput -Child $tempZipPath

try {
    $archive = [System.IO.Compression.ZipFile]::Open(
        $tempZipPath,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $stagePath -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($stagePath.Length).TrimStart('\', '/')
                $entryName = $relative.Replace('\', '/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive,
                    $_.FullName,
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
    }
    finally {
        $archive.Dispose()
    }

    $requiredEntries = @(
        'CS2MiniGames.dll',
        'CS2MiniGames.deps.json',
        'Microsoft.Data.Sqlite.dll',
        'runtimes/linux-x64/native/libe_sqlite3.so',
        'LICENSE'
    )
    $zip = [System.IO.Compression.ZipFile]::OpenRead($tempZipPath)
    try {
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
        $missing = @($requiredEntries | Where-Object { $_ -notin $entryNames })
        $forbidden = @($entryNames | Where-Object {
            $name = [System.IO.Path]::GetFileName($_)
            $isNonRuntimeConfigurationArtifact = Test-IsNonRuntimeConfigurationArtifact -Name $name
            $isUserConfiguration =
                -not $isNonRuntimeConfigurationArtifact -and
                (
                    $name -ieq 'CS2MiniGames.json' -or
                    $name -ieq '.env' -or
                    $name -match '(?i)^\.env\.' -or
                    $name -match '(?i)^appsettings(?:\.[^.]+)*\.json$' -or
                    $name -match '(?i)(?:^|[._-])config(?:uration)?(?:[._-].*)?\.(?:json|ya?ml|toml|cjs)$'
                )

            $_ -match '(^|/)(bin|obj|\.vs|TestResults|release-build|release-output)(/|$)' -or
            $_ -match '(^|/)backup(?:$|[-_/])' -or
            $name -match '(?i)\.db(?:$|-(?:wal|shm|journal)$)' -or
            $name -match '(?i)\.(?:log|bak)(?:$|-)' -or
            $isUserConfiguration
        })

        if ($missing.Count -gt 0) {
            throw "ZIP missing required entries:`n$($missing -join "`n")"
        }
        if ($forbidden.Count -gt 0) {
            throw "ZIP contains forbidden entries:`n$($forbidden -join "`n")"
        }
    }
    finally {
        $zip.Dispose()
    }

    $hash = (Get-FileHash -LiteralPath $tempZipPath -Algorithm SHA256).Hash
    Move-Item -LiteralPath $tempZipPath -Destination $zipPath
}
finally {
    if (Assert-NotReparsePoint -Path $tempZipPath) {
        Assert-ChildPath -Parent $releaseOutput -Child $tempZipPath
        Remove-Item -LiteralPath $tempZipPath -Force
    }
}

Write-Output "PASS: $zipPath"
Write-Output "SHA-256: $hash"
