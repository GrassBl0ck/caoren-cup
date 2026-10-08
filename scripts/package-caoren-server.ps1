[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version,
    [string[]]$Modules = @(),
    [string]$AudioEventsPath,
    [string]$AudioAssetsPath,
    [switch]$RetainPerformanceCalls,
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'CaorenRelease.psm1') -Force
$manifest = Read-ServerPackageManifest $repoRoot
$plugins = @(Select-ReleaseModules $manifest $Modules)
$hasAudio = 'CaorenCupQOL_PlaySound' -in @($plugins.name)
$hasFunCommands = 'CaorenCupFunCommands' -in @($plugins.name)
$needsContracts = $false
foreach ($plugin in $plugins) {
    [xml]$project = Get-Content -LiteralPath (Resolve-ReleasePath $repoRoot $plugin.project) -Raw -Encoding UTF8
    if ($project.SelectNodes('//ProjectReference') | Where-Object { $_.Include -match 'CaorenCupContracts\.csproj$' }) {
        $needsContracts = $true
    }
}
if ($AudioEventsPath -or $AudioAssetsPath) { $null = Read-ReleaseAudioCatalog $AudioEventsPath $AudioAssetsPath }
if ($ValidateOnly) {
    [pscustomobject]@{
        Mode = 'Manifest validation only; no build or ZIP'
        PluginCount = $plugins.Count
        Plugins = @($plugins.name)
        NeedsContracts = $needsContracts
        RequiresExplicitAudioCatalogs = $hasAudio
        RequiresPerformanceCallApproval = $hasFunCommands
        ExternalComponents = $manifest.externalComponents
    } | ConvertTo-Json -Depth 6
    exit 0
}
if ($hasFunCommands -and -not $RetainPerformanceCalls) {
    throw 'FunCommands retains performance call sites. Obtain user approval before passing -RetainPerformanceCalls; Diagnostics itself is excluded.'
}
if ($hasAudio) { $null = Read-ReleaseAudioCatalog $AudioEventsPath $AudioAssetsPath }
$partial = $Modules.Count -gt 0
$label = if ($partial) { '局部更新' } else { '服务器插件合集' }
$baseName = if ($partial) { 'CaorenCupUpdate-' + ($Modules -join '+') } else { 'CaorenCupServer' }
$zipRelative = "release-output/$baseName-$label-$Version.zip"
if (Test-Path -LiteralPath (Resolve-ReleasePath $repoRoot $zipRelative)) { throw 'Package already exists; do not overwrite' }
if ('CS2MiniGames' -in @($plugins.name)) {
    # Retain the existing mini-games test gate when moving its ZIP into the new scheme.
    & dotnet test (Resolve-ReleasePath $repoRoot 'mini-games-plugin/CS2MiniGames.sln') -c Release --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Mini-games package tests failed' }
}
$buildRoot = Resolve-ReleasePath $repoRoot ('release-build/server-package-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $buildRoot 'stage'
$null = New-Item -ItemType Directory -Path $stage -Force
$required = @()

foreach ($plugin in $plugins) {
    $publish = Join-Path $buildRoot ('publish/' + $plugin.name)
    & dotnet publish (Resolve-ReleasePath $repoRoot $plugin.project) -c Release --no-restore -warnaserror -p:EnableMenuDevelopmentControls=false -p:EnableAudioCapabilityProbe=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($plugin.name). Restore dependencies separately before retrying." }
    $relative = 'addons/counterstrikesharp/plugins/CaorenCup/' + $plugin.directory
    $destination = Resolve-ReleasePath $stage $relative
    Copy-ReleasePublishTree $publish $destination
    $required += "$relative/$($plugin.name).dll", "$relative/$($plugin.name).deps.json"
    if ($plugin.name -eq 'CaorenWeaponPaints') {
        $required += "$relative/LICENSE", "$relative/UPSTREAM.md", "$relative/MySqlConnector.dll", "$relative/data/skin-rarities.json"
        $gamedata = Resolve-ReleasePath $stage 'addons/counterstrikesharp/gamedata/weaponpaints.json'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $gamedata) -Force
        Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/gamedata/weaponpaints.json') -Destination $gamedata
        $required += 'addons/counterstrikesharp/gamedata/weaponpaints.json'
    }
    if ($plugin.name -eq 'CS2MiniGames') {
        Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'mini-games-plugin/LICENSE') -Destination (Join-Path $destination 'LICENSE')
        & (Resolve-ReleasePath $repoRoot 'mini-games-plugin/scripts/Verify-Package.ps1') -OutputPath $destination
        if (-not $?) { throw 'Existing mini-games package verification failed' }
        $required += "$relative/LICENSE", "$relative/Microsoft.Data.Sqlite.dll", "$relative/runtimes/linux-x64/native/libe_sqlite3.so"
    }
}
if ($needsContracts) {
    $publish = Join-Path $buildRoot 'contracts-publish'
    & dotnet publish (Resolve-ReleasePath $repoRoot $manifest.contracts.project) -c Release --no-restore -warnaserror -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Contracts publish failed' }
    $relative = 'addons/counterstrikesharp/shared/CaorenCupContracts/CaorenCupContracts.dll'
    $destination = Resolve-ReleasePath $stage $relative
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
    Copy-Item -LiteralPath (Join-Path $publish 'CaorenCupContracts.dll') -Destination $destination
    $required += $relative
}
if ($hasAudio) {
    $audioDir = Resolve-ReleasePath $stage 'addons/counterstrikesharp/plugins/CaorenCup/CaorenCupQOLs/CaorenCupQOL_PlaySound'
    Copy-Item -LiteralPath $AudioEventsPath -Destination (Join-Path $audioDir 'audio-events.json')
    Copy-Item -LiteralPath $AudioAssetsPath -Destination (Join-Path $audioDir 'audio-assets.json')
    $required += 'addons/counterstrikesharp/plugins/CaorenCup/CaorenCupQOLs/CaorenCupQOL_PlaySound/audio-events.json'
    $required += 'addons/counterstrikesharp/plugins/CaorenCup/CaorenCupQOLs/CaorenCupQOL_PlaySound/audio-assets.json'
}
if ($hasFunCommands) {
    $example = Resolve-ReleasePath $stage 'examples/module-configs/presets.grass.json'
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $example) -Force
    Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'game-plugin/module-configs/presets.grass.json') -Destination $example
}
if ('CaorenCupGamemode_RUSH' -in @($plugins.name)) {
    $assetRoot = 'game-plugin/PluginSplit/CaorenCupGamemode_RUSH/AssetTools/'
    foreach ($relative in @('README.md', 'addoninfo.txt', 'build_neutral_decider.py', 'VpkPack/Program.cs', 'VpkPack/VpkPack.csproj')) {
        $target = Resolve-ReleasePath $stage ('tools/rush/' + $relative)
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot ($assetRoot + $relative)) -Destination $target
    }
}
$licenses = Resolve-ReleasePath $stage 'licenses'
$null = New-Item -ItemType Directory -Path $licenses -Force
Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'LICENSE') -Destination (Join-Path $licenses 'CaorenCup-MIT.txt')
Copy-Item -LiteralPath (Resolve-ReleasePath $repoRoot 'docs/release-installation.md') -Destination (Join-Path $stage 'INSTALL.md')
$sourceText = "Source repository: https://github.com/GrassBl0ck/caoren-cup" + [Environment]::NewLine +
    "Release tag: $Version (verify the published tag before distribution)." + [Environment]::NewLine +
    "GPL-3.0 components, when included: mini-games-plugin/; game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/." + [Environment]::NewLine +
    "Corresponding source must be available at the release tag; each component retains its own license."
[IO.File]::WriteAllText((Join-Path $stage 'SOURCE.md'), $sourceText, (New-Object Text.UTF8Encoding($false)))
$contracts = @(Get-ChildItem -LiteralPath $stage -File -Recurse -Filter 'CaorenCupContracts.dll')
if ($contracts.Count -ne [int]$needsContracts) { throw 'Contracts must be shared exactly once when required' }
$oldDll = Join-Path $stage 'addons/counterstrikesharp/plugins/CaorenCup/CaorenCup.dll'
if (Test-Path -LiteralPath $oldDll) { throw 'Old umbrella DLL would block nested plugin discovery' }
Assert-ReleaseTree $stage $required
Write-ReleaseFileManifest $stage $Version @($plugins.name) $manifest.externalComponents
$result = New-PortableReleaseZip $repoRoot $stage $zipRelative
Write-Output "Created: $($result.Path)"
Write-Output "SHA256: $($result.SHA256)"
Write-Output "Primary plugin count: $($plugins.Count); external CS2Snake is not bundled."
Write-Output 'No upload, deployment, Git operation or Workshop submission was performed.'
