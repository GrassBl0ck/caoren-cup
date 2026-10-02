param([string]$Cs2Root,[switch]$NonInteractive)
. (Join-Path $PSScriptRoot 'TestResourceCommon.ps1')
Assert-TestGameStopped
$root = Get-TestGameRoot $Cs2Root -NonInteractive:$NonInteractive
$game = Join-Path $root 'game\csgo'
$info = Assert-TestPath (Join-Path $game 'gameinfo.gi') $root
$target = Assert-TestPath (Join-Path $game 'overrides\caoren_admin_menu.vpk') $root
$source = Join-Path $PSScriptRoot 'caoren_admin_menu.vpk'
$version = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($version.Sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw '测试包校验信息无效。' }
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $version.Sha256) { throw '测试包 VPK 校验失败，请重新解压完整测试包。' }
$text = [IO.File]::ReadAllText($info, (New-Object Text.UTF8Encoding($false,$true)))
$updated = Add-TestMountLine $text
$hadVpk = Test-Path -LiteralPath $target -PathType Leaf
if ($hadVpk -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $version.Sha256 -and $updated -ceq $text) {
    Write-Host '已安装同一版本，无需重复部署。'; exit 0
}
$backup = Assert-TestPath (Join-Path $game ('overrides\caoren-test-backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'))) $root
if (Test-Path -LiteralPath $backup) { throw '备份目录已存在，停止覆盖。' }
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $info -Destination (Join-Path $backup 'gameinfo-before.gi')
if ($hadVpk) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'vpk-before.vpk') }
$state = [ordered]@{Cs2Root=$root;HadVpk=$hadVpk;OriginalGameinfoHash=(Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash;
    OriginalVpkHash=$(if ($hadVpk) { (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash } else { '' });
    InstalledVpkHash=$version.Sha256;InstalledGameinfoHash='';Complete=$false;Restored=$false}
$statePath = Join-Path $backup 'install-state.json'
$state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ($updated -cne $text) { [IO.File]::WriteAllText($info,$updated,(New-Object Text.UTF8Encoding($false))) }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $version.Sha256) { throw '部署后 VPK 校验失败。' }
    $state.InstalledGameinfoHash = (Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash
    $state.Complete = $true
    $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    Write-Host '部署完成，可以重新启动 CS2。'
    Write-Host "备份目录：$backup"
} catch {
    Copy-Item -LiteralPath (Join-Path $backup 'gameinfo-before.gi') -Destination $info -Force
    if ($hadVpk) { Copy-Item -LiteralPath (Join-Path $backup 'vpk-before.vpk') -Destination $target -Force }
    elseif (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination (Join-Path $backup 'incomplete-install.vpk') }
    throw
}
