Set-StrictMode -Version Latest

function Get-BigEndianUInt32 {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes,
        [Parameter(Mandatory = $true)]
        [int]$Offset
    )

    return [uint32](
        ([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor
        [uint32]$Bytes[$Offset + 3]
    )
}

function Get-WeaponPaintsImageMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $bytes = [System.IO.File]::ReadAllBytes($resolved)
    $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    if ($bytes.Length -lt 24) {
        throw "INVALID_PNG: $resolved"
    }
    for ($index = 0; $index -lt $signature.Length; $index++) {
        if ($bytes[$index] -ne $signature[$index]) {
            throw "INVALID_PNG: $resolved"
        }
    }
    if ([System.Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ne 'IHDR') {
        throw "INVALID_PNG_IHDR: $resolved"
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($bytes)
    } finally {
        $sha256.Dispose()
    }

    [pscustomobject][ordered]@{
        bytes = [int64]$bytes.Length
        sha256 = ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
        width = [int](Get-BigEndianUInt32 -Bytes $bytes -Offset 16)
        height = [int](Get-BigEndianUInt32 -Bytes $bytes -Offset 20)
    }
}

function Get-WeaponPaintsImageSourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImageUrl
    )

    $uri = [System.Uri]$ImageUrl
    $decodedPath = [System.Uri]::UnescapeDataString($uri.AbsolutePath).Replace('\', '/')
    $marker = '/website/img/skins/'
    $markerIndex = $decodedPath.IndexOf($marker, [System.StringComparison]::Ordinal)
    if ($markerIndex -lt 0) {
        throw "INVALID_IMAGE_URL: $ImageUrl"
    }
    $relative = $decodedPath.Substring($markerIndex + 1)
    if ($relative -match '(^|/)\.\.(/|$)' -or $relative -notmatch '^website/img/skins/[^/]+\.png$') {
        throw "INVALID_IMAGE_PATH: $ImageUrl"
    }
    return $relative
}

function Read-WeaponPaintsJsonArray {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $value = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $value) {
        return
    }
    @($value)
}

function ConvertTo-WeaponPaintsJson {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    return (($Value | ConvertTo-Json -Depth 30) -replace "`r`n", "`n") + "`n"
}

function Write-WeaponPaintsUtf8Text {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Add-WeaponPaintsZipTextEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)]
        [string]$EntryName,
        [Parameter(Mandatory = $true)]
        [string]$Text,
        [Parameter(Mandatory = $true)]
        [System.DateTimeOffset]$Timestamp
    )

    $entry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = $Timestamp
    $stream = $entry.Open()
    $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
    try {
        $writer.Write($Text)
    } finally {
        $writer.Dispose()
    }
}

function Add-WeaponPaintsZipFileEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)]
        [string]$EntryName,
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [System.DateTimeOffset]$Timestamp
    )

    $entry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = $Timestamp
    $input = [System.IO.File]::OpenRead($SourcePath)
    $output = $entry.Open()
    try {
        $input.CopyTo($output)
    } finally {
        $output.Dispose()
        $input.Dispose()
    }
}

function New-WeaponPaintsDeterministicZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ZipPath,
        [Parameter(Mandatory = $true)]
        [string]$PackRoot,
        [Parameter(Mandatory = $true)]
        [string]$ManifestJson,
        [Parameter(Mandatory = $true)]
        [object[]]$Assets,
        [Parameter(Mandatory = $true)]
        [System.DateTimeOffset]$Timestamp
    )

    if (Test-Path -LiteralPath $ZipPath) {
        throw "OUTPUT_ALREADY_EXISTS: $ZipPath"
    }
    $partialPath = "$ZipPath.partial"
    if (Test-Path -LiteralPath $partialPath) {
        throw "PARTIAL_OUTPUT_ALREADY_EXISTS: $partialPath"
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($partialPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Add-WeaponPaintsZipTextEntry -Archive $archive -EntryName "$PackRoot/manifest.json" -Text $ManifestJson -Timestamp $Timestamp
        $sumLines = @($Assets | Sort-Object packagePath | ForEach-Object { "$($_.sha256)  $($_.packagePath)" })
        $sumText = if ($sumLines.Count -gt 0) { ($sumLines -join "`n") + "`n" } else { '' }
        Add-WeaponPaintsZipTextEntry -Archive $archive -EntryName "$PackRoot/SHA256SUMS.txt" -Text $sumText -Timestamp $Timestamp
        foreach ($asset in @($Assets | Sort-Object packagePath)) {
            Add-WeaponPaintsZipFileEntry -Archive $archive -EntryName "$PackRoot/$($asset.packagePath)" -SourcePath $asset.sourcePath -Timestamp $Timestamp
        }
    } finally {
        $archive.Dispose()
    }
    [System.IO.File]::Move($partialPath, $ZipPath)
}

function New-WeaponPaintsAssetPackages {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,

        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,

        [Parameter(Mandatory = $true)]
        [string]$ConfigPath
    )

    $sourceFull = (Resolve-Path -LiteralPath $SourceRoot).Path
    $configFull = (Resolve-Path -LiteralPath $ConfigPath).Path
    $config = Get-Content -LiteralPath $configFull -Raw -Encoding UTF8 | ConvertFrom-Json
    $dataRoot = Join-Path $sourceFull 'website\data'
    $imageRoot = Join-Path $sourceFull 'website\img\skins'
    if (-not (Test-Path -LiteralPath $dataRoot -PathType Container)) {
        throw "DATA_ROOT_NOT_FOUND: $dataRoot"
    }
    if (-not (Test-Path -LiteralPath $imageRoot -PathType Container)) {
        throw "IMAGE_ROOT_NOT_FOUND: $imageRoot"
    }

    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    $outputFull = (Resolve-Path -LiteralPath $OutputRoot).Path
    $baseLabel = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5Z+656GA5Zu+54mH'))
    $stickersLabel = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5Y2w6Iqx5Zu+54mH'))
    $baseZip = Join-Path $outputFull "CaorenCupWeaponPaintsImages-$baseLabel-$($config.packVersion).zip"
    $stickersZip = Join-Path $outputFull "CaorenCupWeaponPaintsImages-$stickersLabel-$($config.packVersion).zip"
    $reportPath = Join-Path $outputFull "CaorenCupWeaponPaintsImages-report-$($config.packVersion).json"
    foreach ($target in @($baseZip, $stickersZip, $reportPath, "$baseZip.sha256", "$stickersZip.sha256")) {
        if (Test-Path -LiteralPath $target) {
            throw "OUTPUT_ALREADY_EXISTS: $target"
        }
    }

    $definitions = @(
        [ordered]@{ category = 'skin'; file = 'skins'; pack = 'base' },
        [ordered]@{ category = 'glove'; file = 'gloves'; pack = 'base' },
        [ordered]@{ category = 'agent'; file = 'agents'; pack = 'base' },
        [ordered]@{ category = 'music'; file = 'music'; pack = 'base' },
        [ordered]@{ category = 'pin'; file = 'collectibles'; pack = 'base' },
        [ordered]@{ category = 'keychain'; file = 'keychains'; pack = 'base' },
        [ordered]@{ category = 'sticker'; file = 'stickers'; pack = 'stickers' }
    )
    $packItems = @{ base = New-Object System.Collections.Generic.List[object]; stickers = New-Object System.Collections.Generic.List[object] }
    $packAssets = @{ base = @{}; stickers = @{} }
    $referencedPaths = @{}
    $missingPaths = New-Object System.Collections.Generic.List[string]
    $itemsWithoutImage = New-Object System.Collections.Generic.List[string]
    $metadataByPath = @{}

    foreach ($definition in $definitions) {
        $englishPath = Join-Path $dataRoot "$($definition.file)_en.json"
        $chinesePath = Join-Path $dataRoot "$($definition.file)_zh-CN.json"
        $englishItems = @(Read-WeaponPaintsJsonArray -Path $englishPath)
        $chineseItems = @(Read-WeaponPaintsJsonArray -Path $chinesePath)
        $mergedItems = @(Merge-WeaponPaintsCatalogItems -Category $definition.category -EnglishItems $englishItems -ChineseItems $chineseItems)

        foreach ($item in $mergedItems) {
            $manifestItem = [ordered]@{}
            foreach ($property in $item.PSObject.Properties) {
                if ($property.Name -ne 'imageSource') {
                    $manifestItem[$property.Name] = $property.Value
                }
            }

            $imageUrl = [string]$item.imageSource
            if ([string]::IsNullOrWhiteSpace($imageUrl)) {
                $manifestItem.image = $null
                $manifestItem.available = $false
                $manifestItem.missingReason = 'not-provided-by-upstream'
                $itemsWithoutImage.Add("$($item.category):$($item.key)")
                $packItems[$definition.pack].Add([pscustomobject]$manifestItem)
                continue
            }

            $relativeSourcePath = Get-WeaponPaintsImageSourcePath -ImageUrl $imageUrl
            $referencedPaths[$relativeSourcePath] = $true
            $sourceImagePath = Join-Path $sourceFull $relativeSourcePath.Replace('/', '\')
            if (-not (Test-Path -LiteralPath $sourceImagePath -PathType Leaf)) {
                $manifestItem.image = $null
                $manifestItem.available = $false
                $manifestItem.missingReason = 'missing-from-pinned-upstream'
                $missingPaths.Add($relativeSourcePath)
                $packItems[$definition.pack].Add([pscustomobject]$manifestItem)
                continue
            }

            if (-not $metadataByPath.ContainsKey($relativeSourcePath)) {
                $metadataByPath[$relativeSourcePath] = Get-WeaponPaintsImageMetadata -Path $sourceImagePath
            }
            $metadata = $metadataByPath[$relativeSourcePath]
            $fileName = [System.IO.Path]::GetFileName($sourceImagePath)
            $packagePath = "images/$fileName"
            $manifestItem.image = $packagePath
            $manifestItem.available = $true
            $manifestItem.bytes = $metadata.bytes
            $manifestItem.sha256 = $metadata.sha256
            $manifestItem.width = $metadata.width
            $manifestItem.height = $metadata.height
            $packItems[$definition.pack].Add([pscustomobject]$manifestItem)
            if (-not $packAssets[$definition.pack].ContainsKey($packagePath)) {
                $packAssets[$definition.pack][$packagePath] = [pscustomobject][ordered]@{
                    packagePath = $packagePath
                    sourcePath = $sourceImagePath
                    sourceRelativePath = $relativeSourcePath
                    bytes = $metadata.bytes
                    sha256 = $metadata.sha256
                }
            }
        }
    }

    $missing = @($missingPaths | Sort-Object -Unique)
    Test-WeaponPaintsMissingImages -MissingPaths $missing -KnownMissingPaths @($config.knownMissingImages)

    $treeFiles = @(Get-ChildItem -LiteralPath $imageRoot -File -Filter '*.png' | Sort-Object Name)
    $treeBytes = [int64](($treeFiles | Measure-Object Length -Sum).Sum)
    if ($treeFiles.Count -ne [int]$config.expectedImageTreeCount -or $treeBytes -ne [int64]$config.expectedImageTreeBytes) {
        throw "PINNED_IMAGE_TREE_MISMATCH: expected $($config.expectedImageTreeCount) files/$($config.expectedImageTreeBytes) bytes, got $($treeFiles.Count) files/$treeBytes bytes"
    }
    $treePaths = @($treeFiles | ForEach-Object { "website/img/skins/$($_.Name)" })
    $unused = @($treePaths | Where-Object { -not $referencedPaths.ContainsKey($_) } | Sort-Object)
    $duplicateGroups = @(
        $metadataByPath.GetEnumerator() |
            Group-Object { $_.Value.sha256 } |
            Where-Object { $_.Count -gt 1 } |
            Sort-Object Name |
            ForEach-Object {
                [pscustomobject][ordered]@{
                    sha256 = $_.Name
                    paths = @($_.Group | ForEach-Object { $_.Key } | Sort-Object)
                }
            }
    )

    $sourceMetadata = [ordered]@{
        repository = [string]$config.repository
        commit = [string]$config.commit
        commitTimestamp = [string]$config.commitTimestamp
    }
    $manifests = @{}
    foreach ($packName in @('base', 'stickers')) {
        $items = @($packItems[$packName] | Sort-Object category, key)
        $availableCount = @($items | Where-Object { $_.available }).Count
        $manifests[$packName] = [pscustomobject][ordered]@{
            schemaVersion = 1
            packVersion = [string]$config.packVersion
            pack = $packName
            source = [pscustomobject]$sourceMetadata
            counts = [pscustomobject][ordered]@{
                items = $items.Count
                available = $availableCount
                unavailable = $items.Count - $availableCount
                imageFiles = $packAssets[$packName].Count
            }
            items = $items
        }
    }

    $report = [pscustomobject][ordered]@{
        schemaVersion = 1
        packVersion = [string]$config.packVersion
        source = [pscustomobject]$sourceMetadata
        imageTree = [pscustomobject][ordered]@{ files = $treeFiles.Count; bytes = $treeBytes }
        referencedImages = $referencedPaths.Count
        availableImages = $metadataByPath.Count
        missingImages = $missing
        itemsWithoutImage = @($itemsWithoutImage | Sort-Object -Unique)
        unusedImages = $unused
        duplicateGroups = $duplicateGroups
    }
    $reportJson = ConvertTo-WeaponPaintsJson -Value $report
    Write-WeaponPaintsUtf8Text -Path $reportPath -Text $reportJson

    $timestamp = [System.DateTimeOffset]::Parse([string]$config.commitTimestamp, [System.Globalization.CultureInfo]::InvariantCulture)
    New-WeaponPaintsDeterministicZip -ZipPath $baseZip -PackRoot 'weaponpaints/base' -ManifestJson (ConvertTo-WeaponPaintsJson -Value $manifests.base) -Assets @($packAssets.base.Values) -Timestamp $timestamp
    New-WeaponPaintsDeterministicZip -ZipPath $stickersZip -PackRoot 'weaponpaints/stickers' -ManifestJson (ConvertTo-WeaponPaintsJson -Value $manifests.stickers) -Assets @($packAssets.stickers.Values) -Timestamp $timestamp

    foreach ($zipPath in @($baseZip, $stickersZip)) {
        $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-WeaponPaintsUtf8Text -Path "$zipPath.sha256" -Text "$zipHash  $([System.IO.Path]::GetFileName($zipPath))`n"
    }

    [pscustomobject][ordered]@{
        baseZip = $baseZip
        stickersZip = $stickersZip
        baseSha256 = "$baseZip.sha256"
        stickersSha256 = "$stickersZip.sha256"
        report = $reportPath
    }
}

function Test-WeaponPaintsMissingImages {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$MissingPaths,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$KnownMissingPaths
    )

    $known = @{}
    foreach ($path in $KnownMissingPaths) {
        $known[$path] = $true
    }

    $unknown = @($MissingPaths | Where-Object { -not $known.ContainsKey($_) } | Sort-Object -Unique)
    if ($unknown.Count -gt 0) {
        throw "UNKNOWN_MISSING_IMAGE: $($unknown -join ', ')"
    }

    $missingSet = @{}
    foreach ($path in $MissingPaths) {
        $missingSet[$path] = $true
    }
    $changed = @($KnownMissingPaths | Where-Object { -not $missingSet.ContainsKey($_) } | Sort-Object -Unique)
    if ($changed.Count -gt 0) {
        throw "KNOWN_MISSING_SET_CHANGED: $($changed -join ', ')"
    }
}

function Merge-WeaponPaintsCatalogItems {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('agent', 'glove', 'keychain', 'music', 'pin', 'skin', 'sticker')]
        [string]$Category,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$EnglishItems,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ChineseItems
    )

    $chineseByKey = @{}
    foreach ($item in $ChineseItems) {
        $key = if ($Category -eq 'agent') {
            "$($item.team):$($item.model)"
        } elseif ($Category -eq 'glove') {
            "$($item.weapon_defindex):$($item.paint)"
        } elseif ($Category -eq 'skin') {
            "$($item.weapon_name):$($item.paint)"
        } else {
            [string]$item.id
        }
        $chineseByKey[$key] = $item
    }

    foreach ($item in $EnglishItems) {
        if ($Category -eq 'agent') {
            $key = "$($item.team):$($item.model)"
            $englishName = [string]$item.agent_name
            $localizedName = $englishName
            if ($chineseByKey.ContainsKey($key) -and $chineseByKey[$key].agent_name) {
                $localizedName = [string]$chineseByKey[$key].agent_name
            }
            [pscustomobject][ordered]@{
                category = 'agent'
                key = $key
                id = 0
                name = [string]$localizedName
                englishName = [string]$item.agent_name
                imageSource = [string]$item.image
                team = [int]$item.team
                model = [string]$item.model
            }
            continue
        }

        if ($Category -in @('keychain', 'music', 'pin', 'sticker')) {
            $key = [string]$item.id
            $englishName = [string]$item.name
            $localizedName = $englishName
            if ($chineseByKey.ContainsKey($key) -and $chineseByKey[$key].name) {
                $localizedName = [string]$chineseByKey[$key].name
            }
            [pscustomobject][ordered]@{
                category = $Category
                key = $key
                id = [int]$item.id
                name = $localizedName
                englishName = $englishName
                imageSource = [string]$item.image
            }
            continue
        }

        $key = if ($Category -eq 'glove') {
            "$($item.weapon_defindex):$($item.paint)"
        } else {
            "$($item.weapon_name):$($item.paint)"
        }
        $englishName = [string]$item.paint_name
        $localizedName = $englishName
        if ($chineseByKey.ContainsKey($key) -and $chineseByKey[$key].paint_name) {
            $localizedName = [string]$chineseByKey[$key].paint_name
        }
        $result = [ordered]@{
            category = $Category
            key = $key
            id = [int]$item.paint
            name = $localizedName
            englishName = $englishName
            imageSource = [string]$item.image
            defIndex = [int]$item.weapon_defindex
        }
        if ($Category -eq 'skin') {
            $result.weaponKey = [string]$item.weapon_name
            $result.legacyModel = [bool]$item.legacy_model
        }
        [pscustomobject]$result
    }
}

Export-ModuleMember -Function @(
    'Get-WeaponPaintsImageMetadata',
    'Merge-WeaponPaintsCatalogItems',
    'New-WeaponPaintsAssetPackages',
    'Test-WeaponPaintsMissingImages'
)
