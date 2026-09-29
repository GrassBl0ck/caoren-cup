# 菜单＋音频的单 Addon 构建。只生成新产物；不覆盖安装资源，不修改服务器，不发布工坊。
param(
    [Parameter(Mandatory)][string]$Cs2Root,
    [Parameter(Mandatory)][string]$VpkEditCli,
    [string]$Catalog,
    [string]$SourceDirectory,
    [string]$Ffmpeg,
    [string]$Ffprobe,
    [string]$Python = 'python',
    [string]$OutputDirectory,
    [string]$AdditionalSoundEvents,
    [switch]$AllowEmptyAudio
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resources = Join-Path $repo 'game-plugin\Features\InGameMenu\resources'
if (-not $Catalog) { $Catalog = Join-Path $repo 'game-plugin\Features\InGameMenu\audio-events.json' }
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $repo 'game-plugin\Features\InGameMenu\audio-source' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo ('release-build\ingamemenu-audio\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fffffff')) }
function Assert-InDirectory([string]$Path, [string]$Root) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) { throw "路径不在目标目录内：$absolute" }
    return $absolute
}
$Catalog = Assert-InDirectory $Catalog $repo
$SourceDirectory = Assert-InDirectory $SourceDirectory $repo
$OutputDirectory = Assert-InDirectory $OutputDirectory $repo
if ($AdditionalSoundEvents) { $AdditionalSoundEvents = Assert-InDirectory $AdditionalSoundEvents $repo }
$Cs2Root = [IO.Path]::GetFullPath($Cs2Root)
$compiler = Join-Path $Cs2Root 'game\bin\win64\resourcecompiler.exe'
foreach ($tool in @($compiler,$VpkEditCli)) { if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "缺少工具：$tool" } }
if (-not (Test-Path -LiteralPath $Catalog -PathType Leaf)) { throw "缺少已确认的登记清单：$Catalog" }
if (Test-Path -LiteralPath $OutputDirectory) { throw '输出目录已存在，停止以免覆盖上一份产物。' }
$entries = @(Get-Content -LiteralPath $Catalog -Raw -Encoding utf8 | ConvertFrom-Json)
$sourceEntries = @($entries | Where-Object { $_.SourceFile })
foreach ($entry in $entries) {
    if ($entry.Id -notmatch '^[a-z0-9]+([._-][a-z0-9]+)*$' -or $entry.Id -match '^(vote|bridge|gameplay)\.') { throw "无效或保留事件 ID：$($entry.Id)" }
    if ($null -eq $entry.DefaultVolume -or -not [double]::IsFinite([double]$entry.DefaultVolume) -or [double]$entry.DefaultVolume -lt 0 -or [double]$entry.DefaultVolume -gt 1) { throw "无效默认音量：$($entry.Id)" }
    if ($entry.Channel -cnotin @('Effect','Music','Broadcast') -or $entry.Loop -isnot [bool]) { throw "无效通道或循环设置：$($entry.Id)" }
    if ($entry.SourceFile -and [IO.Path]::GetExtension($entry.SourceFile) -ine '.ogg') { throw "源素材必须是 OGG：$($entry.SourceFile)" }
}
if (@($entries.Id | Select-Object -Unique).Count -ne $entries.Count) { throw '音频事件 ID 重复。' }
if ($sourceEntries.Count -eq 0 -and -not $AllowEmptyAudio) { throw '没有音频素材；仅验证菜单时需显式传 -AllowEmptyAudio，不能当作音频构建通过。' }
if ($sourceEntries.Count -gt 0) {
    foreach ($tool in @($Ffmpeg,$Ffprobe)) { if (-not $tool -or -not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw '音频构建需要现有 ffmpeg 和 ffprobe 的绝对路径。' } }
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$addon = 'caoren_resources_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fffffff')
$content = Assert-InDirectory (Join-Path $Cs2Root "content\csgo_addons\$addon") (Join-Path $Cs2Root 'content\csgo_addons')
$compiled = Assert-InDirectory (Join-Path $Cs2Root "game\csgo_addons\$addon") (Join-Path $Cs2Root 'game\csgo_addons')
$stage = Join-Path $OutputDirectory 'vpk_stage'
$vpk = Join-Path $OutputDirectory 'caoren_resources.vpk'
$report = [ordered]@{ Success = $false; AudioInputs = $sourceEntries.Count; Addon = $addon; Content = $content; Compiled = $compiled; Vpk = $vpk; RuntimeCatalog = ''; Errors = @(); GameValidated = $false }
try {
    if ((Test-Path -LiteralPath $content) -or (Test-Path -LiteralPath $compiled)) { throw '临时 Addon 已存在，停止以免覆盖。' }
    $scanOutput = Join-Path $OutputDirectory 'scan'
    $scanArgs = @((Join-Path $PSScriptRoot 'scan-caoren-audio.py'),'--source',$SourceDirectory,'--catalog',$Catalog,'--output',$scanOutput)
    if ($Ffmpeg) { $scanArgs += @('--ffmpeg',$Ffmpeg) }
    if ($Ffprobe) { $scanArgs += @('--ffprobe',$Ffprobe) }
    & $Python @scanArgs
    if ($LASTEXITCODE -ne 0) { throw '音频内容/解码校验失败；检查 scan-report.json，原清单未覆盖。' }
    $scanReport = Get-Content -LiteralPath (Join-Path $scanOutput 'scan-report.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if (-not $scanReport.Success) { throw '扫描报告未通过。' }
    $scannedDraft = @(Get-Content -LiteralPath (Join-Path $scanOutput 'audio-events.draft.json') -Raw -Encoding utf8 | ConvertFrom-Json)
    if ($scannedDraft.Count -ne $entries.Count) { throw '存在未登记素材，请先确认扫描草稿，再单独构建。' }
    $bankOutput = Join-Path $OutputDirectory 'bank'
    $bankArgs = @((Join-Path $PSScriptRoot 'generate-caoren-audio-bank.py'),'--catalog',$Catalog,'--scan',(Join-Path $scanOutput 'scan-report.json'),'--output',$bankOutput)
    if ($AdditionalSoundEvents) { $bankArgs += @('--additional',$AdditionalSoundEvents) }
    & $Python @bankArgs
    if ($LASTEXITCODE -ne 0) { throw '自动声音事件登记失败。' }
    New-Item -ItemType Directory -Path $content | Out-Null
    Copy-Item -LiteralPath (Join-Path $resources 'panorama') -Destination $content -Recurse
    Copy-Item -LiteralPath (Join-Path $resources 'addoninfo.txt') -Destination $content
    $compilerArgs = @('-game',(Join-Path $Cs2Root 'game\csgo'))
    $expected = @()
    foreach ($name in @('caoren_admin_menu','caoren_player_menu','caoren_vote')) {
        foreach ($part in @(@{Dir='layout';Ext='vxml'},@{Dir='styles';Ext='vcss'})) {
            $relative = "panorama\$($part.Dir)\custom_game\$name.$($part.Ext)"
            $compilerArgs += @('-i',(Join-Path $content $relative))
            $expected += ($relative + '_c')
        }
    }
    $runtime = @()
    foreach ($entry in $entries) {
        if ($entry.Id -notmatch '^[a-z0-9]+([._-][a-z0-9]+)*$') { throw "无效事件 ID：$($entry.Id)" }
        if ($entry.SourceFile) {
            $inputAudio = Assert-InDirectory (Join-Path $SourceDirectory $entry.SourceFile) $SourceDirectory
            if (-not (Test-Path -LiteralPath $inputAudio -PathType Leaf)) { throw "音频源文件缺失：$inputAudio" }
            $compiledRelative = "sounds/caorencup/$($entry.Id.Replace('.','_')).vsnd_c"
            $wavRelative = "sounds\caorencup\$($entry.Id.Replace('.','_')).wav"
            $wav = Join-Path $content $wavRelative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $wav) | Out-Null
            $decodedWav = if ($entry.Loop) { $wav + '.decoded.wav' } else { $wav }
            & $Ffmpeg -v error -xerror -i $inputAudio -map 0:a:0 -ar 48000 -ac 2 -c:a pcm_s16le -n $decodedWav
            if ($LASTEXITCODE -ne 0) { throw "OGG 转换失败：$($entry.SourceFile)" }
            if ($entry.Loop) {
                & $Python (Join-Path $PSScriptRoot 'mark-caoren-audio-loop.py') --input $decodedWav --output $wav
                if ($LASTEXITCODE -ne 0) { throw "内部整段循环标记失败：$($entry.Id)" }
            }
            $compilerArgs += @('-i',$wav)
            $expected += $compiledRelative.Replace('/','\')
            # 同一 OGG 生成两种内部资源；运行中开启循环可以从当前位置切换。
            $alternateWav = if ($entry.Loop) { $wav.Replace('.wav','_once.wav') } else { $wav.Replace('.wav','_loop.wav') }
            if ($entry.Loop) { Copy-Item -LiteralPath $decodedWav -Destination $alternateWav }
            else {
                & $Python (Join-Path $PSScriptRoot 'mark-caoren-audio-loop.py') --input $wav --output $alternateWav
                if ($LASTEXITCODE -ne 0) { throw "循环副本生成失败：$($entry.Id)" }
            }
            $compilerArgs += @('-i',$alternateWav)
            $expected += ("sounds\caorencup\" + [IO.Path]::GetFileNameWithoutExtension($alternateWav) + '.vsnd_c')
            $runtime += [ordered]@{ Id=$entry.Id; DisplayName=$entry.DisplayName; Source=$compiledRelative; NativeEvent=$false; DefaultVolume=$entry.DefaultVolume; Channel=$entry.Channel; Loop=$entry.Loop }
        }
        else { $runtime += $entry }
    }
    if (@($runtime.Id | Select-Object -Unique).Count -ne $runtime.Count) { throw '音频事件 ID 重复。' }
    $compilerArgs += @('-f','-nop4','-v')
    if (Test-Path -LiteralPath (Join-Path $bankOutput 'soundevents_addon.vsndevts')) {
        $eventsRelative = 'soundevents\soundevents_addon.vsndevts'
        New-Item -ItemType Directory -Path (Join-Path $content 'soundevents') | Out-Null
        Copy-Item -LiteralPath (Join-Path $bankOutput 'soundevents_addon.vsndevts') -Destination (Join-Path $content $eventsRelative)
        $compilerArgs += @('-i',(Join-Path $content $eventsRelative))
        $expected += ($eventsRelative + '_c')
    }
    & $compiler @compilerArgs *> (Join-Path $OutputDirectory 'resourcecompiler.log')
    if ($LASTEXITCODE -ne 0) { throw 'resourcecompiler 失败；检查日志中的具体文件，上一份产物未覆盖。' }
    foreach ($relative in $expected) {
        $file = Join-Path $compiled $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "编译产物缺失：$relative" }
        $destination = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $resources 'addoninfo.txt') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $resources 'panorama\preprocessor_config.txt') -Destination (Join-Path $stage 'panorama')
    & $VpkEditCli --output $vpk --type vpk --version 2 --single-file $stage
    if ($LASTEXITCODE -ne 0) { throw 'VPK 打包失败。' }
    & $VpkEditCli --verify-checksums files $vpk
    if ($LASTEXITCODE -ne 0) { throw 'VPK 文件校验失败。' }
    $runtimeCatalog = Join-Path $OutputDirectory 'audio-events.json'
    Copy-Item -LiteralPath (Join-Path $bankOutput 'audio-events.json') -Destination $runtimeCatalog
    Copy-Item -LiteralPath (Join-Path $bankOutput 'audio-assets.json') -Destination (Join-Path $OutputDirectory 'audio-assets.json')
    $report.RuntimeCatalog = $runtimeCatalog
    $report.Success = $true
    $report.Sha256 = (Get-FileHash -LiteralPath $vpk -Algorithm SHA256).Hash
    Write-Output "已生成未部署的独立资源包：$vpk"
} catch {
    $report.Errors += $_.Exception.Message
    throw
} finally {
    ConvertTo-Json -InputObject $report -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-report.json') -Encoding utf8NoBOM
    # 保留临时 Addon 和失败产物，不执行递归删除，不覆盖任何既有产物。
}
