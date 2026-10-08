[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
# Compatibility entry point for an explicitly labelled partial update.
# The server packager supplies the current nested installation tree and license.
& (Join-Path $PSScriptRoot 'package-caoren-server.ps1') -Version $Version -Modules @('CS2MiniGames') -ValidateOnly:$ValidateOnly
