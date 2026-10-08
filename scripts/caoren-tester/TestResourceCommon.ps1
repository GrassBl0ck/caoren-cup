Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TestGameRoot([string]$RequestedRoot, [switch]$NonInteractive) {
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if ($RequestedRoot) { $candidates.Add($RequestedRoot) }
    else {
        $steamRoots = @()
        foreach ($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
            $value = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
            if ($value) {
                foreach ($name in @('SteamPath','InstallPath')) {
                    $property = $value.PSObject.Properties[$name]
                    if ($property -and $property.Value) { $steamRoots += [string]$property.Value }
                }
            }
        }
        $steamRoots += @('C:\Program Files (x86)\Steam','D:\Steam','E:\Steam')
        foreach ($steamRoot in @($steamRoots | Select-Object -Unique)) {
            $libraries = @($steamRoot)
            $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
                $text = [IO.File]::ReadAllText($libraryFile)
                foreach ($match in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                    $libraries += $match.Groups[1].Value.Replace('\\','\')
                }
            }
            foreach ($library in $libraries) { $candidates.Add((Join-Path $library 'steamapps\common\Counter-Strike Global Offensive')) }
        }
    }
    $valid = @($candidates | Select-Object -Unique | Where-Object {
        (Test-Path -LiteralPath (Join-Path $_ 'game\csgo\gameinfo.gi') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $_ 'game\bin\win64\cs2.exe') -PathType Leaf)
    })
    if ($valid.Count -eq 1) { return [IO.Path]::GetFullPath($valid[0]) }
    if ($NonInteractive) { throw '未找到唯一的 CS2 安装目录，请使用 -Cs2Root 指定。' }
    Write-Host '请输入 CS2 安装目录（包含 game 文件夹的 Counter-Strike Global Offensive 目录）：'
    $selected = Read-Host
    return Get-TestGameRoot -RequestedRoot $selected -NonInteractive
}

function Assert-TestPath([string]$Path, [string]$Root) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase)) { throw "路径超出 CS2 目标目录：$absolute" }
    $existing = $absolute
    while ($existing -and $existing.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $existing) {
            $item = Get-Item -LiteralPath $existing -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "目标包含链接或重解析点，请改用实际目录：$existing" }
        }
        $existing = Split-Path -Parent $existing
    }
    return $absolute
}

function Add-TestMountLine([string]$Text) {
    $header = [regex]::Match($Text,'(?im)^[ \t]*"?SearchPaths"?[ \t]*(?:(?://[^\r\n]*)?\r?\n[ \t]*)?\{')
    if (-not $header.Success) { throw '未找到 SearchPaths 区块，停止修改 gameinfo.gi。' }
    $opening = $header.Index + $header.Length - 1
    $depth = 1; $quoted = $false; $lineComment = $false; $blockComment = $false; $closing = -1
    for ($index = $opening + 1; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        $next = if ($index + 1 -lt $Text.Length) { $Text[$index+1] } else { [char]0 }
        if ($lineComment) { if ($character -eq "`n") { $lineComment = $false }; continue }
        if ($blockComment) { if ($character -eq '*' -and $next -eq '/') { $blockComment = $false; $index++ }; continue }
        if ($quoted) {
            if ($character -eq '\') { $index++; continue }
            if ($character -eq '"') { $quoted = $false }
            continue
        }
        if ($character -eq '/' -and $next -eq '/') { $lineComment = $true; $index++; continue }
        if ($character -eq '/' -and $next -eq '*') { $blockComment = $true; $index++; continue }
        if ($character -eq '"') { $quoted = $true; continue }
        if ($character -eq '{') { $depth++ }
        if ($character -eq '}') { $depth--; if ($depth -eq 0) { $closing = $index; break } }
    }
    if ($closing -lt 0) { throw 'SearchPaths 大括号不完整，停止修改。' }
    $body = $Text.Substring($opening+1,$closing-$opening-1)
    $mount = '(?im)^[ \t]*Game[ \t]+"?csgo[/\\]overrides[/\\]caoren_admin_menu\.vpk"?[ \t]*(?://[^\r\n]*)?\r?$'
    if ([regex]::IsMatch($body,$mount)) { return $Text }
    $anchor = [regex]::Match($body,'(?im)^(?<indent>[ \t]*)Game[ \t]+"?csgo"?[ \t]*(?://[^\r\n]*)?(?:\r?\n|$)')
    if (-not $anchor.Success) { throw 'SearchPaths 内没有 Game csgo 行，停止自动修改。' }
    $newline = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $position = $opening+1+$anchor.Index+$anchor.Length
    $line = $anchor.Groups['indent'].Value+'Game'+[char]9+'csgo/overrides/caoren_admin_menu.vpk'+$newline
    return $Text.Insert($position,$line)
}

function Assert-TestGameStopped {
    if (Get-Process -Name cs2 -ErrorAction SilentlyContinue) { throw 'CS2 仍在运行。请完全退出游戏后再执行。' }
}
