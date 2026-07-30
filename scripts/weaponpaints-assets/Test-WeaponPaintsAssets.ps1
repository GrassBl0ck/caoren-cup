param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'WeaponPaintsAssets.psm1'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..'))
$testRoot = Join-Path $repoRoot 'runtime\weaponpaints-assets\tests\unknown-missing'
$safeTestParent = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'runtime\weaponpaints-assets\tests'))

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw "ASSERT FAILED: $Message"
    }
}

function Reset-TestRoot {
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($safeTestParent + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside the test root: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

function Write-TestJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object]$Value
    )

    $json = ($Value | ConvertTo-Json -Depth 20) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($Path, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
}

function Read-ZipJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ZipPath,
        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $zip.GetEntry($EntryName)
        Assert-True ($null -ne $entry) "Missing ZIP entry: $EntryName"
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try {
            return ($reader.ReadToEnd() | ConvertFrom-Json)
        } finally {
            $reader.Dispose()
        }
    } finally {
        $zip.Dispose()
    }
}

Reset-TestRoot
try {
    if (Test-Path -LiteralPath $modulePath) {
        Import-Module $modulePath -Force
    }
    $command = Get-Command Test-WeaponPaintsMissingImages -ErrorAction SilentlyContinue
    Assert-True ($null -ne $command) 'Test-WeaponPaintsMissingImages is not implemented.'

    $message = ''
    try {
        Test-WeaponPaintsMissingImages -MissingPaths @('weapon_ak47-9999.png') -KnownMissingPaths @('weapon_awp-9998.png')
    } catch {
        $message = $_.Exception.Message
    }

    Assert-True ($message -match 'UNKNOWN_MISSING_IMAGE') 'Unknown missing images must fail validation.'
    Write-Host 'PASS: unknown missing images block packaging.'

    $message = ''
    try {
        Test-WeaponPaintsMissingImages -MissingPaths @() -KnownMissingPaths @('weapon_awp-9998.png')
    } catch {
        $message = $_.Exception.Message
    }
    Assert-True ($message -match 'KNOWN_MISSING_SET_CHANGED') 'Resolved allowlisted paths must require an explicit source update.'
    Write-Host 'PASS: the known missing-image set must match exactly.'

    $mergeCommand = Get-Command Merge-WeaponPaintsCatalogItems -ErrorAction SilentlyContinue
    Assert-True ($null -ne $mergeCommand) 'Merge-WeaponPaintsCatalogItems is not implemented.'

    $englishAgents = @(
        [pscustomobject][ordered]@{
            team = 2
            model = 'characters/models/tm_phoenix/tm_phoenix_varianta.vmdl'
            agent_name = 'Phoenix'
            image = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/agent-5105.png'
        }
    )
    $mergedAgents = @(Merge-WeaponPaintsCatalogItems -Category 'agent' -EnglishItems $englishAgents -ChineseItems @())
    Assert-True ($mergedAgents.Count -eq 1) 'One English agent must produce one manifest item.'
    Assert-True ($mergedAgents[0].name -eq 'Phoenix') 'Missing Chinese agent data must fall back to English.'
    Assert-True ($mergedAgents[0].englishName -eq 'Phoenix') 'The English name must remain searchable.'
    Write-Host 'PASS: missing Chinese agent data falls back to English.'

    $englishSkins = @(
        [pscustomobject][ordered]@{
            weapon_defindex = 7
            weapon_name = 'weapon_ak47'
            paint = 180
            paint_name = 'AK-47 | Fire Serpent'
            image = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/weapon_ak47-180.png'
            legacy_model = $false
        }
    )
    $chineseSkins = @(
        [pscustomobject][ordered]@{
            weapon_defindex = 7
            weapon_name = 'weapon_ak47'
            paint = 180
            paint_name = 'AK-47 | Fire Serpent ZH'
            image = 'https://example.invalid/ignored.png'
            legacy_model = $false
        }
    )
    $mergedSkins = @(Merge-WeaponPaintsCatalogItems -Category 'skin' -EnglishItems $englishSkins -ChineseItems $chineseSkins)
    Assert-True ($mergedSkins[0].key -eq 'weapon_ak47:180') 'Skin keys must combine weapon name and paint id.'
    Assert-True ($mergedSkins[0].name -eq 'AK-47 | Fire Serpent ZH') 'Skin names must use matching Chinese data.'
    Assert-True ($mergedSkins[0].imageSource -match 'weapon_ak47-180\.png$') 'English image URLs must be authoritative.'
    Write-Host 'PASS: skin records merge by stable key.'

    $englishGloves = @(
        [pscustomobject][ordered]@{
            weapon_defindex = 5030
            paint = 10006
            paint_name = 'Sport Gloves | Vice'
            image = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/sporty_gloves-10006.png'
        }
    )
    $mergedGloves = @(Merge-WeaponPaintsCatalogItems -Category 'glove' -EnglishItems $englishGloves -ChineseItems @())
    Assert-True ($mergedGloves[0].key -eq '5030:10006') 'Glove keys must combine defindex and paint id.'
    Assert-True ($mergedGloves[0].defIndex -eq 5030) 'Glove defindex must be preserved.'
    Write-Host 'PASS: glove records use defindex and paint id.'

    $englishStickers = @(
        [pscustomobject][ordered]@{
            id = '42'
            name = 'Sticker | Test'
            image = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/sticker-42.png'
        }
    )
    $chineseStickers = @(
        [pscustomobject][ordered]@{
            id = '42'
            name = 'Sticker Test ZH'
            image = 'https://example.invalid/ignored.png'
        }
    )
    $mergedStickers = @(Merge-WeaponPaintsCatalogItems -Category 'sticker' -EnglishItems $englishStickers -ChineseItems $chineseStickers)
    Assert-True ($mergedStickers[0].key -eq '42') 'Simple catalog keys must use the numeric id.'
    Assert-True ($mergedStickers[0].name -eq 'Sticker Test ZH') 'Simple catalog names must use matching Chinese data.'
    Write-Host 'PASS: simple records merge by numeric id.'

    $sourceConfigPath = Join-Path $scriptRoot 'source-v1.9.0.json'
    Assert-True (Test-Path -LiteralPath $sourceConfigPath) 'The pinned source configuration is missing.'
    $sourceConfig = Get-Content -LiteralPath $sourceConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($sourceConfig.commit -eq 'fa8936f3959310acf94de410bc5bd0015f34ff24') 'The v1.9.0 source commit must remain pinned.'
    Assert-True (@($sourceConfig.knownMissingImages).Count -eq 49) 'The approved missing-image allowlist must contain exactly 49 paths.'
    Write-Host 'PASS: v1.9.0 source metadata is pinned.'

    $metadataCommand = Get-Command Get-WeaponPaintsImageMetadata -ErrorAction SilentlyContinue
    Assert-True ($null -ne $metadataCommand) 'Get-WeaponPaintsImageMetadata is not implemented.'
    $pngPath = Join-Path $testRoot 'one-pixel.png'
    $pngBytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
    [System.IO.File]::WriteAllBytes($pngPath, $pngBytes)
    $metadata = Get-WeaponPaintsImageMetadata -Path $pngPath
    Assert-True ($metadata.width -eq 1 -and $metadata.height -eq 1) 'PNG dimensions must be read from IHDR.'
    Assert-True ($metadata.sha256 -match '^[0-9a-f]{64}$') 'Image SHA-256 must be lowercase hexadecimal.'
    Write-Host 'PASS: PNG metadata includes dimensions and SHA-256.'

    $packageCommand = Get-Command New-WeaponPaintsAssetPackages -ErrorAction SilentlyContinue
    Assert-True ($null -ne $packageCommand) 'New-WeaponPaintsAssetPackages is not implemented.'

    $fixtureRoot = Join-Path $testRoot 'source'
    $dataRoot = Join-Path $fixtureRoot 'website\data'
    $imageRoot = Join-Path $fixtureRoot 'website\img\skins'
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $imageRoot -Force | Out-Null

    foreach ($fileName in @(
        'agents_en.json', 'agents_zh-CN.json',
        'collectibles_en.json', 'collectibles_zh-CN.json',
        'gloves_en.json', 'gloves_zh-CN.json',
        'keychains_en.json', 'keychains_zh-CN.json',
        'music_en.json', 'music_zh-CN.json',
        'skins_en.json', 'skins_zh-CN.json',
        'stickers_en.json', 'stickers_zh-CN.json'
    )) {
        Write-TestJson -Path (Join-Path $dataRoot $fileName) -Value @()
    }

    $skinUrl = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/weapon_ak47-180.png'
    $agentUrl = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/agent-5105.png'
    $missingGloveUrl = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/sporty_gloves-10006.png'
    $stickerUrl = 'https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/img/skins/sticker-42.png'

    Write-TestJson -Path (Join-Path $dataRoot 'skins_en.json') -Value @(
        [ordered]@{ weapon_defindex = 7; weapon_name = 'weapon_ak47'; paint = 180; paint_name = 'AK-47 | Fire Serpent'; image = $skinUrl; legacy_model = $false }
    )
    Write-TestJson -Path (Join-Path $dataRoot 'skins_zh-CN.json') -Value @(
        [ordered]@{ weapon_defindex = 7; weapon_name = 'weapon_ak47'; paint = 180; paint_name = 'AK-47 | Fire Serpent ZH'; image = $skinUrl; legacy_model = $false }
    )
    Write-TestJson -Path (Join-Path $dataRoot 'agents_en.json') -Value @(
        [ordered]@{ team = 2; model = 'characters/test.vmdl'; agent_name = 'Test Agent'; image = $agentUrl }
    )
    Write-TestJson -Path (Join-Path $dataRoot 'gloves_en.json') -Value @(
        [ordered]@{ weapon_defindex = 5030; paint = 10006; paint_name = 'Sport Gloves | Vice'; image = $missingGloveUrl }
    )
    Write-TestJson -Path (Join-Path $dataRoot 'stickers_en.json') -Value @(
        [ordered]@{ id = '42'; name = 'Sticker | Test'; image = $stickerUrl }
    )

    foreach ($fileName in @('weapon_ak47-180.png', 'agent-5105.png', 'sticker-42.png', 'unused.png')) {
        [System.IO.File]::WriteAllBytes((Join-Path $imageRoot $fileName), $pngBytes)
    }

    $fixtureConfigPath = Join-Path $testRoot 'fixture-source.json'
    Write-TestJson -Path $fixtureConfigPath -Value ([ordered]@{
        schemaVersion = 1
        packVersion = 'v1.9.0'
        repository = 'https://example.invalid/upstream.git'
        commit = '0123456789012345678901234567890123456789'
        commitTimestamp = '2026-07-27T10:00:39+02:00'
        expectedImageTreeCount = 4
        expectedImageTreeBytes = $pngBytes.Length * 4
        knownMissingImages = @('website/img/skins/sporty_gloves-10006.png')
    })

    $firstOutput = Join-Path $testRoot 'output-1'
    $secondOutput = Join-Path $testRoot 'output-2'
    $first = New-WeaponPaintsAssetPackages -SourceRoot $fixtureRoot -OutputRoot $firstOutput -ConfigPath $fixtureConfigPath
    $second = New-WeaponPaintsAssetPackages -SourceRoot $fixtureRoot -OutputRoot $secondOutput -ConfigPath $fixtureConfigPath

    Assert-True (Test-Path -LiteralPath $first.baseZip) 'The base image ZIP must be created.'
    Assert-True (Test-Path -LiteralPath $first.stickersZip) 'The sticker image ZIP must be created.'
    Assert-True ((Get-FileHash -LiteralPath $first.baseZip -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $second.baseZip -Algorithm SHA256).Hash) 'Base packages must be reproducible.'
    Assert-True ((Get-FileHash -LiteralPath $first.stickersZip -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $second.stickersZip -Algorithm SHA256).Hash) 'Sticker packages must be reproducible.'

    $baseManifest = Read-ZipJson -ZipPath $first.baseZip -EntryName 'weaponpaints/base/manifest.json'
    $stickerManifest = Read-ZipJson -ZipPath $first.stickersZip -EntryName 'weaponpaints/stickers/manifest.json'
    Assert-True (@($baseManifest.items).Count -eq 3) 'The base manifest must contain skin, agent, and missing glove records.'
    Assert-True (@($baseManifest.items | Where-Object { -not $_.available }).Count -eq 1) 'Known missing images must remain in the manifest as unavailable.'
    Assert-True (@($stickerManifest.items).Count -eq 1) 'The sticker manifest must be isolated in its own package.'
    Assert-True (($baseManifest | ConvertTo-Json -Depth 20) -notmatch 'raw\.githubusercontent\.com') 'Runtime manifests must not contain remote image URLs.'

    $report = Get-Content -LiteralPath $first.report -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True (@($report.unusedImages) -contains 'website/img/skins/unused.png') 'Unused upstream images must be reported.'
    Assert-True (@($report.duplicateGroups).Count -eq 1) 'Duplicate image content must be grouped in the report.'
    Write-Host 'PASS: fixture builds deterministic split packages and reports anomalies.'

    $entryScripts = @(
        (Join-Path $scriptRoot 'Sync-WeaponPaintsAssets.ps1'),
        (Join-Path $repoRoot 'scripts\package-weaponpaints-assets.ps1')
    )
    foreach ($entryScript in $entryScripts) {
        Assert-True (Test-Path -LiteralPath $entryScript) "Missing entry script: $entryScript"
        $tokens = $null
        $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($entryScript, [ref]$tokens, [ref]$parseErrors) | Out-Null
        Assert-True (@($parseErrors).Count -eq 0) "Entry script has parse errors: $entryScript"
    }
    Write-Host 'PASS: sync and package entry scripts parse successfully.'
} finally {
    Reset-TestRoot
}
