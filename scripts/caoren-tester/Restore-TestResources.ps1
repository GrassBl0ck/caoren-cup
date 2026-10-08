param([string]$Cs2Root,[switch]$NonInteractive)
. (Join-Path $PSScriptRoot 'TestResourceCommon.ps1')
Assert-TestGameStopped
$root = Get-TestGameRoot $Cs2Root -NonInteractive:$NonInteractive
$game = Join-Path $root 'game\csgo'
$info = Assert-TestPath (Join-Path $game 'gameinfo.gi') $root
$target = Assert-TestPath (Join-Path $game 'overrides\caoren_admin_menu.vpk') $root
$backupRoot = Assert-TestPath (Join-Path $game 'overrides\caoren-test-backups') $root
if (-not (Test-Path -LiteralPath $backupRoot -PathType Container)) { throw '没有此安装器创建的备份。' }
$candidates = @(Get-ChildItem -LiteralPath $backupRoot -Directory | Sort-Object Name -Descending)
$selected = $null
foreach ($candidate in $candidates) {
    $path = Assert-TestPath (Join-Path $candidate.FullName 'install-state.json') $root
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $state = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.Complete -and -not $state.Restored) { $selected = $candidate; break }
}
if (-not $selected) { throw '没有尚未恢复的完整备份。' }
if ([IO.Path]::GetFullPath($state.Cs2Root) -ne $root) { throw '备份记录与所选 CS2 目录不一致。' }
if ((Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash -ne $state.InstalledGameinfoHash -or
    (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $state.InstalledVpkHash) {
    throw '安装后文件又被其他操作修改，停止自动恢复；请检查备份后手动处理。'
}
$backup = Assert-TestPath $selected.FullName $root
if ((Get-FileHash -LiteralPath (Join-Path $backup 'gameinfo-before.gi') -Algorithm SHA256).Hash -ne $state.OriginalGameinfoHash) { throw 'gameinfo 备份校验失败。' }
if ($state.HadVpk -and (Get-FileHash -LiteralPath (Join-Path $backup 'vpk-before.vpk') -Algorithm SHA256).Hash -ne $state.OriginalVpkHash) { throw 'VPK 备份校验失败。' }
if (-not $state.HadVpk) {
    $retired = Assert-TestPath (Join-Path $backup 'installed-vpk-retired.vpk') $root
    if (Test-Path -LiteralPath $retired) { throw '恢复存档目标已存在。' }
}
Copy-Item -LiteralPath (Join-Path $backup 'gameinfo-before.gi') -Destination $info -Force
if ($state.HadVpk) { Copy-Item -LiteralPath (Join-Path $backup 'vpk-before.vpk') -Destination $target -Force }
else {
    Move-Item -LiteralPath $target -Destination $retired
}
$state.Restored = $true
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'install-state.json') -Encoding UTF8
Write-Host '已恢复安装前的文件，测试资源保存在备份目录中。'
