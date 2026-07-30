# 草人杯赛前指引与完整规则 PDF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增加一个登录前后均可访问的公开赛前指引页，并从更新后的完整规则源生成可下载 PDF。

**Architecture:** 使用独立静态 `/rules.html` 承载玩家快速指引，并在现有持久品牌头部加入入口。完整规则以 UTF-8 Markdown 作为唯一维护源，通过开发期 Python/ReportLab 脚本生成并提交 PDF；线上 Node 服务只负责静态文件分发，不增加运行时依赖。

**Tech Stack:** 原生 HTML/CSS/JavaScript、Node.js 静态契约测试、Python 3、ReportLab 4.4.9、现有 Express 静态目录。

## Global Constraints

- 开始修改前，为所有将修改的已有文件创建 Git 忽略的 UTF-8 备份。
- 不修改固定成员密码、邀请码、设备令牌、确认码、管理员鉴权、Session、CS2 插件、游戏插件或桌面客户端逻辑。
- `/rules.html` 必须无需登录、Socket 或 API 即可阅读。
- 快速指引仅包含玩家开赛前需要的信息，不包含管理员页面、身份库 schema、部署和数据导入说明。
- PDF 与快速指引必须准确描述固定成员、临时参赛者和旧命令恢复流程。
- PDF 继续公开 `119.45.166.182:27015` 与 QQ `3465434029`。
- PDF 和快速指引均包含 HTTP 下使用独立密码的提醒，不包含管理员密码、哈希、令牌、生产配置或服务器文件路径。
- 页面沿用现有浅色/深色变量，断点覆盖 360px、768px、1440px 和 1920px。
- 原 Word 文件 `C:\Users\grassbl0ck\Desktop\玩！\草人杯规则文档.docx` 只作为迁移来源，不修改、不删除、不提交。

---

### Task 1: 建立完整规则唯一内容源

**Files:**
- Create: `docs/rules/caoren-cup-full-rules.md`
- Create: `web-command-center/scripts/check-rules-content.cjs`
- Modify: `web-command-center/package.json`

**Interfaces:**
- Consumes: 原 Word 规则文档及 `docs/superpowers/specs/2026-07-13-rules-guide-design.md`。
- Produces: UTF-8 Markdown 完整规则源；命令 `npm run test:rules-content`。

- [ ] **Step 1: 备份 package.json**

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Copy-Item web-command-center/package.json "web-command-center/package.json.bak-$stamp-rules-guide"
git check-ignore "web-command-center/package.json.bak-$stamp-rules-guide"
```

Expected: 备份文件存在且 `git check-ignore` 返回成功。

- [ ] **Step 2: 写入内容契约测试并注册 npm 命令**

Create `web-command-center/scripts/check-rules-content.cjs` with assertions equivalent to:

```js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..');
const sourcePath = path.join(root, 'docs', 'rules', 'caoren-cup-full-rules.md');
assert.ok(fs.existsSync(sourcePath), '完整规则 Markdown 不存在');
const source = fs.readFileSync(sourcePath, 'utf8');

for (const text of [
  '# 草人杯完整规则',
  '固定成员',
  '临时参赛者',
  'SteamID64',
  '本场确认',
  'connect 119.45.166.182:27015',
  'QQ：3465434029',
  '不平衡竞技',
  '卧底模式',
  'Demo 查看方式',
  '娱乐玩法修改汇总',
]) assert.ok(source.includes(text), `缺少规则内容：${text}`);

for (const obsolete of [
  '网页会显示绑定码',
  '玩家进入网页大厅，输入昵称加入房间',
  '!ccbind 绑定码 完成绑定',
]) assert.ok(!source.includes(obsolete), `仍包含旧默认绑定说明：${obsolete}`);

assert.match(source, /!ccbind[\s\S]{0,100}(故障恢复|特殊情况)/);
assert.match(source, /HTTP[\s\S]{0,160}(独立密码|不要.*共用)/);
console.log('rules content contract checks passed');
```

Add to `web-command-center/package.json`:

```json
"test:rules-content": "node scripts/check-rules-content.cjs"
```

- [ ] **Step 3: 运行测试并确认失败**

Run:

```powershell
cd web-command-center
npm run test:rules-content
```

Expected: FAIL with `完整规则 Markdown 不存在`。

- [ ] **Step 4: 将 Word 内容迁移为完整 Markdown**

Create `docs/rules/caoren-cup-full-rules.md` with this exact top-level outline:

```markdown
# 草人杯完整规则

更新日期：2026-07-13

## 一、草人杯简介
## 二、进入服务器与赛前准备
## 三、玩法分类总览
## 四、网页对局指挥台通用流程
## 五、通用行为规则
## 六、服务器插件与玩家指令
## 七、各玩法规则
## 八、卧底模式详细规则与计分
## 九、Demo 查看方式
## 十、娱乐玩法修改汇总
## 十一、常见问题
## 十二、关于草人杯
## 十三、最终解释
```

Transcribe every substantive paragraph and table from the Word source. Rewrite the entry section using this canonical copy:

```markdown
### 4.1 大厅与本场确认

固定成员使用管理员预设的 SteamID64 和独立密码进入当前网页大厅，不需要邀请码。进入 CS2 后，桥接插件读取服务器可信 SteamID；与预设 SteamID 完全一致时自动完成本场确认，不需要游戏内确认码、`!cclogin` 或 `!ccbind`。

临时参赛者使用本场邀请码、昵称和 SteamID64 进入网页大厅。进入 CS2 后，按网页显示的确认流程完成本场身份核对。

`!cclogin`、`!cccode` 和 `!ccbind` 继续保留，只用于首次识别失败、账号不一致或其他特殊情况的故障恢复，不是普通入场的默认步骤。

> 密码提醒：草人杯当前使用 HTTP。固定成员应使用只给草人杯使用的独立密码，不要与 Steam、QQ、邮箱或其他网站共用。
```

In sections 7.3 and 8.2 replace “完成玩家绑定” with “进入网页大厅并完成本场确认”。Convert the entertainment modification summary from the Word two-column layout into a numbered Markdown list so the PDF can paginate cleanly。

- [ ] **Step 5: 运行内容测试**

Run: `npm run test:rules-content`

Expected: PASS with `rules content contract checks passed`。

- [ ] **Step 6: 提交规则源**

```powershell
git add docs/rules/caoren-cup-full-rules.md web-command-center/scripts/check-rules-content.cjs web-command-center/package.json
git diff --cached --check
git commit -m "docs: migrate complete caoren cup rules"
```

---

### Task 2: 构建并验证完整规则 PDF

**Files:**
- Create: `scripts/build-rules-pdf.py`
- Create: `scripts/rules-pdf-requirements.txt`
- Create: `web-command-center/scripts/check-rules-pdf.cjs`
- Create: `web-command-center/public/downloads/caoren-cup-full-rules.pdf`
- Modify: `web-command-center/package.json`

**Interfaces:**
- Consumes: `docs/rules/caoren-cup-full-rules.md`。
- Produces: `python scripts/build-rules-pdf.py --source docs/rules/caoren-cup-full-rules.md --output web-command-center/public/downloads/caoren-cup-full-rules.pdf --font C:\Windows\Fonts\simhei.ttf`；命令 `npm run test:rules-pdf`；公开 PDF 下载资产。

- [ ] **Step 1: 写入 PDF 资产契约测试**

Create `web-command-center/scripts/check-rules-pdf.cjs`:

```js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const pdfPath = path.resolve(__dirname, '..', 'public', 'downloads', 'caoren-cup-full-rules.pdf');
assert.ok(fs.existsSync(pdfPath), '完整规则 PDF 不存在');
const pdf = fs.readFileSync(pdfPath);
assert.ok(pdf.length > 50_000, '完整规则 PDF 体积异常');
assert.equal(pdf.subarray(0, 5).toString('ascii'), '%PDF-', '下载资产不是 PDF');
console.log('rules PDF contract checks passed');
```

Add to `package.json`:

```json
"test:rules-pdf": "node scripts/check-rules-pdf.cjs"
```

- [ ] **Step 2: 运行 PDF 测试并确认失败**

Run: `npm run test:rules-pdf`

Expected: FAIL with `完整规则 PDF 不存在`。

- [ ] **Step 3: 建立可重复的 ReportLab 构建器**

Create `scripts/rules-pdf-requirements.txt`:

```text
reportlab==4.4.9
```

Create `scripts/build-rules-pdf.py` with these concrete interfaces:

```python
@dataclass(frozen=True)
class MarkdownBlock:
    kind: Literal['heading', 'paragraph', 'bullet', 'number', 'quote', 'table']
    text: str = ''
    level: int = 0
    rows: tuple = ()

# Required public functions and return types:
# parse_markdown(source: str) -> list[MarkdownBlock]
# resolve_cjk_font(explicit_path: str | None) -> str
# build_styles(font_name: str) -> dict[str, ParagraphStyle]
# build_story(blocks: list[MarkdownBlock], styles: dict[str, ParagraphStyle], page_width: float) -> list[Flowable]
# build_pdf(source_path: Path, output_path: Path, font_path: str | None) -> None
```

The parser must support `#`/`##`/`###` headings, paragraphs, ordered/unordered lists, block quotes and pipe tables. Build tables with `LongTable(table_data, repeatRows=1, splitByRow=1)` and convert the long entertainment list to paragraphs rather than a wide table. Register the chosen TrueType font with `pdfmetrics.registerFont(TTFont('CaorenRulesCJK', font_path))` and use it for every style.

`resolve_cjk_font()` checks, in order: `--font`, environment variable `CAOREN_RULES_FONT`, `C:\Windows\Fonts\simhei.ttf`, `/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc`; if none exists, raise `FileNotFoundError` with a command example. Use a `BaseDocTemplate` subclass and `afterFlowable()` to register heading entries in a `TableOfContents`, then call `multiBuild()` so the PDF contains a real generated directory. Set PDF title to `草人杯完整规则` and author to `GrassBl0ck` without embedding local file paths。

- [ ] **Step 4: 生成 PDF**

Run with the bundled Python runtime:

```powershell
$python = 'C:\Users\grassbl0ck\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $python scripts/build-rules-pdf.py `
  --source docs/rules/caoren-cup-full-rules.md `
  --output web-command-center/public/downloads/caoren-cup-full-rules.pdf `
  --font C:\Windows\Fonts\simhei.ttf
```

Expected: exit 0 and a non-empty PDF at the public download path。

- [ ] **Step 5: 运行 PDF 契约测试**

Run: `npm run test:rules-pdf`

Expected: PASS with `rules PDF contract checks passed`。

- [ ] **Step 6: 提交 PDF 构建链和资产**

```powershell
git add scripts/build-rules-pdf.py scripts/rules-pdf-requirements.txt web-command-center/scripts/check-rules-pdf.cjs web-command-center/public/downloads/caoren-cup-full-rules.pdf web-command-center/package.json
git diff --cached --check
git commit -m "docs: add downloadable complete rules PDF"
```

---

### Task 3: 实现公开赛前指引页

**Files:**
- Create: `web-command-center/public/rules.html`
- Create: `web-command-center/scripts/check-rules-guide.cjs`
- Modify: `web-command-center/public/css/app.css`
- Modify: `web-command-center/package.json`

**Interfaces:**
- Consumes: 现有 `app.css` 主题变量和 `/downloads/caoren-cup-full-rules.pdf`。
- Produces: 无登录要求的 `/rules.html`；命令 `npm run test:rules-guide`。

- [ ] **Step 1: 备份 app.css 与 package.json**

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Copy-Item web-command-center/public/css/app.css "web-command-center/public/css/app.css.bak-$stamp-rules-guide"
Copy-Item web-command-center/package.json "web-command-center/package.json.bak-$stamp-rules-guide-page"
```

- [ ] **Step 2: 写入规则页契约测试**

Create `web-command-center/scripts/check-rules-guide.cjs` with exact checks:

```js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const publicDir = path.resolve(__dirname, '..', 'public');
const html = fs.readFileSync(path.join(publicDir, 'rules.html'), 'utf8');
for (const id of ['rules-join', 'rules-modes', 'rules-cs2', 'rules-conduct', 'rules-help']) {
  assert.ok(html.includes(`id="${id}"`), `缺少规则章节：${id}`);
}
for (const text of ['固定成员', '临时参赛者', '不平衡竞技', '卧底模式', '.sp 1/3/5/r', 'snd_toolvolume', '独立密码']) {
  assert.ok(html.includes(text), `缺少赛前指引文案：${text}`);
}
assert.match(html, /href="\/downloads\/caoren-cup-full-rules\.pdf"/);
assert.ok(!html.includes('/socket.io/socket.io.js'), '公开规则页不应连接 Socket');
assert.ok(!html.includes('管理员工作区'), '快速指引不应展示管理员内容');
console.log('rules guide contract checks passed');
```

Add to `package.json`:

```json
"test:rules-guide": "node scripts/check-rules-guide.cjs"
```

- [ ] **Step 3: 运行规则页测试并确认失败**

Run: `npm run test:rules-guide`

Expected: FAIL because `public/rules.html` does not exist。

- [ ] **Step 4: 创建语义化规则页**

Create `public/rules.html` with:

```html
<header class="rules-site-header">
  <a class="rules-brand" href="/">草人杯对局指挥台</a>
  <nav aria-label="规则页导航">
    <a href="/">进入大厅</a>
    <a aria-current="page" href="/rules.html">赛前指引</a>
    <a href="/downloads/caoren-cup-full-rules.pdf" download>完整规则 PDF</a>
  </nav>
  <button id="theme-toggle" class="theme-toggle" type="button">深色模式</button>
</header>
```

Add a `<main class="rules-page">` containing the five required sections with IDs from the test. Use an `<aside class="rules-toc">` with anchor links and an `<article class="rules-guide">` for content. Use the approved canonical copy from the visual mock: fixed/temporary login cards, shared match flow, unbalanced `.sp` summary, undercover roles/tasks/interrogation/accusation summary, CS2 reminder, conduct rules, commands and PDF links at top and bottom。

Add an inline theme script that uses the existing key `caoren-theme`, falls back to `prefers-color-scheme`, sets `document.body.dataset.theme`, and updates the same theme button label. Do not load Socket.IO or `lobby-app.js`。

- [ ] **Step 5: 添加规则页样式**

Append a clearly scoped `/* Public rules guide */` block to `app.css`. All selectors must start with `.rules-` or `body.rules-page-body` except reuse of `.theme-toggle`. Define stable desktop grid `grid-template-columns: 220px minmax(0, 1fr)`, sticky TOC, two-column mode/login comparison, numbered steps and success/warning note blocks. At `max-width: 760px`, switch to one column and horizontal scrolling TOC; at `max-width: 420px`, force cards and command rows to one column. Use existing variables `--surface`, `--surface-muted`, `--text-strong`, `--text-default`, `--text-muted`, `--border-subtle`, `--primary` and no inline colors。

- [ ] **Step 6: 运行规则页契约测试**

Run: `npm run test:rules-guide`

Expected: PASS with `rules guide contract checks passed`。

- [ ] **Step 7: 提交规则页**

```powershell
git add web-command-center/public/rules.html web-command-center/public/css/app.css web-command-center/scripts/check-rules-guide.cjs web-command-center/package.json
git diff --cached --check
git commit -m "feat: add public pregame rules guide"
```

---

### Task 4: 在登录前后共用头部增加规则入口

**Files:**
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/scripts/check-rules-guide.cjs`

**Interfaces:**
- Consumes: 现有持久 `.site-header`，它位于 `#login-area` 与 `#lobby-area` 之外。
- Produces: 登录前后始终可见的 `#rules-guide-link`。

- [ ] **Step 1: 备份 index.html**

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Copy-Item web-command-center/public/index.html "web-command-center/public/index.html.bak-$stamp-rules-guide-link"
```

- [ ] **Step 2: 扩展契约测试并确认失败**

Append to `check-rules-guide.cjs`:

```js
const indexHtml = fs.readFileSync(path.join(publicDir, 'index.html'), 'utf8');
assert.match(indexHtml, /id="rules-guide-link"[^>]+href="\/rules\.html"/);
const headerEnd = indexHtml.indexOf('</header>');
const loginStart = indexHtml.indexOf('<div id="login-area"');
assert.ok(headerEnd > 0 && headerEnd < loginStart, '规则入口必须位于登录和大厅共用头部');
```

Run: `npm run test:rules-guide`

Expected: FAIL because `#rules-guide-link` is missing。

- [ ] **Step 3: 修改持久头部**

Change the current header action area to:

```html
<div class="site-header-actions">
  <a id="rules-guide-link" class="header-text-link" href="/rules.html">赛前指引</a>
  <button id="theme-toggle" class="theme-toggle" type="button" onclick="toggleTheme()">深色模式</button>
</div>
```

Add scoped styles for `.site-header-actions` and `.header-text-link` using existing variables. At mobile width, allow wrapping without hiding the link. Do not add event handlers or modify `lobby-app.js`。

- [ ] **Step 4: 运行 UI 回归**

Run:

```powershell
cd web-command-center
npm run test:rules-guide
npm run test:fixed-member-ui
npm run typecheck
```

Expected: all exit 0。

- [ ] **Step 5: 提交持久入口**

```powershell
git add web-command-center/public/index.html web-command-center/public/css/app.css web-command-center/scripts/check-rules-guide.cjs
git diff --cached --check
git commit -m "feat: expose rules guide before and after login"
```

---

### Task 5: 视觉验收、完整回归与发布边界检查

**Files:**
- Verify: `web-command-center/public/rules.html`
- Verify: `web-command-center/public/downloads/caoren-cup-full-rules.pdf`
- Verify: all committed files and package contents

**Interfaces:**
- Consumes: Tasks 1-4 outputs。
- Produces: 浏览器截图、PDF 页面图和最终验证记录；不提交 QA 中间产物。

- [ ] **Step 1: 启动隔离本地服务**

```powershell
cd web-command-center
$env:PORT='3102'
$env:ADMIN_PASSWORD='rules-guide-review-admin'
$env:IDENTITY_STORE_PATH=(Resolve-Path '..\release-build').Path + '\rules-guide-identity.json'
npm run dev
```

Expected: `http://localhost:3102/rules.html` returns 200。服务必须使用隔离身份库，不能读取生产或原工作区 runtime。

- [ ] **Step 2: 浏览器自动化检查页面**

Using the bundled Playwright runtime, capture light and dark screenshots at widths 360, 768, 1440 and 1920. For each viewport assert:

```js
await page.goto('http://localhost:3102/rules.html');
await page.waitForLoadState('networkidle');
const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
if (overflow) throw new Error('rules page has horizontal overflow');
await page.locator('#rules-modes').scrollIntoViewIfNeeded();
await page.screenshot({ path: screenshotPath, fullPage: true });
```

Inspect all screenshots for text overlap, clipped buttons, broken anchors, sticky TOC obstruction and dark-theme contrast. Also visit `/` and confirm `#rules-guide-link` is visible before login。

- [ ] **Step 3: 渲染并逐页检查 PDF**

Run:

```powershell
$out = 'release-build\rules-pdf-qa'
New-Item -ItemType Directory -Force -Path $out | Out-Null
& 'C:\Users\grassbl0ck\.cache\codex-runtimes\codex-primary-runtime\dependencies\bin\override\pdftoppm.cmd' `
  -png -r 150 `
  'web-command-center\public\downloads\caoren-cup-full-rules.pdf' `
  "$out\page"
```

Expected: `release-build/rules-pdf-qa/page-1.png` through the final page exist。Inspect every page at 100% for missing Chinese glyphs, clipped tables, broken list numbering, orphan headings, excessive blank areas and incorrect page numbers. If any defect appears, update Markdown or PDF builder, regenerate and repeat this step。

- [ ] **Step 4: 运行完整回归**

```powershell
cd web-command-center
npm run typecheck
npm run test:lobby-identity
npm run test:fixed-member-ui
npm run test:caoren-modules
npm run test:postmatch-fun-stats
npm run test:rules-content
npm run test:rules-pdf
npm run test:rules-guide
```

Expected: all commands exit 0; identity test output has 56 passing tests and 0 failures。

- [ ] **Step 5: 检查公开内容和 Git 边界**

```powershell
git status --short --ignored
git diff origin/main --name-only
rg -n "管理员密码|password hash|PLUGIN_TOKEN|ADMIN_PASSWORD|/opt/caoren-cup|/root/game_servers" docs/rules web-command-center/public/rules.html
```

Expected: only planned public files differ; no backup, runtime, private Bot/C4 plugin, config, log or QA screenshot is tracked; sensitive scan returns no matches。Public IP and QQ matches are allowed only in the complete rules and player-facing connection/contact copy。

- [ ] **Step 6: 验证网页发布包包含规则资产**

Run `scripts/package-caoren-web.ps1 -Version v1.8.3`, then inspect `release-output/CaorenCupWeb-网页端-v1.8.3.zip`. It must contain:

```text
public/rules.html
public/downloads/caoren-cup-full-rules.pdf
```

It must not contain `node_modules/`, `runtime/`, backups, logs, `.env`, `caoren_config.json`, `ecosystem.config.cjs`, QA screenshots or the original Word file。

- [ ] **Step 7: 最终提交修正**

If visual QA required content or layout fixes, stage only those planned files and commit:

```powershell
git add docs/rules scripts/build-rules-pdf.py scripts/rules-pdf-requirements.txt web-command-center/package.json web-command-center/public web-command-center/scripts/check-rules-*.cjs
git diff --cached --check
git commit -m "fix: polish rules guide and PDF layout"
```

If no files changed after QA, do not create an empty commit。
