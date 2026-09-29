# 仅生成本地检查包，不上传、不覆盖服务器文件。
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$splitDir = [IO.Path]::GetFullPath($PSScriptRoot)
$gameDir = [IO.Path]::GetFullPath((Join-Path $splitDir '..'))
$solution = Join-Path $gameDir 'CaorenCup.sln'
$outputRoot = Join-Path $splitDir ('bin\local-package-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

if (-not $outputRoot.StartsWith($splitDir + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package path outside PluginSplit'
}
if (Test-Path -LiteralPath $outputRoot) { throw 'Package target already exists' }

& dotnet build $solution -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed' }

$base = Join-Path $outputRoot 'addons\counterstrikesharp'
$group = Join-Path $base 'plugins\CaorenCup'
$projects = @(
    @{ Name = 'CaorenCupCore'; Source = 'PluginSplit\CaorenCupCore'; Subdir = 'CaorenCupCore' },
    @{ Name = 'CaorenCupFunCommands'; Source = '.'; Subdir = 'CaorenCupFunCommands' },
    @{ Name = 'CaorenCupInGameMenu'; Source = 'PluginSplit\CaorenCupInGameMenu'; Subdir = 'CaorenCupInGameMenu' },
    @{ Name = 'CaorenCupGamemode_Competitive'; Source = 'PluginSplit\CaorenCupGamemode_Competitive'; Subdir = 'CaorenCupGamemodes\CaorenCupGamemode_Competitive' },
    @{ Name = 'CaorenCupGamemode_RUSH'; Source = 'PluginSplit\CaorenCupGamemode_RUSH'; Subdir = 'CaorenCupGamemodes\CaorenCupGamemode_RUSH' },
    @{ Name = 'CaorenCupQOL_Alias'; Source = 'PluginSplit\CaorenCupQOL_Alias'; Subdir = 'CaorenCupQOLs\CaorenCupQOL_Alias' },
    @{ Name = 'CaorenCupQOL_PlaySound'; Source = 'PluginSplit\CaorenCupQOL_PlaySound'; Subdir = 'CaorenCupQOLs\CaorenCupQOL_PlaySound' },
    @{ Name = 'CaorenCupQOL_RadarColor'; Source = 'PluginSplit\CaorenCupQOL_RadarColor'; Subdir = 'CaorenCupQOLs\CaorenCupQOL_RadarColor' },
    @{ Name = 'CaorenCupQOL_SimpleHP'; Source = 'PluginSplit\CaorenCupQOL_SimpleHP'; Subdir = 'CaorenCupQOLs\CaorenCupQOL_SimpleHP' }
)

foreach ($entry in $projects) {
    $source = Join-Path (Join-Path $gameDir $entry.Source) "bin\$Configuration\net10.0"
    $dest = Join-Path $group $entry.Subdir
    if (Test-Path -LiteralPath $dest) { throw "Unexpected package destination: $dest" }
    New-Item -ItemType Directory -Path $dest | Out-Null
    foreach ($extension in @('.dll', '.deps.json', '.pdb')) {
        $name = $entry.Name + $extension
        $from = Join-Path $source $name
        if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { throw "Missing build output: $from" }
        Copy-Item -LiteralPath $from -Destination (Join-Path $dest $name)
    }
}

# 换肤插件保留独立的 net8.0 项目及依赖，发布到与源码对应的 QOL 目录。
$weaponPaintsSource = Join-Path $splitDir 'CaorenCupQOLs\CaorenWeaponPaints'
$weaponPaintsProject = Join-Path $weaponPaintsSource 'CaorenWeaponPaints.csproj'
$weaponPaintsDest = Join-Path $group 'CaorenCupQOLs\CaorenWeaponPaints'
if (Test-Path -LiteralPath $weaponPaintsDest) { throw "Unexpected package destination: $weaponPaintsDest" }
& dotnet publish $weaponPaintsProject -c $Configuration -o $weaponPaintsDest
if ($LASTEXITCODE -ne 0) { throw 'WeaponPaints publish failed' }
if (-not (Test-Path -LiteralPath (Join-Path $weaponPaintsDest 'CaorenWeaponPaints.dll') -PathType Leaf)) {
    throw 'WeaponPaints primary DLL missing from package'
}
if (-not (Test-Path -LiteralPath (Join-Path $weaponPaintsDest 'data') -PathType Container)) {
    throw 'WeaponPaints item data missing from package'
}
$globalGamedataDest = Join-Path $base 'gamedata'
New-Item -ItemType Directory -Path $globalGamedataDest | Out-Null
Copy-Item -LiteralPath (Join-Path $weaponPaintsSource 'gamedata\weaponpaints.json') -Destination (Join-Path $globalGamedataDest 'weaponpaints.json')

$contractSource = Join-Path $splitDir "CaorenCupContracts\bin\$Configuration\net10.0\CaorenCupContracts.dll"
$contractDest = Join-Path $base 'shared\CaorenCupContracts'
New-Item -ItemType Directory -Path $contractDest | Out-Null
Copy-Item -LiteralPath $contractSource -Destination (Join-Path $contractDest 'CaorenCupContracts.dll')

# 新安装需要的预设库；已有服务器数据须单独备份并保留，不能用此文件直接覆盖。
$presetDest = Join-Path $group 'module-configs'
New-Item -ItemType Directory -Path $presetDest | Out-Null
Copy-Item -LiteralPath (Join-Path $gameDir 'module-configs\presets.grass.json') -Destination (Join-Path $presetDest 'presets.grass.json')

$menuResources = Join-Path $gameDir 'Features\InGameMenu\resources'
if (Test-Path -LiteralPath $menuResources -PathType Container) {
    $menuDest = Join-Path $group 'CaorenCupInGameMenu\resources'
    Copy-Item -LiteralPath $menuResources -Destination $menuDest -Recurse
}

$pluginDlls = @(Get-ChildItem -LiteralPath (Join-Path $base 'plugins') -Recurse -File -Filter '*.dll')
$weaponPaintsDlls = @($pluginDlls | Where-Object {
    $_.FullName.StartsWith($weaponPaintsDest + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
})
$otherPluginDlls = @($pluginDlls | Where-Object {
    -not $_.FullName.StartsWith($weaponPaintsDest + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
})
$mismatched = @($otherPluginDlls | Where-Object { $_.BaseName -cne $_.Directory.Name })
if ($otherPluginDlls.Count -ne $projects.Count -or $mismatched.Count -ne 0 -or
    @($weaponPaintsDlls | Where-Object { $_.Name -ceq 'CaorenWeaponPaints.dll' }).Count -ne 1) {
    throw "Plugin directory validation failed: primary=$($otherPluginDlls.Count), weaponpaints=$($weaponPaintsDlls.Count), mismatched=$($mismatched.Count)"
}
$contractCopies = @(Get-ChildItem -LiteralPath $base -Recurse -File -Filter 'CaorenCupContracts.dll')
if ($contractCopies.Count -ne 1) { throw "Expected one shared contract DLL; found $($contractCopies.Count)" }

Write-Output "LOCAL_PACKAGE=$outputRoot"
Write-Output "PLUGIN_DLLS=$($projects.Count + 1)"
Write-Output 'This package has not been loaded by CS2 and must not be deployed as verified.'
