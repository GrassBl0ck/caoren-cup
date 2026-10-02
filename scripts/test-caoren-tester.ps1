$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $repo ('release-build\ingamemenu-audio\installer-tests-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$package=Join-Path $fixture 'package'; New-Item -ItemType Directory -Path $package | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'caoren-tester\*') -Destination $package
$vpk=Join-Path $package 'caoren_admin_menu.vpk'; [IO.File]::WriteAllBytes($vpk,[Text.Encoding]::UTF8.GetBytes('installer-fixture-vpk'))
@{Sha256=(Get-FileHash -LiteralPath $vpk -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'version.json') -Encoding UTF8
. (Join-Path $package 'TestResourceCommon.ps1')
$runner=Join-Path $fixture 'run-fixture.ps1'
$runnerText=@'
param([string]$Script,[string]$GameRoot)
$ErrorActionPreference='Stop'
$allowed=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')+'\'
$actual=[IO.Path]::GetFullPath($GameRoot)
if (-not $actual.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture path escaped' }
if ((Get-Item -LiteralPath (Join-Path $actual 'game\bin\win64\cs2.exe')).Length -ne 0) { throw 'Not a synthetic fixture' }
function Get-Process { [CmdletBinding()]param([string]$Name) return $null }
& $Script -Cs2Root $actual -NonInteractive
'@
[IO.File]::WriteAllText($runner,$runnerText,(New-Object Text.UTF8Encoding($true)))
$completed=0
foreach ($hadVpk in @($false,$true)) {
    $root=Join-Path $fixture ('game-'+[string]$hadVpk)
    $game=Join-Path $root 'game\csgo'; New-Item -ItemType Directory -Path $game | Out-Null
    $bin=Join-Path $root 'game\bin\win64'; New-Item -ItemType Directory -Path $bin | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $bin 'cs2.exe'),[byte[]]@())
    $info=Join-Path $game 'gameinfo.gi'
    $text="GameInfo`r`n{`r`n // a comment containing { }`r`n FileSystem`r`n {`r`n  SearchPaths`r`n  {`r`n   Game csgo`r`n   Game core`r`n  }`r`n }`r`n}`r`n"
    [IO.File]::WriteAllText($info,$text,(New-Object Text.UTF8Encoding($false)))
    $originalInfo=(Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash
    $target=Join-Path $game 'overrides\caoren_admin_menu.vpk'
    if ($hadVpk) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) | Out-Null
        [IO.File]::WriteAllBytes($target,[Text.Encoding]::UTF8.GetBytes('previous-vpk'))
        $originalVpk=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner (Join-Path $package 'Install-TestResources.ps1') $root
    if ($LASTEXITCODE) { throw 'Installer fixture failed' }
    $after=[IO.File]::ReadAllText($info)
    if ([regex]::Matches($after,'csgo/overrides/caoren_admin_menu.vpk').Count -ne 1) { throw 'Mount count wrong' }
    if (-not $after.Contains('Game core')) { throw 'Unrelated path lost' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner (Join-Path $package 'Install-TestResources.ps1') $root
    if ($LASTEXITCODE) { throw 'Repeat install failed' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $game 'overrides\caoren-test-backups') -Directory).Count -ne 1) { throw 'Repeat installer created extra backup' }
    if ($hadVpk) {
        [IO.File]::AppendAllText($info,"// later external edit`r`n")
        $changed=(Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner (Join-Path $package 'Restore-TestResources.ps1') $root 2> (Join-Path $fixture 'expected-restore-refusal.txt')
        if ($LASTEXITCODE -eq 0 -or (Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash -ne $changed) { throw 'Restore failed to protect external edit' }
        [IO.File]::WriteAllText($info,$after,(New-Object Text.UTF8Encoding($false)))
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner (Join-Path $package 'Restore-TestResources.ps1') $root
    if ($LASTEXITCODE) { throw 'Restore fixture failed' }
    if ((Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash -ne $originalInfo) { throw 'gameinfo restoration mismatch' }
    if ($hadVpk) { if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $originalVpk) { throw 'VPK restoration mismatch' } }
    elseif (Test-Path -LiteralPath $target) { throw 'New VPK still mounted at original target' }
    $completed++
}
$wrong="GameInfo`n{`nOther`n{`nGame csgo/overrides/caoren_admin_menu.vpk`n}`nSearchPaths`n{`nGame csgo`nGame core`n}`n}`n"
$fixed=Add-TestMountLine $wrong
if ([regex]::Matches($fixed,'csgo/overrides/caoren_admin_menu.vpk').Count -ne 2) { throw 'Matched unrelated section' }
if ((Add-TestMountLine $fixed) -cne $fixed) { throw 'Mount insertion not idempotent' }
try { Assert-TestPath (Join-Path $fixture 'outside') (Join-Path $fixture 'game-True'); throw 'Path guard failed' }
catch { if ($_.Exception.Message -eq 'Path guard failed') { throw } }
Write-Output "Installer fixture checks passed: fresh install, upgrade, repeat install, restore, external edit protection, section scope and path guard. Fixtures=$fixture"
