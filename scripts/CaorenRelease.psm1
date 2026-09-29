Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ReleasePath {
    param([string]$Root, [string]$Relative)
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) {
        throw "Expected a relative path: $Relative"
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $Relative))
    if (-not $full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path outside package workspace: $Relative"
    }
    $cursor = $full
    while ($cursor.Length -ge $rootFull.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not allowed: $cursor"
            }
        }
        if ($cursor -eq $rootFull) { break }
        $cursor = Split-Path -Parent $cursor
    }
    return $full
}

function Read-ServerPackageManifest {
    param([string]$RepoRoot)
    $file = Resolve-ReleasePath $RepoRoot 'scripts/server-package-manifest.json'
    $manifest = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or @($manifest.plugins).Count -eq 0) { throw 'Invalid package manifest' }
    $names = @{}
    $directories = @{}
    foreach ($plugin in $manifest.plugins) {
        if ($plugin.name -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $names.ContainsKey($plugin.name)) {
            throw "Duplicate or invalid module: $($plugin.name)"
        }
        if ($plugin.directory -match '(^|[\\/])\.\.([\\/]|$)' -or
            ($plugin.directory -split '[\\/]')[-1] -cne $plugin.name -or
            $directories.ContainsKey($plugin.directory)) { throw 'Invalid plugin destination' }
        $null = Resolve-ReleasePath $RepoRoot ('release-build/check/plugins/CaorenCup/' + $plugin.directory)
        $project = Resolve-ReleasePath $RepoRoot $plugin.project
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Missing project: $($plugin.project)" }
        [xml]$projectXml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
        $assembly = $projectXml.SelectSingleNode('//AssemblyName')
        $outputName = if ($assembly) { $assembly.InnerText } else { [IO.Path]::GetFileNameWithoutExtension($project) }
        if ($outputName -cne $plugin.name) { throw "Project/DLL mismatch: $($plugin.name)" }
        $names[$plugin.name] = $true
        $directories[$plugin.directory] = $true
    }
    $contractProject = Resolve-ReleasePath $RepoRoot $manifest.contracts.project
    if (-not (Test-Path -LiteralPath $contractProject -PathType Leaf)) { throw 'Missing contracts project' }
    return $manifest
}

function Select-ReleaseModules {
    param($Manifest, [string[]]$Modules)
    if (-not $Modules -or $Modules.Count -eq 0) { return @($Manifest.plugins) }
    if (@($Modules | Select-Object -Unique).Count -ne $Modules.Count) { throw 'Repeated module selection' }
    foreach ($name in $Modules) {
        if ($name -notin @($Manifest.plugins.name)) { throw "Unknown or excluded module: $name" }
    }
    return @($Manifest.plugins | Where-Object { $_.name -in $Modules })
}

function Test-ReleaseEntry {
    param([string]$Path)
    $name = $Path.Replace('\', '/')
    if ($name -match '(^|/)\.\.(/|$)' -or $name -match '^/|^[A-Za-z]:' -or
        $name -match '(^|/)(\.git|\.vs|node_modules|bin|obj|runtime|TestResults|release-build|release-output|tests?|backup[^/]*|Diagnostics|ParticleMenu|friberg-game|development)(/|$)' -or
        $name -match '(?i)\.(bak|backup|log|zip|rar|7z|vpk|pem|key|pfx)(-|$|\.)' -or
        $name -match '(?i)\.(db|sqlite|sqlite3)($|-(wal|shm|journal)$)' -or
        $name -match '(?i)(^|/)(\.env(?:\.(?!example$)[^/]+)?|caoren_config\.json|ecosystem\.config\.cjs|player-preferences\.json|menu-user-preferences\.json|identity-store\.json|player-center-sessions\.json|live-session-snapshot\.json|id_rsa|server_io\.py)$') {
        return $false
    }
    return $true
}

function Read-ReleaseAudioCatalog {
    param([string]$EventsPath, [string]$AssetsPath)
    if (-not $EventsPath -or -not $AssetsPath) {
        throw 'PlaySound requires explicit matched audio-events.json and audio-assets.json; empty repository defaults must not replace accepted catalogs.'
    }
    foreach ($path in @($EventsPath, $AssetsPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing audio catalog: $path" }
    }
    $eventsText = Get-Content -LiteralPath $EventsPath -Raw -Encoding UTF8
    $assetsText = Get-Content -LiteralPath $AssetsPath -Raw -Encoding UTF8
    if (-not $eventsText.TrimStart().StartsWith('[') -or -not $assetsText.TrimStart().StartsWith('[')) {
        throw 'Audio catalogs must be JSON arrays'
    }
    # Windows PowerShell 5.1 returns a JSON array as one pipeline object.
    # Enumerate the parsed values explicitly so single-entry catalogs stay flat.
    $parsedEvents = $eventsText | ConvertFrom-Json
    $parsedAssets = $assetsText | ConvertFrom-Json
    $events = @(foreach ($item in $parsedEvents) { $item })
    $assets = @(foreach ($item in $parsedAssets) { $item })
    if ($events.Count -eq 0 -or $assets.Count -eq 0) { throw 'Empty audio catalog is not a release input' }
    foreach ($entries in @($events, $assets)) {
        $ids = @{}
        foreach ($entry in $entries) {
            if ($entry.Id -notmatch '^[a-z0-9]+([._-][a-z0-9]+)*$' -or $ids.ContainsKey($entry.Id)) {
                throw 'Invalid or duplicate audio ID'
            }
            $ids[$entry.Id] = $true
        }
    }
    foreach ($event in $events) {
        if ($event.Source -notmatch '^[A-Za-z0-9_./-]+$' -or $event.Source -match '^/|(^|/)\.\.(/|$)' -or
            $event.Channel -notin @('Effect', 'Music', 'Broadcast') -or
            $event.DefaultVolume -lt 0 -or $event.DefaultVolume -gt 1 -or
            [double]::IsNaN([double]$event.DefaultVolume) -or [double]::IsInfinity([double]$event.DefaultVolume) -or
            $event.Loop -isnot [bool] -or $event.NativeEvent -ne $true) { throw 'Invalid audio event fields' }
    }
    foreach ($asset in $assets) {
        if ($asset.Id -notin @($events.Id) -or $asset.DurationSeconds -le 0 -or
            [double]::IsNaN([double]$asset.DurationSeconds) -or [double]::IsInfinity([double]$asset.DurationSeconds) -or
            @($asset.Resources).Count -ne 2) { throw 'Audio assets/events mismatch' }
        $event = @($events | Where-Object { $_.Id -ceq $asset.Id })[0]
        if ($event.Source -cne $asset.SoundEvent) { throw 'Audio event source mismatch' }
        if ($asset.LoopSoundEvent -notmatch '^[A-Za-z0-9_./-]+$' -or $asset.LoopSoundEvent -match '^/|(^|/)\.\.(/|$)') {
            throw 'Invalid loop sound event'
        }
        if ($asset.PSObject.Properties['SourceFile']) {
            if ($asset.SourceFile -match '^[\\/]|^[A-Za-z]:|(^|[\\/])\.\.([\\/]|$)') {
                throw 'SourceFile must not expose absolute/private paths'
            }
        }
        foreach ($resource in $asset.Resources) {
            if ($resource -notmatch '^sounds/[A-Za-z0-9_./-]+\.vsnd$' -or $resource -match '(^|/)\.\.(/|$)') {
                throw 'Invalid audio resource path'
            }
        }
    }
    return @{ Events = $events; Assets = $assets }
}

function Assert-WebReleaseBoundary {
    param([string]$RepoRoot, [string]$Version)
    if ($Version -match '^v1\.10\.') {
        $server = Get-Content -LiteralPath (Resolve-ReleasePath $RepoRoot 'web-command-center/src/server.ts') -Raw -Encoding UTF8
        if ($server -match 'registerSteamAuthRoutes|steam-auth-routes') {
            throw 'v1.10 web release blocked: deferred Steam login is still wired into server.ts. Isolate the deferred source changes first; packaging does not rewrite authentication code.'
        }
        $identity = Resolve-ReleasePath $RepoRoot 'web-command-center/src/identity'
        foreach ($file in Get-ChildItem -LiteralPath $identity -File -Filter '*.ts') {
            if ($file.Name -match '\.test\.ts$|^steam-(auth|openid)') { continue }
            $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
            if ($text -match 'findOrCreateSteamAccount|steam_only|hasBackupPassword') {
                throw "v1.10 web release blocked: deferred identity changes remain in $($file.Name)"
            }
        }
    }
}

function Copy-ReleaseWebTree {
    param([string]$RepoRoot, [string]$Destination, [string]$Version)
    $source = Resolve-ReleasePath $RepoRoot 'web-command-center'
    foreach ($item in Get-ChildItem -LiteralPath $source -File -Recurse -Force) {
        $relative = $item.FullName.Substring($source.Length).TrimStart('\', '/').Replace('\', '/')
        if (-not (Test-ReleaseEntry $relative) -or $relative -match '^(CaorenCupPlugin|CaorenCupPlugin.Tests|scripts|public/weaponpaints)/' -or
            $relative -match '\.test\.[jt]s$|postmatch-mock-test\.html$' -or
            ($Version -match '^v1\.10\.' -and $relative -match '(^|/)steam-(auth|openid)[^/]*\.ts$')) { continue }
        $null = Resolve-ReleasePath $source $relative
        $target = Resolve-ReleasePath $Destination $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath $item.FullName -Destination $target
    }
    $data = Resolve-ReleasePath $RepoRoot 'game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/data'
    foreach ($item in Get-ChildItem -LiteralPath $data -File -Recurse -Filter '*.json') {
        $relative = $item.FullName.Substring($data.Length).TrimStart('\', '/')
        $null = Resolve-ReleasePath $data $relative
        $target = Resolve-ReleasePath $Destination ('weaponpaints-data/' + $relative)
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath $item.FullName -Destination $target
    }
    $component = Resolve-ReleasePath $RepoRoot 'game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints'
    foreach ($name in @('LICENSE', 'UPSTREAM.md')) {
        Copy-Item -LiteralPath (Join-Path $component $name) -Destination (Resolve-ReleasePath $Destination ('weaponpaints-data/' + $name))
    }
}

function Copy-ReleasePublishTree {
    param([string]$Source, [string]$Destination)
    foreach ($item in Get-ChildItem -LiteralPath $Source -File -Recurse) {
        $relative = $item.FullName.Substring($Source.Length).TrimStart('\', '/').Replace('\', '/')
        if (-not (Test-ReleaseEntry $relative) -or
            $relative -match '^(resources|module-configs|gamedata)/' -or
            $item.Name -in @('CaorenCupContracts.dll', 'CaorenCupContracts.pdb', 'CaorenCupContracts.deps.json', 'CounterStrikeSharp.API.dll', 'CounterStrikeSharp.API.pdb', 'CounterStrikeSharp.API.xml')) { continue }
        $allowed = $relative -match '\.(dll|pdb|so|dylib)$|(\.deps|\.runtimeconfig)\.json$' -or
            $relative -match '^data/(skin-rarities\.json|(en|zh-CN)/(skins|stickers|music|keychains|gloves|collectibles|agents)\.json)$' -or
            $item.Name -in @('LICENSE', 'LICENSE.txt', 'README.md', 'UPSTREAM.md')
        if (-not $allowed) { continue }
        $null = Resolve-ReleasePath $Source $relative
        $target = Resolve-ReleasePath $Destination $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath $item.FullName -Destination $target
    }
}

function Assert-ReleaseTree {
    param([string]$Root, [string[]]$RequiredEntries)
    foreach ($required in $RequiredEntries) {
        if (-not (Test-Path -LiteralPath (Resolve-ReleasePath $Root $required) -PathType Leaf)) {
            throw "Package missing required entry: $required"
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
        $null = Resolve-ReleasePath $Root $relative
        if (-not (Test-ReleaseEntry $relative)) { throw "Forbidden package entry: $relative" }
    }
}

function Write-ReleaseFileManifest {
    param([string]$Root, [string]$Version, [string[]]$Modules, $ExternalComponents)
    $files = @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{
            path = $_.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    $manifest = @{ version = $Version; modules = @($Modules); externalComponents = @($ExternalComponents); files = $files }
    [IO.File]::WriteAllText((Join-Path $Root 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
}

function New-PortableReleaseZip {
    param([string]$RepoRoot, [string]$Stage, [string]$ZipRelativePath)
    $zipPath = Resolve-ReleasePath $RepoRoot $ZipRelativePath
    if (Test-Path -LiteralPath $zipPath) { throw "Output already exists: $zipPath" }
    Assert-ReleaseTree $Stage @('package-manifest.json')
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $zipPath) -Force
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryPath = Resolve-ReleasePath $RepoRoot ($ZipRelativePath + '.partial-' + [Guid]::NewGuid().ToString('N'))
    $archive = [IO.Compression.ZipFile]::Open($temporaryPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Stage -File -Recurse | Sort-Object FullName) {
            $relative = $file.FullName.Substring($Stage.Length).TrimStart('\', '/').Replace('\', '/')
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
    $check = [IO.Compression.ZipFile]::OpenRead($temporaryPath)
    try {
        if ($check.Entries.Count -ne @(Get-ChildItem -LiteralPath $Stage -File -Recurse).Count) { throw 'ZIP entry count mismatch' }
        foreach ($entry in $check.Entries) {
            if ($entry.FullName.Contains('\') -or -not (Test-ReleaseEntry $entry.FullName)) { throw 'ZIP entry validation failed' }
        }
    } finally { $check.Dispose() }
    # Both paths were checked within RepoRoot. Failed candidates are retained for diagnosis.
    Move-Item -LiteralPath $temporaryPath -Destination $zipPath
    return @{ Path = $zipPath; SHA256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash }
}

Export-ModuleMember -Function Resolve-ReleasePath, Read-ServerPackageManifest, Select-ReleaseModules, Test-ReleaseEntry, Read-ReleaseAudioCatalog, Assert-WebReleaseBoundary, Copy-ReleaseWebTree, Copy-ReleasePublishTree, Assert-ReleaseTree, Write-ReleaseFileManifest, New-PortableReleaseZip
