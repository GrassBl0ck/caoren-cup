[CmdletBinding()]
param(
    [switch]$IncludeGamePlugin,
    [switch]$SkipGamePlugin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

function Invoke-CheckedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$Command,
        [string[]]$Arguments = @()
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host "    $WorkingDirectory" -ForegroundColor DarkGray

    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Name failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

function Test-GamePluginChanged {
    try {
        $changed = & git -C $repoRoot status --short -- game-plugin 2>$null
        return -not [string]::IsNullOrWhiteSpace(($changed -join "`n"))
    }
    catch {
        return $false
    }
}

$webRoot = Join-Path $repoRoot 'web-command-center'
$bridgeRoot = Join-Path $webRoot 'CaorenCupPlugin'
$gamePluginRoot = Join-Path $repoRoot 'game-plugin'

Write-Host "Duel mode local test gate" -ForegroundColor Green
Write-Host "Repo: $repoRoot"
Write-Host "This script does not deploy, restart services, or open any app."

Invoke-CheckedStep `
    -Name 'Web TypeScript typecheck' `
    -WorkingDirectory $webRoot `
    -Command 'npm' `
    -Arguments @('run', 'typecheck')

Invoke-CheckedStep `
    -Name 'Lobby JavaScript syntax check' `
    -WorkingDirectory $webRoot `
    -Command 'node' `
    -Arguments @('--check', 'public\js\lobby-app.js')

Invoke-CheckedStep `
    -Name 'Web bridge plugin build' `
    -WorkingDirectory $bridgeRoot `
    -Command 'dotnet' `
    -Arguments @('build')

$shouldBuildGamePlugin = $IncludeGamePlugin -or ((Test-GamePluginChanged) -and -not $SkipGamePlugin)
if ($shouldBuildGamePlugin) {
    Invoke-CheckedStep `
        -Name 'Game plugin build' `
        -WorkingDirectory $gamePluginRoot `
        -Command 'dotnet' `
        -Arguments @('build')
}
else {
    Write-Host ""
    Write-Host "==> Game plugin build skipped" -ForegroundColor Yellow
    Write-Host "    Use -IncludeGamePlugin if this release also changes game-plugin/."
}

Write-Host ""
Write-Host "Local duel mode checks passed." -ForegroundColor Green
Write-Host "Next: run the browser lobby checklist in docs\duel-mode-test-flow.md, then run CS2 server checks with real players."
