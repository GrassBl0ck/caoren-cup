Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'CaorenRelease.psm1') -Force
$fixture = Resolve-ReleasePath $repo ('release-build/package-tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$script:passed = 0

function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passed++
    Write-Output "PASS: $Name"
}
function Reject([scriptblock]$Action, [string]$Name) {
    $rejected = $false
    try { $null = & $Action } catch { $rejected = $true }
    Check $rejected $Name
}
function Put([string]$Relative, [string]$Text) {
    $path = Resolve-ReleasePath $fixture $Relative
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
    [IO.File]::WriteAllText($path, $Text, (New-Object Text.UTF8Encoding($false)))
    return $path
}

$manifest = Read-ServerPackageManifest $repo
Check (@($manifest.plugins).Count -eq 13) '13 buildable plugin projects and destinations'
Check (@($manifest.externalComponents.name) -contains 'CS2Snake') 'Snake is an explicit external component'
$selected = @(Select-ReleaseModules $manifest @('CaorenDuel', 'CS2MiniGames'))
Check ($selected.Count -eq 2 -and $selected[0].directory -eq 'CaorenCupGamemodes/CaorenCupSolos/CaorenDuel') 'partial selection preserves nested directories'
Reject { Select-ReleaseModules $manifest @('ParticleMenu') } 'paused module cannot be selected'
Reject { Select-ReleaseModules $manifest @('CS2Snake') } 'unsupported external binary cannot be selected'
Reject { Select-ReleaseModules $manifest @('CaorenDuel', 'CaorenDuel') } 'duplicate module selection'
Reject { Resolve-ReleasePath $fixture '../escape' } 'path traversal rejected'
Reject { Resolve-ReleasePath $fixture $repo } 'absolute relative-path input rejected'
foreach ($path in @('.env', '.env.production', 'data/player-preferences.json', 'runtime/identity-store.json',
    'node_modules/private.js', 'data/scores.db-wal', 'data/settings.sqlite', 'a.json.bak-old', 'backup_round001.txt',
    'server_io.py', 'private.key', 'resources/a.vpk', 'Diagnostics/a.dll', 'friberg-game/a.ts')) {
    Check (-not (Test-ReleaseEntry $path)) ("excluded runtime/private entry: " + $path)
}
Check (Test-ReleaseEntry 'ecosystem.config.cjs.example') 'example config allowed'
Check (Test-ReleaseEntry 'runtimes/linux-x64/native/libe_sqlite3.so') 'native runtime dependency allowed'
Check (Test-ReleaseEntry 'Microsoft.Data.Sqlite.dll') 'SQLite library is not a user database'

$eventsPath = Put 'audio/events.json' '[{"Id":"music.demo","Source":"caorencup.demo","DefaultVolume":1,"Channel":"Music","Loop":false,"NativeEvent":true}]'
$assetsPath = Put 'audio/assets.json' '[{"Id":"music.demo","DurationSeconds":3,"SoundEvent":"caorencup.demo","LoopSoundEvent":"caorencup.demo.loop","Resources":["sounds/demo.vsnd","sounds/demo_loop.vsnd"]}]'
$audio = Read-ReleaseAudioCatalog $eventsPath $assetsPath
Check ($audio.Events.Count -eq 1 -and $audio.Assets.Count -eq 1) 'matched explicit audio catalogs accepted'
$emptyPath = Put 'audio/empty.json' '[]'
Reject { Read-ReleaseAudioCatalog $emptyPath $assetsPath } 'empty catalog cannot replace accepted content'
$wrongPath = Put 'audio/wrong.json' '[{"Id":"music.other","DurationSeconds":3,"SoundEvent":"caorencup.demo","Resources":["sounds/demo.vsnd","sounds/demo_loop.vsnd"]}]'
Reject { Read-ReleaseAudioCatalog $eventsPath $wrongPath } 'mismatched audio catalogs rejected'
Reject { Read-ReleaseAudioCatalog '' '' } 'audio catalogs require explicit input'
$invalidVolume = Put 'audio/invalid-volume.json' '[{"Id":"music.demo","Source":"caorencup.demo","DefaultVolume":2,"Channel":"Music","Loop":false,"NativeEvent":true}]'
Reject { Read-ReleaseAudioCatalog $invalidVolume $assetsPath } 'invalid default volume rejected'
$privateSource = Put 'audio/private-source.json' '[{"Id":"music.demo","DurationSeconds":3,"SoundEvent":"caorencup.demo","LoopSoundEvent":"caorencup.demo.loop","Resources":["sounds/demo.vsnd","sounds/demo_loop.vsnd"],"SourceFile":"C:/private/audio.ogg"}]'
Reject { Read-ReleaseAudioCatalog $eventsPath $privateSource } 'absolute audio source path rejected'

$boundary = Resolve-ReleasePath $fixture 'boundary'
$null = Put 'boundary/web-command-center/src/server.ts' 'import { registerSteamAuthRoutes } from "./identity/steam-auth-routes";'
$null = Put 'boundary/web-command-center/src/identity/identity-types.ts' 'export type State = "active";'
Reject { Assert-WebReleaseBoundary $boundary 'v1.10.0' } 'deferred Steam server entry blocks v1.10'
$null = Put 'boundary/web-command-center/src/server.ts' 'console.log("existing login");'
$null = Put 'boundary/web-command-center/src/identity/identity-types.ts' 'export type State = "steam_only";'
Reject { Assert-WebReleaseBoundary $boundary 'v1.10.0' } 'deferred identity changes also block v1.10'
$null = Put 'boundary/web-command-center/src/identity/identity-types.ts' 'export type State = "active";'
Assert-WebReleaseBoundary $boundary 'v1.10.0'
Check $true 'clean existing-login fixture accepted'

$null = Put 'publish/Demo.dll' 'fixture DLL'
$null = Put 'publish/Demo.deps.json' '{}'
$null = Put 'publish/CounterStrikeSharp.API.dll' 'must not ship'
$null = Put 'publish/CaorenCupContracts.dll' 'shared only'
$null = Put 'publish/module-configs/presets.grass.json' '{}'
$null = Put 'publish/data/player-preferences.json' '{"private":true}'
$null = Put 'publish/data/en/skins.json' '[]'
$null = Put 'publish/runtimes/linux-x64/native/libe_sqlite3.so' 'native fixture'
$publish = Resolve-ReleasePath $fixture 'publish'
$copy = Resolve-ReleasePath $fixture 'stage/addons/counterstrikesharp/plugins/CaorenCup/Demo'
Copy-ReleasePublishTree $publish $copy
Check (Test-Path -LiteralPath (Join-Path $copy 'Demo.dll')) 'primary DLL copied'
Check (-not (Test-Path -LiteralPath (Join-Path $copy 'CounterStrikeSharp.API.dll'))) 'framework API omitted'
Check (-not (Test-Path -LiteralPath (Join-Path $copy 'CaorenCupContracts.dll'))) 'shared contract not duplicated'
Check (-not (Test-Path -LiteralPath (Join-Path $copy 'module-configs'))) 'default config not installed over runtime data'
Check (-not (Test-Path -LiteralPath (Join-Path $copy 'data/player-preferences.json'))) 'personal preferences not copied'
Check (Test-Path -LiteralPath (Join-Path $copy 'runtimes/linux-x64/native/libe_sqlite3.so')) 'native dependency copied'
Check (Test-Path -LiteralPath (Join-Path $copy 'data/en/skins.json')) 'item data copied'

$null = Put 'boundary/web-command-center/package.json' '{}'
$null = Put 'boundary/web-command-center/.env' 'must-not-ship'
$null = Put 'boundary/web-command-center/public/index.html' '<html>fixture</html>'
$null = Put 'boundary/web-command-center/public/index.html.bak-old' 'must-not-ship'
$null = Put 'boundary/web-command-center/node_modules/private.txt' 'must-not-ship'
$null = Put 'boundary/web-command-center/CaorenCupPlugin/secret.dll' 'separate server package'
$null = Put 'boundary/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/data/skin-rarities.json' '{}'
$null = Put 'boundary/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/LICENSE' 'fixture license'
$null = Put 'boundary/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/UPSTREAM.md' 'fixture upstream'
$webStage = Resolve-ReleasePath $fixture 'web-stage'
Copy-ReleaseWebTree $boundary $webStage 'v1.10.0'
Check (-not (Test-Path -LiteralPath (Join-Path $webStage '.env'))) 'web production environment excluded'
Check (-not (Test-Path -LiteralPath (Join-Path $webStage 'CaorenCupPlugin'))) 'bridge not duplicated in web ZIP'
Check (-not (Test-Path -LiteralPath (Join-Path $webStage 'node_modules'))) 'web node_modules excluded'
Check (Test-Path -LiteralPath (Join-Path $webStage 'weaponpaints-data/LICENSE')) 'web data retains component license'

$null = Put 'stage/安装说明.txt' 'Unicode ZIP paths preserved'
$stage = Resolve-ReleasePath $fixture 'stage'
Assert-ReleaseTree $stage @('addons/counterstrikesharp/plugins/CaorenCup/Demo/Demo.dll')
Reject { Assert-ReleaseTree $stage @('missing.dll') } 'missing primary entry rejected'
Write-ReleaseFileManifest $stage 'v0.0.0' @('Demo') @()
$zipRelative = $fixture.Substring($repo.Length).TrimStart('\', '/').Replace('\', '/') + '/fixture.zip'
$result = New-PortableReleaseZip $repo $stage $zipRelative
$zip = [IO.Compression.ZipFile]::OpenRead($result.Path)
try {
    $names = @($zip.Entries.FullName)
    Check ($names -contains '安装说明.txt') 'Unicode ZIP entry round trip'
    Check ($names -contains 'addons/counterstrikesharp/plugins/CaorenCup/Demo/Demo.dll') 'ZIP nested installation tree preserved'
    $entry = $zip.GetEntry('package-manifest.json')
    $reader = New-Object IO.StreamReader($entry.Open(), [Text.Encoding]::UTF8)
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    Check (-not $text.Contains($repo) -and -not $text.Contains($fixture)) 'file manifest contains no private absolute paths'
} finally { $zip.Dispose() }
Reject { New-PortableReleaseZip $repo $stage $zipRelative } 'existing ZIP cannot be overwritten'
Check ($result.SHA256.Length -eq 64) 'ZIP SHA256 provided'
Write-Output "Passed: $script:passed checks. Fixture retained inside ignored release-build for inspection."
