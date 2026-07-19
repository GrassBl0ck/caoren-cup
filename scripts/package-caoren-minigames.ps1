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

if (Test-Path -LiteralPath $zipPath) {
    throw "Output already exists: $zipPath"
}

New-Item -ItemType Directory -Force -Path $releaseBuild | Out-Null
New-Item -ItemType Directory -Force -Path $releaseOutput | Out-Null
Assert-ChildPath -Parent $releaseBuild -Child $stagePath
if (Test-Path -LiteralPath $stagePath) {
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
$archive = [System.IO.Compression.ZipFile]::Open(
    $zipPath,
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
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
    $missing = @($requiredEntries | Where-Object { $_ -notin $entryNames })
    $forbidden = @($entryNames | Where-Object {
        $_ -match '(^|/)(bin|obj|\.vs|TestResults|release-build|release-output)(/|$)' -or
        $_ -match '(^|/)backup(?:$|[-_/])' -or
        $_ -match '(?i)\.db(?:$|-(?:wal|shm|journal)$)' -or
        $_ -match '(?i)\.(?:log|bak)(?:$|-)' -or
        $_ -match '(^|/)(CS2MiniGames\.json|\.env(?:\..*)?)$'
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

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Output "PASS: $zipPath"
Write-Output "SHA-256: $hash"
