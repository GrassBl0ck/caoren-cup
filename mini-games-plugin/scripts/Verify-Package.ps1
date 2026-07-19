[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PackageRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    return $FullPath.Substring($RootPath.Length).TrimStart([char[]]@('\', '/'))
}

function Test-IsNonRuntimeConfigurationArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $Name -match '(?i)(?:^|[._-])(?:example|sample|template|schema)(?:$|\.)'
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) {
    Write-Error "FAIL: output path does not exist or is not a directory: $OutputPath"
    exit 1
}

$resolvedOutputPath = (Resolve-Path -LiteralPath $OutputPath).Path
$requiredFiles = @(
    'CS2MiniGames.dll',
    'CS2MiniGames.deps.json',
    'Microsoft.Data.Sqlite.dll',
    'runtimes/linux-x64/native/libe_sqlite3.so'
)
$violations = [System.Collections.Generic.List[string]]::new()

foreach ($relativePath in $requiredFiles) {
    $platformPath = $relativePath.Replace(
        '/',
        [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path -Path $resolvedOutputPath -ChildPath $platformPath

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $violations.Add("required file is missing: $relativePath")
    }
}

$forbiddenDirectories = Get-ChildItem -LiteralPath $resolvedOutputPath -Directory -Recurse -Force |
    Where-Object {
        $_.Name -match '(?i)^backup(?:$|[-_])' -or
        $_.Name -ieq 'TestResults'
    }

foreach ($directory in $forbiddenDirectories) {
    $relativePath = Get-PackageRelativePath `
        -RootPath $resolvedOutputPath `
        -FullPath $directory.FullName
    $violations.Add("forbidden directory found: $relativePath")
}

$files = Get-ChildItem -LiteralPath $resolvedOutputPath -File -Recurse -Force
foreach ($file in $files) {
    $relativePath = Get-PackageRelativePath `
        -RootPath $resolvedOutputPath `
        -FullPath $file.FullName
    $name = $file.Name
    $isDatabaseOrLog = $name -match '(?i)(?:\.log$|\.db(?:$|-(?:wal|shm|journal)$))'
    $isBackup = $name -match '(?i)\.bak(?:-.*)?$'
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

    if ($isDatabaseOrLog) {
        $violations.Add("database or log file found: $relativePath")
    }
    elseif ($isBackup) {
        $violations.Add("backup file found: $relativePath")
    }
    elseif ($isUserConfiguration) {
        $violations.Add("user configuration found: $relativePath")
    }
}

if ($violations.Count -gt 0) {
    $details = ($violations | Sort-Object -Unique | ForEach-Object { "  - $_" }) -join [Environment]::NewLine
    Write-Error "FAIL: package content verification failed: $resolvedOutputPath$([Environment]::NewLine)$details"
    exit 1
}

Write-Output "PASS: package content verification passed: $resolvedOutputPath"
