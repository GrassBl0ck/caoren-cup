# CS2MiniGames Repository Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import the verified standalone CS2MiniGames plugin into the Caoren Cup repository as an independent component, add CI and a fourth reproducible Release package, and update public management documentation without deploying or changing external repositories.

**Architecture:** Preserve `CS2MiniGames` as an API-independent plugin component under `mini-games-plugin/`; do not merge it into `game-plugin/`. Build and test it independently, stage only publish output plus its GPL-3.0 license, validate the staged tree and final ZIP, then expose it as the fourth Release asset. Work in the isolated `chore/manage-minigames-plugin` worktree based on local tag `v1.8.7`.

**Tech Stack:** C# 12, .NET 8, CounterStrikeSharp.API 1.0.367, Microsoft.Data.Sqlite 8.0.29, xUnit 2.9.3, Windows PowerShell 5.1+, GitHub Actions, ZIP via `System.IO.Compression`.

## Global Constraints

- Work only in `D:\OpenSourcework\caoren-cup-open-source\.worktrees\manage-minigames-plugin` unless a step explicitly performs a read-only integrity check against another local repository.
- Integration base is local `v1.8.7`, commit `5c428cc9d718632bc5c09e2edc36d4d4b2563faf`.
- Source snapshot is standalone `D:\OpenSourcework\CS2MiniGames` at `a2eeef1427226091b0f596ec58b30211c0aff6eb`.
- The standalone repository must remain unchanged and must not be deleted, pushed, archived, or retagged.
- The current dirty checkout at `D:\OpenSourcework\caoren-cup-open-source` is protected user work. Never stage, overwrite, clean, reset, switch, or commit in it.
- Before modifying any existing file, copy it to an ignored `backup_before_minigames_integration_YYYYMMDD/` directory and record matching SHA-256 values.
- Use `apply_patch` for tracked text edits. Task 1's verified 53-file snapshot copy is the only bulk mechanical-copy exception.
- Never stage backups, `.superpowers/`, `bin/`, `obj/`, `TestResults/`, `release-build/`, `release-output/`, ZIP files, databases, logs, runtime configuration, or secrets.
- Use UTF-8 for all text files. PowerShell-facing Chinese ZIP labels must be created from UTF-8 Base64 so Windows PowerShell 5.1 does not corrupt them.
- The fourth asset is exactly `CS2MiniGames-小游戏插件-vX.X.X.zip`.
- ZIP version follows the Caoren Cup Release; `CS2MiniGames.ModuleVersion` remains independently versioned (`0.1.0` for this snapshot).
- The component is GPL-3.0 even though the repository root project is MIT; the component license boundary must be explicit.
- No GitHub push, PR, tag, Release, server upload, server file mutation, plugin reload, or process restart is authorized by this plan.
- After every task, run a read-only check of the protected checkout and confirm its pre-existing dirty file list is unchanged.

---

## File Map

**New component tree**

- Create: `mini-games-plugin/.gitignore` — component-local ignores carried from the standalone repository.
- Create: `mini-games-plugin/CS2MiniGames.sln` — component solution.
- Create: `mini-games-plugin/LICENSE` — GPL-3.0 license boundary.
- Create: `mini-games-plugin/README.md` — component build, install, commands, controls, and lifecycle documentation.
- Create: `mini-games-plugin/docs/superpowers/specs/2026-07-17-tetris-minigame-design.md` — original gameplay design provenance.
- Create: `mini-games-plugin/docs/superpowers/plans/2026-07-17-tetris-minigame.md` — original implementation plan provenance.
- Create: `mini-games-plugin/scripts/Verify-Package.ps1` — component publish-tree verifier.
- Create: `mini-games-plugin/src/CS2MiniGames/**` — plugin production code.
- Create: `mini-games-plugin/tests/CS2MiniGames.Tests/**` — 170-test suite.

**Repository management**

- Create: `scripts/package-caoren-minigames.ps1` — builds, verifies, zips, reopens, and hashes the fourth asset.
- Modify: `.github/workflows/ci.yml` — adds an isolated `minigames-test` job.
- Modify: `README.md` — changes three-component Release guidance to four components and documents the mixed license boundary.
- Modify: `docs/deployment.md` — adds local build and future deployment/preservation guidance for CS2MiniGames.
- Create: `docs/release-notes-template.md` — public four-section Release notes template.

---

### Task 1: Import the verified standalone snapshot

**Files:**

- Create: `mini-games-plugin/**` from the exact standalone tracked-file snapshot.

**Interfaces:**

- Consumes: standalone repository HEAD `a2eeef1427226091b0f596ec58b30211c0aff6eb` and `git ls-files` output.
- Produces: `mini-games-plugin/CS2MiniGames.sln`, build output contract, 170-test suite, and `mini-games-plugin/scripts/Verify-Package.ps1` for later tasks.

- [ ] **Step 1: Record protected state and source identity**

Run:

```powershell
$protected = 'D:\OpenSourcework\caoren-cup-open-source'
$source = 'D:\OpenSourcework\CS2MiniGames'

git -C $protected status --short --branch
git -C $source status --short --branch
git -C $source rev-parse HEAD
git -C $source ls-files
```

Expected:

- protected checkout still shows only its pre-existing CI, desktop, private bot, C4 test, handoff doc, and bot packaging changes;
- standalone source reports `## main` with no tracked changes;
- source HEAD equals `a2eeef1427226091b0f596ec58b30211c0aff6eb`.

Create the ignored baseline and implementation-base ledgers:

```powershell
$ledgerRoot = '.\.superpowers\sdd'
New-Item -ItemType Directory -Force -Path $ledgerRoot | Out-Null

$implementationBase = (git rev-parse HEAD).Trim()
$implementationBase | Set-Content `
    (Join-Path $ledgerRoot 'minigames-implementation-base.txt') `
    -Encoding UTF8

$protectedStatus = @(
    git -c core.quotepath=false -C $protected status --porcelain=v1 -uall
)
$protectedFiles = @()
foreach ($line in $protectedStatus) {
    if ($line.Length -lt 4) { continue }
    $relativePath = $line.Substring(3)
    $fullPath = Join-Path $protected $relativePath
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        $protectedFiles += [pscustomobject]@{
            Path = $relativePath.Replace('\', '/')
            Sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        }
    }
}

[ordered]@{
    Status = $protectedStatus
    Files = @($protectedFiles | Sort-Object Path)
} | ConvertTo-Json -Depth 4 | Set-Content `
    (Join-Path $ledgerRoot 'minigames-protected-baseline.json') `
    -Encoding UTF8
```

Expected: both ledger files exist under ignored `.superpowers/sdd/`; neither is staged.

- [ ] **Step 2: Verify the destination is absent (RED gate)**

Run:

```powershell
if (Test-Path '.\mini-games-plugin') {
    throw 'mini-games-plugin must not exist before snapshot import.'
}

dotnet test '.\mini-games-plugin\CS2MiniGames.sln' --no-restore
```

Expected: the test command fails because the solution does not exist. This is the migration RED gate; do not create a fake test failure.

- [ ] **Step 3: Copy only tracked standalone files**

Run this mechanical snapshot copy from the integration worktree root:

```powershell
$source = 'D:\OpenSourcework\CS2MiniGames'
$expectedHead = 'a2eeef1427226091b0f596ec58b30211c0aff6eb'
$destination = Join-Path (Get-Location) 'mini-games-plugin'

if ((git -C $source rev-parse HEAD).Trim() -ne $expectedHead) {
    throw 'Standalone CS2MiniGames HEAD changed; stop and review the new source.'
}

if (git -C $source status --porcelain) {
    throw 'Standalone CS2MiniGames working tree is not clean.'
}

$tracked = @(git -C $source ls-files)
if ($tracked.Count -ne 53) {
    throw "Expected 53 tracked snapshot files, found $($tracked.Count)."
}

foreach ($relativePath in $tracked) {
    $sourcePath = Join-Path $source $relativePath
    $targetPath = Join-Path $destination $relativePath
    $targetParent = Split-Path -Parent $targetPath
    New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath
}
```

Expected: exactly the 53 tracked files appear under `mini-games-plugin/`; no `.git/`, build output, test output, database, configuration, or backup is copied.

- [ ] **Step 4: Compare every copied file by SHA-256**

Run:

```powershell
$source = 'D:\OpenSourcework\CS2MiniGames'
$destination = Join-Path (Get-Location) 'mini-games-plugin'
$mismatches = @()

foreach ($relativePath in @(git -C $source ls-files)) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $source $relativePath) -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath (Join-Path $destination $relativePath) -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        $mismatches += $relativePath
    }
}

if ($mismatches.Count -gt 0) {
    throw "Snapshot hash mismatch:`n$($mismatches -join "`n")"
}
```

Expected: no mismatches.

- [ ] **Step 5: Run component tests and Release build (GREEN)**

Run:

```powershell
dotnet restore '.\mini-games-plugin\CS2MiniGames.sln'
dotnet test '.\mini-games-plugin\CS2MiniGames.sln' --no-restore --logger 'console;verbosity=minimal'
dotnet build '.\mini-games-plugin\CS2MiniGames.sln' -c Release --no-restore --no-incremental -warnaserror
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\mini-games-plugin\scripts\Verify-Package.ps1' `
  -OutputPath '.\mini-games-plugin\src\CS2MiniGames\bin\Release\net8.0'
```

Expected: 170/170 tests pass; Release build has 0 warnings and 0 errors; package tree verifier prints `PASS`.

- [ ] **Step 6: Check scope and commit**

Run:

```powershell
git status --short --ignored
git add mini-games-plugin
git diff --cached --stat
git diff --cached --check
git commit -m 'feat: import CS2 mini games plugin'
```

Expected: only `mini-games-plugin/` tracked snapshot files are committed; all build output is ignored.

---

### Task 2: Add the fourth-package build script

**Files:**

- Create: `scripts/package-caoren-minigames.ps1`

**Interfaces:**

- Consumes: `-Version vX.X.X`, `mini-games-plugin/CS2MiniGames.sln`, `mini-games-plugin/src/CS2MiniGames/CS2MiniGames.csproj`, component `LICENSE`, and component verifier.
- Produces: `release-output/CS2MiniGames-小游戏插件-vX.X.X.zip`, clean staging directory `release-build/CS2MiniGames-publish/`, and a printed SHA-256.

- [ ] **Step 1: Verify the package entry point is missing (RED)**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\scripts\package-caoren-minigames.ps1' `
  -Version v9.9.9
```

Expected: FAIL because the script does not exist.

- [ ] **Step 2: Implement the package script**

Create `scripts/package-caoren-minigames.ps1` with this complete structure and behavior:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$componentRoot = Join-Path $repoRoot 'mini-games-plugin'
$solutionPath = Join-Path $componentRoot 'CS2MiniGames.sln'
$projectPath = Join-Path $componentRoot 'src\CS2MiniGames\CS2MiniGames.csproj'
$verifierPath = Join-Path $componentRoot 'scripts\Verify-Package.ps1'
$licensePath = Join-Path $componentRoot 'LICENSE'
$releaseBuild = Join-Path $repoRoot 'release-build'
$releaseOutput = Join-Path $repoRoot 'release-output'
$stagePath = Join-Path $releaseBuild 'CS2MiniGames-publish'
$utf8 = [System.Text.Encoding]::UTF8
$packageLabel = $utf8.GetString(
    [Convert]::FromBase64String('5bCP5ri45oiP5o+S5Lu2'))
$zipPath = Join-Path $releaseOutput "CS2MiniGames-$packageLabel-$Version.zip"

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): dotnet $($Arguments -join ' ')"
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + `
        [System.IO.Path]::DirectorySeparatorChar
    $childFull = [System.IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith(
        $parentFull,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path outside expected parent: $childFull"
    }
}

foreach ($requiredPath in @(
    $solutionPath,
    $projectPath,
    $verifierPath,
    $licensePath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required input is missing: $requiredPath"
    }
}

if (Test-Path -LiteralPath $zipPath) {
    throw "Output already exists: $zipPath"
}

New-Item -ItemType Directory -Force -Path $releaseBuild | Out-Null
New-Item -ItemType Directory -Force -Path $releaseOutput | Out-Null
Assert-ChildPath -Parent $releaseBuild -Child $stagePath
if (Test-Path -LiteralPath $stagePath) {
    Remove-Item -LiteralPath $stagePath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagePath | Out-Null

Invoke-DotNet @('restore', $solutionPath)
Invoke-DotNet @(
    'test', $solutionPath,
    '--no-restore',
    '--logger', 'console;verbosity=minimal'
)
Invoke-DotNet @(
    'publish', $projectPath,
    '-c', 'Release',
    '--no-restore',
    '-warnaserror',
    '-o', $stagePath
)

Copy-Item -LiteralPath $licensePath -Destination (Join-Path $stagePath 'LICENSE')

& powershell -NoProfile -ExecutionPolicy Bypass `
    -File $verifierPath -OutputPath $stagePath
if ($LASTEXITCODE -ne 0) {
    throw "Package tree verification failed with exit code $LASTEXITCODE."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open(
    $zipPath,
    [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $stagePath -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($stagePath.Length).TrimStart('\', '/')
            $entryName = $relative.Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $_.FullName,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
}
finally {
    $archive.Dispose()
}

$requiredEntries = @(
    'CS2MiniGames.dll',
    'CS2MiniGames.deps.json',
    'Microsoft.Data.Sqlite.dll',
    'runtimes/linux-x64/native/libe_sqlite3.so',
    'LICENSE'
)
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
    $missing = @($requiredEntries | Where-Object { $_ -notin $entryNames })
    $forbidden = @($entryNames | Where-Object {
        $_ -match '(^|/)(bin|obj|\.vs|TestResults|release-build|release-output)(/|$)' -or
        $_ -match '(^|/)backup(?:$|[-_/])' -or
        $_ -match '(?i)\.db(?:$|-(?:wal|shm|journal)$)' -or
        $_ -match '(?i)\.(?:log|bak)(?:$|-)' -or
        $_ -match '(^|/)(CS2MiniGames\.json|\.env(?:\..*)?)$'
    })

    if ($missing.Count -gt 0) {
        throw "ZIP missing required entries:`n$($missing -join "`n")"
    }
    if ($forbidden.Count -gt 0) {
        throw "ZIP contains forbidden entries:`n$($forbidden -join "`n")"
    }
}
finally {
    $zip.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Output "PASS: $zipPath"
Write-Output "SHA-256: $hash"
```

- [ ] **Step 3: Verify version rejection and successful package generation (GREEN)**

Run invalid input:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\scripts\package-caoren-minigames.ps1' `
  -Version 9.9.9
```

Expected: parameter validation rejects the value because it lacks `v`.

Run valid input:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\scripts\package-caoren-minigames.ps1' `
  -Version v9.9.9
```

Expected: 170/170 tests pass, publish has 0 errors/warnings, verifier prints PASS, ZIP is `release-output/CS2MiniGames-小游戏插件-v9.9.9.zip`, and SHA-256 is printed.

- [ ] **Step 4: Independently inspect ZIP entries**

Run:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = '.\release-output\CS2MiniGames-小游戏插件-v9.9.9.zip'
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $zipPath))
try {
    $zip.Entries | Sort-Object FullName | Select-Object FullName, Length
}
finally {
    $zip.Dispose()
}
```

Expected: required DLL/deps/native library/LICENSE appear at ZIP root-relative paths; no database, log, config, backup, source, `bin/`, or `obj/` entry appears.

- [ ] **Step 5: Check scope and commit**

Run:

```powershell
git status --short --ignored
git add scripts/package-caoren-minigames.ps1
git diff --cached --stat
git diff --cached --check
git commit -m 'build: package CS2 mini games plugin'
```

Expected: only the root package script is committed; ZIP and staging trees remain ignored.

---

### Task 3: Add independent CI validation

**Files:**

- Modify: `.github/workflows/ci.yml`

**Interfaces:**

- Consumes: imported solution, tests, Release output path, and component verifier.
- Produces: GitHub Actions job `minigames-test` on `ubuntu-latest`.

- [ ] **Step 1: Back up the workflow and prove the job is absent (RED)**

Run:

```powershell
$backupRoot = '.\backup_before_minigames_integration_20260719\task3-ci'
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
Copy-Item '.\.github\workflows\ci.yml' `
  (Join-Path $backupRoot 'ci.yml.bak-before-minigames')
Get-FileHash '.\.github\workflows\ci.yml', `
  (Join-Path $backupRoot 'ci.yml.bak-before-minigames') -Algorithm SHA256

if (Select-String -Path '.\.github\workflows\ci.yml' -Pattern '^  minigames-test:$') {
    throw 'minigames-test unexpectedly exists before implementation.'
}
```

Expected: source and backup hashes match; the job is absent.

- [ ] **Step 2: Add the exact CI job**

Append this job under `jobs:` without changing existing jobs:

```yaml
  minigames-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Restore mini games plugin
        working-directory: mini-games-plugin
        run: dotnet restore

      - name: Test mini games plugin
        working-directory: mini-games-plugin
        run: dotnet test --no-restore --logger "console;verbosity=minimal"

      - name: Build mini games plugin
        working-directory: mini-games-plugin
        run: dotnet build -c Release --no-restore --no-incremental -warnaserror

      - name: Verify mini games package tree
        shell: pwsh
        working-directory: mini-games-plugin
        run: >-
          ./scripts/Verify-Package.ps1
          -OutputPath ./src/CS2MiniGames/bin/Release/net8.0
```

- [ ] **Step 3: Verify job text and execute the same commands locally (GREEN)**

Run:

```powershell
$workflow = Get-Content '.\.github\workflows\ci.yml' -Raw -Encoding UTF8
foreach ($required in @(
    'minigames-test:',
    'working-directory: mini-games-plugin',
    'dotnet test --no-restore',
    '-warnaserror',
    './scripts/Verify-Package.ps1'
)) {
    if (-not $workflow.Contains($required)) {
        throw "Workflow missing: $required"
    }
}

dotnet restore '.\mini-games-plugin\CS2MiniGames.sln'
dotnet test '.\mini-games-plugin\CS2MiniGames.sln' --no-restore
dotnet build '.\mini-games-plugin\CS2MiniGames.sln' `
  -c Release --no-restore --no-incremental -warnaserror
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\mini-games-plugin\scripts\Verify-Package.ps1' `
  -OutputPath '.\mini-games-plugin\src\CS2MiniGames\bin\Release\net8.0'
```

Expected: all required CI fragments exist; 170 tests and Release/package checks pass locally.

- [ ] **Step 4: Prove existing public jobs were not removed**

Run:

```powershell
git diff -- '.github/workflows/ci.yml'

$workflow = Get-Content '.\.github\workflows\ci.yml' -Raw -Encoding UTF8
foreach ($required in @(
    'web-typecheck:',
    'dotnet-build:',
    'working-directory: game-plugin',
    'working-directory: web-command-center/CaorenCupPlugin'
)) {
    if (-not $workflow.Contains($required)) {
        throw "Existing CI contract was removed: $required"
    }
}
```

Expected: diff adds only `minigames-test`; existing web/game/bridge jobs remain.

- [ ] **Step 5: Check scope and commit**

Run:

```powershell
git status --short --ignored
git add .github/workflows/ci.yml
git diff --cached --stat
git diff --cached --check
git commit -m 'ci: validate CS2 mini games plugin'
```

Expected: only the workflow is committed; private bot CI changes from the protected checkout are absent.

---

### Task 4: Update four-component public documentation

**Files:**

- Modify: `README.md`
- Modify: `docs/deployment.md`
- Create: `docs/release-notes-template.md`

**Interfaces:**

- Consumes: fourth asset name, component path, deployment target, data-preservation rules, and mixed license decision.
- Produces: public operator guidance for four assets and a reusable four-section Release notes template.

- [ ] **Step 1: Back up existing docs and prove they still describe three packages (RED)**

Run:

```powershell
$backupRoot = '.\backup_before_minigames_integration_20260719\task4-docs'
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
Copy-Item '.\README.md' (Join-Path $backupRoot 'README.md.bak-before-four-packages')
Copy-Item '.\docs\deployment.md' (Join-Path $backupRoot 'deployment.md.bak-before-minigames')
Get-FileHash '.\README.md', (Join-Path $backupRoot 'README.md.bak-before-four-packages') -Algorithm SHA256
Get-FileHash '.\docs\deployment.md', (Join-Path $backupRoot 'deployment.md.bak-before-minigames') -Algorithm SHA256

rg -n '三个包|上传三个 zip|统一版本号上传三个包' README.md docs
```

Expected: backup hashes match and the old three-package wording is found.

- [ ] **Step 2: Update README package and component sections**

Replace the public package list with exactly:

```text
CaorenCup-修改插件本体-vX.X.X.zip
CaorenCupWeb-网页端-vX.X.X.zip
CaorenCupWebPlugin-网页端服务器插件-vX.X.X.zip
CS2MiniGames-小游戏插件-vX.X.X.zip
```

Add the fourth component block:

````md
### 4. CS2 小游戏插件

包名：

```text
CS2MiniGames-小游戏插件-vX.X.X.zip
```

来源目录：

```text
mini-games-plugin/
```

本地打包：

```powershell
.\scripts\package-caoren-minigames.ps1 -Version vX.X.X
```

部署目标：

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CS2MiniGames/
```

小游戏插件内部版本独立管理；ZIP 文件名使用草人杯统一 Release 版本。运行数据库和服务器配置不属于公开包。
````

Change Release upload guidance from three ZIPs to four ZIPs, and change “统一上传三个包” wording to “统一上传四个包；服务器只部署实际改动组件”. Do not add private server paths to the public README.

Replace the root license paragraph with this mixed-license boundary:

```md
## 许可证

草人杯仓库主体使用 MIT License。

`mini-games-plugin/` 作为独立组件使用 GNU General Public License v3.0（GPL-3.0），其复制、修改和分发以该目录内的 `LICENSE` 为准。发布或再分发小游戏插件二进制时必须保留对应许可证与源码获取方式。
```

- [ ] **Step 3: Update deployment documentation**

Append these public, path-neutral sections to `docs/deployment.md`:

````md
## 8. Build CS2 Mini Games Plugin

```powershell
cd mini-games-plugin
dotnet restore
dotnet test '.\CS2MiniGames.sln' --no-restore
dotnet build '.\CS2MiniGames.sln' -c Release --no-restore -warnaserror
```

Create the Release asset from the repository root:

```powershell
.\scripts\package-caoren-minigames.ps1 -Version vX.X.X
```

Deploy the ZIP contents to:

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CS2MiniGames/
```

Before overwriting, back up the existing plugin directory. Preserve `minigames.db`, SQLite sidecar files, and CounterStrikeSharp runtime configuration. Do not deploy the other three components when only the mini games plugin changed.
````

Keep the existing numbered sections unchanged except for adding this new section after the bridge build guidance; renumber the existing “Do Not Commit Local Config” section if necessary.

- [ ] **Step 4: Create the public Release notes template**

Create `docs/release-notes-template.md` with exactly this public structure and guidance:

```md
# Caoren Cup vX.X.X 更新说明

## 一、网页端

### 1. 故障修复

本版本没有网页端故障修复。

### 2. 新增/修改内容

本版本没有网页端功能变更。

## 二、游戏插件

### 1. 故障修复

本版本没有游戏插件故障修复。

### 2. 新增/修改内容

本版本没有游戏插件功能变更。

## 三、桥接插件

### 1. 故障修复

本版本没有桥接插件故障修复。

### 2. 新增/修改内容

本版本没有桥接插件功能变更。

## 四、小游戏插件

### 1. 故障修复

本版本没有小游戏插件故障修复。

### 2. 新增/修改内容

本版本没有小游戏插件功能变更。
```

Below the template, state that private paths, upload steps, backups, PM2/process information, production configuration, and server IPs must never appear in public Release notes. State that website announcements are player-facing and are not copied from this template.

- [ ] **Step 5: Verify there is no stale three-package guidance**

Run:

```powershell
rg -n '三个包|上传三个 zip|统一版本号上传三个包' README.md docs
rg -n 'CS2MiniGames-小游戏插件-vX\.X\.X\.zip|mini-games-plugin/' README.md docs
rg -n 'GPL-3\.0|GNU General Public License' README.md mini-games-plugin/README.md
```

Expected: first search returns no current guidance outside historical design/plan documents; new package/path/license searches find authoritative README and deployment text. Do not rewrite historical specs solely to replace historical “three package” statements.

- [ ] **Step 6: Check scope and commit**

Run:

```powershell
git status --short --ignored
git add README.md docs/deployment.md docs/release-notes-template.md
git diff --cached --stat
git diff --cached --check
git commit -m 'docs: document fourth minigames release package'
```

Expected: only the three public docs are committed.

---

### Task 5: Run full integration verification and prepare handoff

**Files:**

- No tracked files expected unless verification finds a defect requiring a reviewed fix.
- Create ignored report: `.superpowers/sdd/minigames-integration-final-report.md`.

**Interfaces:**

- Consumes: all prior task outputs.
- Produces: evidence that the branch is locally releasable as four components without external side effects.

- [ ] **Step 1: Re-run the imported component gate**

Run:

```powershell
dotnet restore '.\mini-games-plugin\CS2MiniGames.sln'
dotnet test '.\mini-games-plugin\CS2MiniGames.sln' --no-restore --logger 'console;verbosity=minimal'
dotnet build '.\mini-games-plugin\CS2MiniGames.sln' `
  -c Release --no-restore --no-incremental -warnaserror
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\mini-games-plugin\scripts\Verify-Package.ps1' `
  -OutputPath '.\mini-games-plugin\src\CS2MiniGames\bin\Release\net8.0'
```

Expected: 170/170 tests, 0 warnings/errors, verifier PASS.

- [ ] **Step 2: Re-run existing .NET component builds**

Run:

```powershell
dotnet build '.\game-plugin\CaorenCup.csproj' -c Release -warnaserror
dotnet build '.\web-command-center\CaorenCupPlugin\CaorenCupPlugin.csproj' `
  -c Release -warnaserror
```

Expected: both existing plugins build with 0 warnings and 0 errors.

- [ ] **Step 3: Run the existing web typecheck**

Run:

```powershell
cd '.\web-command-center'
npm install --no-audit
npm run typecheck
cd '..'
```

Expected: dependency install succeeds and TypeScript exits 0. If network or registry access blocks install, record the exact failure and do not claim web verification passed.

- [ ] **Step 4: Generate and inspect a fresh fourth package**

Use a version not present in `release-output/`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File '.\scripts\package-caoren-minigames.ps1' `
  -Version v9.9.8
```

Expected: fourth ZIP and SHA-256 are produced; tests/build/verifier inside the script pass.

- [ ] **Step 5: Verify tracked and ignored scope**

Run:

```powershell
git status --short --ignored
git diff --check
git log --oneline --decorate -8
```

Expected: branch `chore/manage-minigames-plugin` has no tracked changes; only expected ignored build/package/backup/report directories appear.

- [ ] **Step 6: Verify both source repositories and protected checkout**

Run:

```powershell
git -C 'D:\OpenSourcework\CS2MiniGames' status --short --branch
git -C 'D:\OpenSourcework\CS2MiniGames' rev-parse HEAD
git -C 'D:\OpenSourcework\caoren-cup-open-source' status --short --branch

$protected = 'D:\OpenSourcework\caoren-cup-open-source'
$baseline = Get-Content `
    '.\.superpowers\sdd\minigames-protected-baseline.json' `
    -Raw -Encoding UTF8 | ConvertFrom-Json
$currentStatus = @(
    git -c core.quotepath=false -C $protected status --porcelain=v1 -uall
)

if (($currentStatus -join "`n") -ne ($baseline.Status -join "`n")) {
    throw 'Protected checkout status changed during integration.'
}

foreach ($file in $baseline.Files) {
    $fullPath = Join-Path $protected $file.Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Protected file disappeared: $($file.Path)"
    }
    $currentHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
    if ($currentHash -ne $file.Sha256) {
        throw "Protected file changed: $($file.Path)"
    }
}
```

Expected:

- standalone repo remains clean at `a2eeef1`;
- protected checkout has exactly the same pre-existing changes and hashes;
- no external repository was modified.

- [ ] **Step 7: Record manual checks that remain pending**

Write `.superpowers/sdd/minigames-integration-final-report.md` with:

```text
- No GitHub push/PR/tag/Release performed.
- No server upload, overwrite, reload, or restart performed.
- GitHub archival notice for the standalone repo remains pending explicit approval.
- Real CS2 smoke tests remain pending deployment approval:
  !tetris UI, A/D/S/Space/E/R/W/Tab, lifecycle restore,
  two-player isolation, !toptetris persistence, unload safety.
```

Do not commit the ignored report.

- [ ] **Step 8: Request whole-branch review**

Review range:

```powershell
$base = (Get-Content '.\.superpowers\sdd\minigames-implementation-base.txt' -Raw).Trim()
$head = git rev-parse HEAD
git diff --stat "$base..$head"
git diff "$base..$head"
```

Reviewer must check spec alignment, source snapshot integrity, license boundary, CI isolation, package safety, four-asset docs, and protected-worktree integrity. Fix all Critical and Important findings with TDD and re-review before completion.

---

## Completion Handoff

When all tasks and whole-branch review pass, use `verification-before-completion`, then `finishing-a-development-branch`. Do not merge into another dirty checkout. Present the user with local merge, push/PR, keep branch, or discard options; pushing, Release creation, and server deployment remain separate approvals.
