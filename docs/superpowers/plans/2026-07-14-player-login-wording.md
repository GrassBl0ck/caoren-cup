# Player Login Wording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace player-facing “固定成员 / 临时参赛者” classification with credential-based “成员账号登录 / 使用邀请码加入” wording and an always-visible choice guide.

**Architecture:** Keep all existing DOM IDs, internal `fixed` / `temporary` values, HTTP requests, Socket events and administrator terminology. Change only player-facing HTML and scoped CSS, then synchronize the public quick guide, Markdown rules and generated PDF from the same approved wording.

**Tech Stack:** Native HTML/CSS/JavaScript, Node.js contract tests, Python 3 + ReportLab PDF generator, npm/TypeScript.

## Global Constraints

- Use UTF-8 for every modified or created text file.
- Create Git-ignored backups before modifying existing files.
- Do not change HTTP, Socket, identity-store, desktop-client or CS2 plugin interfaces.
- Preserve every existing login DOM ID and `fixed` / `temporary` internal value.
- Keep “固定成员账户” in the administrator workspace.
- The choice guide must remain visible above the login mode switch.
- Do not deploy, push, tag or publish until the user separately authorizes those operations.

---

### Task 1: Login wording contract and player login interface

**Files:**
- Modify: `web-command-center/scripts/check-fixed-member-ui.cjs`
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/css/app.css`

**Interfaces:**
- Consumes: existing DOM IDs `login-mode-fixed`, `login-mode-temporary`, `fixed-member-login-btn`, `lobby-invite-enter-btn` and existing `setLoginMode('fixed' | 'temporary')` behavior.
- Produces: always-visible `#login-choice-guide` text block and new player-facing labels; no JavaScript API changes.

- [ ] **Step 1: Back up every existing file**

Run from `D:\OpenSourcework\caoren-cup-rules-guide\web-command-center`:

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Copy-Item scripts\check-fixed-member-ui.cjs "scripts\check-fixed-member-ui.cjs.bak-$stamp-login-wording"
Copy-Item public\index.html "public\index.html.bak-$stamp-login-wording"
Copy-Item public\css\app.css "public\css\app.css.bak-$stamp-login-wording"
git status --short --ignored
```

Expected: all three backups appear with `!!` and are not tracked.

- [ ] **Step 2: Add failing wording assertions**

Append to `check-fixed-member-ui.cjs`:

```js
const choiceGuideIndex = html.indexOf('id="login-choice-guide"');
const modeSwitchIndex = html.indexOf('class="login-mode-switch"');
assert.ok(choiceGuideIndex >= 0, '缺少始终显示的登录方式选择说明');
assert.ok(choiceGuideIndex < modeSwitchIndex, '登录方式选择说明必须位于切换按钮上方');

for (const text of [
  '管理员给了你草人杯成员密码',
  '管理员给了你本场邀请码',
  '成员账号登录',
  '使用邀请码加入',
  '不需要邀请码，也不需要填写昵称',
  '大厅昵称由管理员预设',
  '登录并进入大厅',
  '使用邀请码进入大厅',
]) {
  assert.ok(html.includes(text), `缺少玩家登录说明：${text}`);
}
```

- [ ] **Step 3: Run the contract test and observe the expected failure**

Run:

```powershell
npm run test:fixed-member-ui
```

Expected: FAIL with `缺少始终显示的登录方式选择说明`.

- [ ] **Step 4: Implement the always-visible guide and new labels**

Insert above `.login-mode-switch` in `index.html`:

```html
<aside id="login-choice-guide" class="login-choice-guide" aria-labelledby="login-choice-guide-title">
    <strong id="login-choice-guide-title">不确定该选哪一种？</strong>
    <div class="login-choice-guide-options">
        <p><span>收到成员密码</span>管理员给了你草人杯成员密码，请选择“成员账号登录”。</p>
        <p><span>收到本场邀请码</span>管理员给了你本场邀请码，请选择“使用邀请码加入”。</p>
    </div>
</aside>
```

Change only visible copy while retaining IDs and handlers:

```html
<button id="login-mode-fixed" ...>成员账号登录</button>
<button id="login-mode-temporary" ...>使用邀请码加入</button>

<h3 id="fixed-member-title">成员账号登录</h3>
<p>使用管理员登记的 SteamID64 和草人杯独立密码。不需要邀请码，也不需要填写昵称；大厅昵称由管理员预设。</p>
...
<button id="fixed-member-login-btn" ...>登录并进入大厅</button>

<h3>使用邀请码加入</h3>
<p>使用管理员发放的本场邀请码，并填写本场昵称和 SteamID64。连接 CS2 后，按网页提示完成本场确认。</p>
...
<button id="lobby-invite-enter-btn" ...>使用邀请码进入大厅</button>
```

Change the member password placeholder to `草人杯成员密码`. Do not change administrator workspace text.

- [ ] **Step 5: Add scoped responsive styling**

Add beside the existing `.login-mode-switch` styles in `app.css`:

```css
.login-choice-guide {
    display: grid;
    gap: 10px;
    padding: 14px 16px;
    border: 1px solid var(--border-subtle);
    border-radius: var(--workspace-radius);
    background: var(--surface-muted);
    color: var(--text-default);
}

.login-choice-guide-options {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
}

.login-choice-guide-options p {
    margin: 0;
    color: var(--text-muted);
}

.login-choice-guide-options span {
    display: block;
    margin-bottom: 3px;
    color: var(--text-strong);
    font-weight: 800;
}

@media (max-width: 600px) {
    .login-choice-guide-options {
        grid-template-columns: 1fr;
    }
}
```

- [ ] **Step 6: Run the focused tests**

Run:

```powershell
npm run test:fixed-member-ui
npm run typecheck
git diff --check
```

Expected: all commands pass.

- [ ] **Step 7: Commit the login interface change**

```powershell
git add web-command-center/public/index.html web-command-center/public/css/app.css web-command-center/scripts/check-fixed-member-ui.cjs
git commit -m "fix: clarify player login choices"
```

---

### Task 2: Public quick guide and complete rules wording

**Files:**
- Modify: `web-command-center/scripts/check-rules-guide.cjs`
- Modify: `web-command-center/scripts/check-rules-content.cjs`
- Modify: `web-command-center/public/rules.html`
- Modify: `docs/rules/caoren-cup-full-rules.md`
- Modify (generated): `web-command-center/public/downloads/caoren-cup-full-rules.pdf`

**Interfaces:**
- Consumes: approved player terms from Task 1.
- Produces: consistent public guide, Markdown source and downloadable PDF; PDF URL remains `/downloads/caoren-cup-full-rules.pdf`.

- [ ] **Step 1: Back up rule source and tests**

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Copy-Item web-command-center\scripts\check-rules-guide.cjs "web-command-center\scripts\check-rules-guide.cjs.bak-$stamp-login-wording"
Copy-Item web-command-center\scripts\check-rules-content.cjs "web-command-center\scripts\check-rules-content.cjs.bak-$stamp-login-wording"
Copy-Item web-command-center\public\rules.html "web-command-center\public\rules.html.bak-$stamp-login-wording"
Copy-Item docs\rules\caoren-cup-full-rules.md "docs\rules\caoren-cup-full-rules.md.bak-$stamp-login-wording"
Copy-Item web-command-center\public\downloads\caoren-cup-full-rules.pdf "web-command-center\public\downloads\caoren-cup-full-rules.pdf.bak-$stamp-login-wording"
```

- [ ] **Step 2: Change rule contract tests first**

In `check-rules-guide.cjs`, replace the old player classification terms with:

```js
for (const text of [
  '成员账号登录',
  '使用邀请码加入',
  '收到成员密码',
  '收到本场邀请码',
  '不平衡竞技',
  '卧底模式',
  '.sp 1/3/5/r',
  'snd_toolvolume',
  '独立密码',
]) {
  assert.ok(html.includes(text), `缺少赛前指引文案：${text}`);
}
assert.ok(!html.includes('<h3>固定成员</h3>'), '玩家指引不应继续使用固定成员分类标题');
assert.ok(!html.includes('<h3>临时参赛者</h3>'), '玩家指引不应继续使用临时参赛者分类标题');
```

In `check-rules-content.cjs`, require `成员账号` and `使用邀请码加入` instead of requiring the old classification names.

- [ ] **Step 3: Run tests and observe the expected failure**

```powershell
cd web-command-center
npm run test:rules-guide
npm run test:rules-content
```

Expected: both tests fail because the public documents still use the old headings.

- [ ] **Step 4: Update the public quick guide**

Replace the first-step introduction with:

```html
<p>不用判断自己属于哪类参赛者。管理员给了你成员密码，就选择“成员账号登录”；管理员给了你本场邀请码，就选择“使用邀请码加入”。两种方式都需要先进入网页大厅，再连接 CS2。</p>
```

Use card headings `成员账号登录` and `使用邀请码加入`. Add small labels `收到成员密码` and `收到本场邀请码`, and retain the current steps, password warning and recovery command explanation.

Change the CS2 reminder sentence to:

```html
<li>使用成员账号登录时无需手动输入确认码；使用邀请码加入时按网页提示完成确认。</li>
```

- [ ] **Step 5: Update complete rules Markdown consistently**

Rename sections:

```markdown
### 2.1 成员账号登录
### 2.2 使用邀请码加入
```

Use these exact opening descriptions:

```markdown
如果管理员为你登记了 SteamID64 并设置了草人杯成员密码，请使用“成员账号登录”。该方式不需要邀请码，也不需要填写昵称；大厅昵称由管理员预设。

如果管理员给了你本场邀请码，请使用“使用邀请码加入”，并填写本场昵称和 17 位 SteamID64。
```

Apply the same terms in section `4.1 大厅与本场确认`. Keep administrator concepts, security warning, commands and behavior unchanged.

- [ ] **Step 6: Regenerate the PDF**

Run from repository root:

```powershell
& 'C:\Users\grassbl0ck\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' scripts\build-rules-pdf.py `
  --source docs\rules\caoren-cup-full-rules.md `
  --output web-command-center\public\downloads\caoren-cup-full-rules.pdf
```

Expected: `Created PDF:` followed by the repository PDF path.

- [ ] **Step 7: Run rule tests**

```powershell
cd web-command-center
npm run test:rules-content
npm run test:rules-pdf
npm run test:rules-guide
```

Expected: all three pass.

- [ ] **Step 8: Commit public documentation changes**

```powershell
git add docs/rules/caoren-cup-full-rules.md web-command-center/public/rules.html web-command-center/public/downloads/caoren-cup-full-rules.pdf web-command-center/scripts/check-rules-guide.cjs web-command-center/scripts/check-rules-content.cjs
git commit -m "docs: explain login choices by credential"
```

---

### Task 3: Browser, PDF and full regression verification

**Files:**
- No tracked source changes expected.
- Create ignored QA output under `release-build/player-login-wording-qa/`.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces: verification evidence only.

- [ ] **Step 1: Run the full relevant test suite**

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

Expected: identity tests report 56 passes and every contract test exits 0.

- [ ] **Step 2: Start an isolated local static server**

Serve `web-command-center/public` on `127.0.0.1:3102` with the bundled Python runtime. Confirm `/` and `/rules.html` return HTTP 200.

- [ ] **Step 3: Capture and inspect login screenshots**

Using Playwright with local Edge, capture login page screenshots in both themes at 360px and 1440px. Verify:

- the choice guide is visible without switching tabs;
- both credential cues are readable;
- the tab labels and primary button fit without truncation;
- no body-level horizontal overflow exists;
- administrator workspace terminology remains unchanged in source.

- [ ] **Step 4: Render and inspect the PDF**

Render all pages with Poppler `pdftoppm.exe`. Inspect the login sections and confirm the PDF contains no raw Markdown heading markers, clipping or stale player classification headings.

- [ ] **Step 5: Stop the local server and verify Git boundaries**

```powershell
git status --short --branch
git status --short --ignored
git diff --check
git diff origin/main --stat
```

Expected: only intended tracked commits differ; all backups and QA files remain ignored.

---

### Task 4: Handoff, deployment and player announcement

**Files:**
- Create when authorized: `release-output/v1.8.4-release-notes.md`
- Runtime update when authorized: website lobby announcement through `/api/admin/lobby-announcement`.

**Interfaces:**
- Consumes: verified web changes from Tasks 1–3.
- Produces: a web-only deployment and separate audience-specific release/website notes.

- [ ] **Step 1: Report implementation for user review**

Provide changed files, exact new player wording, test results, screenshots, PDF result and the statement that no deployment or GitHub publication has occurred.

- [ ] **Step 2: Wait for explicit deployment authorization**

Do not upload or change server state before the user explicitly approves deployment.

- [ ] **Step 3: Build and inspect the web package after authorization**

Build `CaorenCupWeb-网页端-v1.8.4.zip`, verify it includes updated `public/index.html`, `public/rules.html`, CSS and PDF, and excludes backups, runtime data, configuration and `node_modules`.

- [ ] **Step 4: Deploy only the web package after authorization**

Use the saved WinSCP session, upload to `/tmp`, create a timestamped server backup, overlay only the web package, restart only `caoren-cup-web`, and verify the homepage, guide and PDF over public HTTP.

- [ ] **Step 5: Update the website player announcement**

Publish a concise three-section announcement using the player terms:

```html
<h3>一、网页端</h3>
<p><strong>新增/修改内容</strong></p>
<p>登录入口现在会直接提示：收到成员密码请选择“成员账号登录”；收到本场邀请码请选择“使用邀请码加入”。</p>
<h3>二、游戏插件</h3>
<p>本次没有改变游戏内玩法和指令。</p>
<h3>三、桥接插件</h3>
<p>本次没有改变 SteamID 自动核对和本场确认流程。</p>
```

Do not include test commands, source paths or deployment details.

- [ ] **Step 6: Wait for explicit GitHub publication authorization**

If authorized, create a PR and a separate GitHub Release technical note. The GitHub note must identify the web-only upgrade scope and verification commands; it must not reuse the website player announcement verbatim.
