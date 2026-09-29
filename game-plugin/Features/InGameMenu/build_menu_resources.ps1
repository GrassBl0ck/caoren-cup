# 草人杯管理员菜单 Panorama 资源编译打包脚本
# 历史单菜单资源编译流程；当前完整菜单与音频包请使用 scripts/build-caoren-menu-audio.ps1。
#   resources 源文件 -> 本机 CS2 content 树 -> resourcecompiler 编译 -> VPKEdit 打包 -> dist/caoren_admin_menu.vpk
#
# 用法:
#   powershell -ExecutionPolicy Bypass -File build_menu_resources.ps1 [-Cs2Root <CS2目录>] [-VpkEditCli <vpkeditcli路径>] [-SkipClean]
# 默认值可用环境变量 CS2_ROOT / VPKEDIT_CLI 覆盖。
# 注意:会在本机 CS2 安装目录下创建临时 csgo_addons/caoren_admin_menu(默认结束后清理)。

param(
    [string]$Cs2Root = $env:CS2_ROOT,
    [string]$VpkEditCli = $env:VPKEDIT_CLI,
    [switch]$SkipClean
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$resourcesDir = Join-Path $scriptDir 'resources'
$distDir = Join-Path $scriptDir 'dist'
$stageDir = Join-Path $scriptDir 'vpk_stage'

if ([string]::IsNullOrWhiteSpace($Cs2Root)) {
    # 常见默认位置,逐个探测
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive",
        "D:\Steam\steamapps\common\Counter-Strike Global Offensive",
        "E:\Steam\steamapps\common\Counter-Strike Global Offensive"
    )
    $Cs2Root = $candidates | Where-Object { Test-Path (Join-Path $_ 'game\bin\win64\resourcecompiler.exe') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($Cs2Root) -or -not (Test-Path (Join-Path $Cs2Root 'game\bin\win64\resourcecompiler.exe'))) {
    throw "未找到本机 CS2(缺 game\bin\win64\resourcecompiler.exe)。请用 -Cs2Root 指定或设置环境变量 CS2_ROOT。"
}

if ([string]::IsNullOrWhiteSpace($VpkEditCli)) {
    $cmd = Get-Command vpkeditcli -ErrorAction SilentlyContinue
    if ($cmd) { $VpkEditCli = $cmd.Source }
}
if ([string]::IsNullOrWhiteSpace($VpkEditCli) -or -not (Test-Path $VpkEditCli)) {
    throw "未找到 vpkeditcli。请用 -VpkEditCli 指定或设置环境变量 VPKEDIT_CLI。"
}

$compiler = Join-Path $Cs2Root 'game\bin\win64\resourcecompiler.exe'
$addonName = 'caoren_admin_menu'
$contentDir = Join-Path $Cs2Root "content\csgo_addons\$addonName"
$gameAddonDir = Join-Path $Cs2Root "game\csgo_addons\$addonName"

Write-Host "== 1/5 准备 content 树: $contentDir"
New-Item -ItemType Directory -Force -Path $contentDir | Out-Null
Copy-Item -Path (Join-Path $resourcesDir '*') -Destination $contentDir -Recurse -Force

Write-Host "== 2/5 resourcecompiler 编译 vxml/vcss"
& $compiler `
    -game (Join-Path $Cs2Root 'game\csgo') `
    -i (Join-Path $contentDir 'panorama\layout\custom_game\caoren_admin_menu.vxml') `
    -i (Join-Path $contentDir 'panorama\styles\custom_game\caoren_admin_menu.vcss') `
    -f -nop4 -v
if ($LASTEXITCODE -ne 0) { throw "resourcecompiler 失败,退出码 $LASTEXITCODE" }

$compiledLayout = Join-Path $gameAddonDir 'panorama\layout\custom_game\caoren_admin_menu.vxml_c'
$compiledStyle = Join-Path $gameAddonDir 'panorama\styles\custom_game\caoren_admin_menu.vcss_c'
foreach ($f in @($compiledLayout, $compiledStyle)) {
    if (-not (Test-Path $f)) { throw "编译产物缺失: $f" }
}

Write-Host "== 3/5 组装 VPK 暂存目录"
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force -Path "$stageDir\panorama\layout\custom_game" | Out-Null
New-Item -ItemType Directory -Force -Path "$stageDir\panorama\styles\custom_game" | Out-Null
Copy-Item (Join-Path $resourcesDir 'addoninfo.txt') $stageDir
Copy-Item $compiledLayout "$stageDir\panorama\layout\custom_game"
Copy-Item $compiledStyle "$stageDir\panorama\styles\custom_game"
Copy-Item (Join-Path $resourcesDir 'panorama\preprocessor_config.txt') "$stageDir\panorama"

Write-Host "== 4/5 打包 VPK"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$vpkOut = Join-Path $distDir "$addonName.vpk"
if (Test-Path $vpkOut) { Remove-Item -Force $vpkOut }
& $VpkEditCli --output $vpkOut --type vpk --version 2 --single-file $stageDir
if ($LASTEXITCODE -ne 0) { throw "vpkeditcli 失败,退出码 $LASTEXITCODE" }
Write-Host "已生成: $vpkOut"

if (-not $SkipClean) {
    Write-Host "== 5/5 清理本机 content/game 临时产物"
    Remove-Item -Recurse -Force $contentDir
    Remove-Item -Recurse -Force $gameAddonDir
} else {
    Write-Host "== 5/5 跳过清理(-SkipClean)"
}

Write-Host "完成。部署形态(管理员手动方案,参照 probe README 第四节):"
Write-Host "  服务器/客户端: game/csgo/overrides/$addonName.vpk"
Write-Host "  并在 gameinfo.gi 的 SearchPaths 'Game csgo' 行后加:  Game        csgo/overrides/$addonName.vpk"
