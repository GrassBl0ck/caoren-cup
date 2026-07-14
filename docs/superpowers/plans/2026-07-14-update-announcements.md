# 版本更新公告实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在登录前和登录后的草人杯指挥台中提供可追溯、可标记未读的版本更新公告，并让管理员通过现有“公告”模块维护草稿、已发布和隐藏公告。

**Architecture:** 新功能使用独立的 schemaVersion 1 JSON 存储、服务层、HTTP 路由和公开 Socket 投影，不修改身份库或比赛 Session。玩家端拆分为纯已读状态模块、右侧抽屉控制器和管理员编辑器；现有大厅公告保持原接口与数据不变。

**Tech Stack:** Node.js 20、TypeScript、Express、Socket.IO、原生 HTML/CSS/JavaScript、`node:test`、现有 npm 合约测试。

## Global Constraints

- 所有新增和修改文本文件使用 UTF-8。
- 修改任何已有文件前，先创建 `*.bak-20260714-update-announcements` 或任务专用的 Git 忽略备份。
- 不执行 `git pull`，不覆盖或回退用户已有修改。
- 不提交 `.superpowers/`、备份、`runtime/`、`release-build/`、`release-output/`、依赖或构建产物。
- 不修改 Identity、LobbyMembership、比赛 Session、登录流程、CS2 插件或桌面客户端。
- 不新增原生依赖；复用 Node.js、Express、Socket.IO 和现有前端技术。
- 管理员密码只出现在 POST JSON 请求体中，不进入 URL、日志、公开响应或 Socket 数据。
- 网站更新公告只写玩家需要了解的内容，不直接复制 GitHub Release notes，也不包含私人部署信息。
- 首次发布时记录 `publishedAt`；后续编辑、隐藏和重新发布不得修改它。
- 更新公告不提供永久删除操作，管理员只能保存草稿、发布、隐藏和重新发布。
- 所有公开 HTML 必须先经过服务器端白名单清洗。
- 实施、测试和截图均在本地完成；未经用户再次授权，不推送、不创建 PR、不部署、不发布 Release。

## File Map

**Create:**

- `web-command-center/src/announcement-html.ts`：大厅公告与更新公告共用的安全 HTML 清洗。
- `web-command-center/src/announcement-html.test.ts`：清洗器回归测试。
- `web-command-center/src/update-announcements/update-announcement-types.ts`：存储、公开投影、输入和错误类型。
- `web-command-center/src/update-announcements/update-announcement-seeds.ts`：v1.8.2、v1.8.3、v1.8.4 玩家版种子。
- `web-command-center/src/update-announcements/update-announcement-store.ts`：schema 校验、原子写入和 previous 恢复。
- `web-command-center/src/update-announcements/update-announcement-store.test.ts`：初始化、重启、恢复和失败写入测试。
- `web-command-center/src/update-announcements/update-announcement-service.ts`：版本唯一性、状态转换、时间与提醒修订号。
- `web-command-center/src/update-announcements/update-announcement-service.test.ts`：业务规则测试。
- `web-command-center/src/routes/update-announcement-routes.ts`：公开和管理员 HTTP 接口。
- `web-command-center/src/routes/update-announcement-routes.test.ts`：鉴权、公开投影、错误码和广播测试。
- `web-command-center/public/js/update-announcement-read-state.js`：可在浏览器和 Node 中运行的纯已读逻辑。
- `web-command-center/public/js/update-announcement-public.js`：公开抽屉、Socket 和可访问性控制。
- `web-command-center/public/js/update-announcement-admin.js`：管理员列表、编辑器和状态操作。
- `web-command-center/public/css/update-announcements.css`：双主题和响应式样式。
- `web-command-center/scripts/check-update-announcement-ui.cjs`：DOM、脚本、样式与已读逻辑合约测试。

**Modify:**

- `web-command-center/src/routes/lobby-announcement-routes.ts`：改用共用清洗器，保持原导出兼容。
- `web-command-center/src/types.ts`：增加 `UPDATE_ANNOUNCEMENTS` Socket 事件。
- `web-command-center/src/server.ts`：初始化服务、注册路由和广播公开投影。
- `web-command-center/public/index.html`：加入公共入口、抽屉、管理员编辑区和脚本/样式引用。
- `web-command-center/public/js/lobby-app.js`：管理员切换到“公告”模块时派发明确事件。
- `web-command-center/package.json`：加入两个新增测试命令。

---

### Task 1: 提取共用 HTML 清洗器

**Files:**

- Create: `web-command-center/src/announcement-html.ts`
- Create: `web-command-center/src/announcement-html.test.ts`
- Modify: `web-command-center/src/routes/lobby-announcement-routes.ts`

**Interfaces:**

- Produces: `sanitizeAnnouncementHtml(rawHtml: unknown, maximumLength?: number): string`
- Preserves: `sanitizeLobbyAnnouncementHtml(rawHtml: unknown): string`

- [ ] **Step 1: 备份现有大厅公告路由**

Run:

```powershell
Copy-Item web-command-center/src/routes/lobby-announcement-routes.ts web-command-center/src/routes/lobby-announcement-routes.ts.bak-20260714-update-announcements
```

Expected: 备份文件存在，并被 Git 忽略。

- [ ] **Step 2: 写清洗器失败测试**

Create `web-command-center/src/announcement-html.test.ts`:

```ts
import assert from 'node:assert/strict';
import test from 'node:test';
import { sanitizeAnnouncementHtml } from './announcement-html';

test('announcement HTML keeps safe formatting and removes executable content', () => {
    const result = sanitizeAnnouncementHtml(
        '<script>alert(1)</script><p onclick="bad()"><strong>安全内容</strong></p>',
    );
    assert.equal(result, '<p><strong>安全内容</strong></p>');
});

test('announcement links allow safe protocols and reject javascript URLs', () => {
    assert.equal(
        sanitizeAnnouncementHtml('<a href="https://example.com">规则</a>'),
        '<a href="https://example.com" target="_blank" rel="noopener noreferrer">规则</a>',
    );
    assert.equal(sanitizeAnnouncementHtml('<a href="javascript:alert(1)">危险</a>'), '<a>危险</a>');
});

test('announcement HTML applies the requested length limit before sanitizing', () => {
    assert.equal(sanitizeAnnouncementHtml('<p>abcdef</p>', 5), '<p>ab');
});
```

- [ ] **Step 3: 运行测试并确认失败**

Run:

```powershell
cd web-command-center
npx tsx --test src/announcement-html.test.ts
```

Expected: FAIL，提示找不到 `./announcement-html`。

- [ ] **Step 4: 创建共用清洗器并保持大厅公告兼容**

Create `web-command-center/src/announcement-html.ts` with the existing allowed-tag policy moved out of the route:

```ts
const DEFAULT_MAXIMUM_LENGTH = 12_000;

const ALLOWED_TAGS = new Set([
    'a', 'b', 'blockquote', 'br', 'code', 'div', 'em', 'h2', 'h3', 'hr', 'i',
    'li', 'ol', 'p', 'pre', 's', 'span', 'strong', 'u', 'ul',
]);

const escapeText = (value: unknown) => String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

const sanitizeUrl = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed) return '';
    if (/^(https?:|mailto:)/i.test(trimmed)) return escapeText(trimmed);
    if (/^[#/]/.test(trimmed)) return escapeText(trimmed);
    return '';
};

export const sanitizeAnnouncementHtml = (
    rawHtml: unknown,
    maximumLength = DEFAULT_MAXIMUM_LENGTH,
) => {
    let html = String(rawHtml ?? '').slice(0, maximumLength);
    html = html
        .replace(/<!--[\s\S]*?-->/g, '')
        .replace(/<\s*(script|style|iframe|object|embed|meta|link)[^>]*>[\s\S]*?<\s*\/\s*\1\s*>/gi, '')
        .replace(/<\s*\/?\s*(script|style|iframe|object|embed|meta|link)[^>]*>/gi, '');

    return html.replace(/<[^>]*>/g, (tag) => {
        const match = tag.match(/^<\s*(\/?)\s*([a-zA-Z0-9]+)([^>]*)>/);
        if (!match) return '';
        const isClosing = match[1] === '/';
        const tagName = match[2].toLowerCase();
        const attributes = match[3] || '';
        if (!ALLOWED_TAGS.has(tagName)) return '';
        if (isClosing) return tagName === 'br' || tagName === 'hr' ? '' : `</${tagName}>`;
        if (tagName === 'br' || tagName === 'hr') return `<${tagName}>`;
        if (tagName === 'a') {
            const href = attributes.match(/\shref\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))/i);
            const safeHref = sanitizeUrl(href?.[1] || href?.[2] || href?.[3] || '');
            return safeHref
                ? `<a href="${safeHref}" target="_blank" rel="noopener noreferrer">`
                : '<a>';
        }
        return `<${tagName}>`;
    });
};
```

In `lobby-announcement-routes.ts`, remove its duplicated tag/URL sanitizer and add:

```ts
import { sanitizeAnnouncementHtml } from '../announcement-html';

export const sanitizeLobbyAnnouncementHtml = (rawHtml: unknown) =>
  sanitizeAnnouncementHtml(rawHtml, MAX_HTML_LENGTH);
```

- [ ] **Step 5: 运行新测试和大厅公告相关类型检查**

Run:

```powershell
npx tsx --test src/announcement-html.test.ts
npm run typecheck
```

Expected: 两条命令均退出码 0。

- [ ] **Step 6: 提交**

```powershell
git add web-command-center/src/announcement-html.ts web-command-center/src/announcement-html.test.ts web-command-center/src/routes/lobby-announcement-routes.ts
git commit -m "refactor: share announcement HTML sanitizer"
```

---

### Task 2: 实现种子与安全存储

**Files:**

- Create: `web-command-center/src/update-announcements/update-announcement-types.ts`
- Create: `web-command-center/src/update-announcements/update-announcement-seeds.ts`
- Create: `web-command-center/src/update-announcements/update-announcement-store.ts`
- Create: `web-command-center/src/update-announcements/update-announcement-store.test.ts`

**Interfaces:**

- Produces: `UpdateAnnouncementStore`, `createSeedUpdateAnnouncementData()` and all update-announcement data types.
- `UpdateAnnouncementStore.load(): Promise<void>` initializes or restores the store.
- `UpdateAnnouncementStore.snapshot(): UpdateAnnouncementStoreData` returns a deep clone.
- `UpdateAnnouncementStore.mutate<T>(mutator): Promise<T>` atomically persists a cloned draft.

- [ ] **Step 1: 写种子、重启、previous 和未知 schema 的失败测试**

Create `update-announcement-store.test.ts` with a temporary directory helper and these exact assertions:

```ts
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { UpdateAnnouncementStore } from './update-announcement-store';

const makeDir = (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `update-announcement-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    return dir;
};

test('a missing store is seeded once with v1.8.2 through v1.8.4', async (t) => {
    const dir = makeDir('seed');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    const first = new UpdateAnnouncementStore(file);
    await first.load();
    assert.deepEqual(
        Object.values(first.snapshot().announcements).map((item) => item.version).sort(),
        ['v1.8.2', 'v1.8.3', 'v1.8.4'],
    );
    const seeded = Object.fromEntries(Object.values(first.snapshot().announcements).map((item) => [item.version, item]));
    assert.equal(seeded['v1.8.2'].publishedAt, Date.parse('2026-07-12T19:25:21Z'));
    assert.equal(seeded['v1.8.3'].publishedAt, Date.parse('2026-07-13T18:24:55Z'));
    assert.equal(seeded['v1.8.4'].publishedAt, Date.parse('2026-07-13T20:10:18Z'));
    assert.equal(seeded['v1.8.4'].status, 'published');
    assert.equal(seeded['v1.8.4'].reminderRevision, 1);
    assert.equal(seeded['v1.8.4'].createdAt, seeded['v1.8.4'].publishedAt);
    assert.equal(seeded['v1.8.4'].updatedAt, seeded['v1.8.4'].publishedAt);
    const original = fs.readFileSync(file, 'utf8');
    const second = new UpdateAnnouncementStore(file);
    await second.load();
    assert.equal(fs.readFileSync(file, 'utf8'), original);
});

test('a corrupt primary restores a valid previous copy', async (t) => {
    const dir = makeDir('previous');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    const store = new UpdateAnnouncementStore(file);
    await store.load();
    fs.copyFileSync(file, path.join(dir, 'update-announcements.previous.json'));
    fs.writeFileSync(file, '{broken', 'utf8');
    const restored = new UpdateAnnouncementStore(file);
    await restored.load();
    assert.equal(Object.keys(restored.snapshot().announcements).length, 3);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 1);
});

test('an unknown schema is preserved and never downgraded from previous', async (t) => {
    const dir = makeDir('unknown');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99, announcements: {} }), 'utf8');
    fs.writeFileSync(path.join(dir, 'update-announcements.previous.json'), JSON.stringify({ schemaVersion: 1, announcements: {} }), 'utf8');
    await assert.rejects(new UpdateAnnouncementStore(file).load(), /unsupported/);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 99);
});

test('two corrupt copies fail without replacing either file', async (t) => {
    const dir = makeDir('both-corrupt');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    const previous = path.join(dir, 'update-announcements.previous.json');
    fs.writeFileSync(file, '{primary-broken', 'utf8');
    fs.writeFileSync(previous, '{previous-broken', 'utf8');
    await assert.rejects(new UpdateAnnouncementStore(file).load());
    assert.equal(fs.readFileSync(file, 'utf8'), '{primary-broken');
    assert.equal(fs.readFileSync(previous, 'utf8'), '{previous-broken');
});
```

Add the failed-write recovery test:

```ts
test('a failed write keeps memory unchanged and a later write can recover', async (t) => {
    const dir = makeDir('write-failure');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    let failRename = false;
    const controlledFs = new Proxy(fs, {
        get(target, property, receiver) {
            if (property === 'renameSync') {
                return (...args: Parameters<typeof fs.renameSync>) => {
                    if (failRename) throw new Error('simulated rename failure');
                    return fs.renameSync(...args);
                };
            }
            return Reflect.get(target, property, receiver);
        },
    });
    const store = new UpdateAnnouncementStore(
        path.join(dir, 'update-announcements.json'),
        { fs: controlledFs },
    );
    await store.load();
    const id = '00000000-0000-4000-8000-000000001804';
    const before = store.snapshot();

    failRename = true;
    await assert.rejects(store.mutate((draft) => {
        draft.announcements[id].title = '不应进入内存';
    }), /simulated rename failure/);
    assert.deepEqual(store.snapshot(), before);

    failRename = false;
    await store.mutate((draft) => {
        draft.announcements[id].title = '恢复成功';
    });
    assert.equal(store.snapshot().announcements[id].title, '恢复成功');
});
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
cd web-command-center
npx tsx --test src/update-announcements/update-announcement-store.test.ts
```

Expected: FAIL，提示存储模块不存在。

- [ ] **Step 3: 定义精确类型和错误类型**

Create `update-announcement-types.ts`:

```ts
export type UpdateAnnouncementStatus = 'draft' | 'published' | 'hidden';

export interface UpdateAnnouncementSections {
    webHtml: string;
    gamePluginHtml: string;
    bridgePluginHtml: string;
}

export interface UpdateAnnouncement {
    id: string;
    version: string;
    title: string;
    sections: UpdateAnnouncementSections;
    status: UpdateAnnouncementStatus;
    reminderRevision: number;
    createdAt: number;
    updatedAt: number;
    publishedAt: number | null;
}

export interface UpdateAnnouncementStoreData {
    schemaVersion: 1;
    announcements: Record<string, UpdateAnnouncement>;
}

export interface PublicUpdateAnnouncement {
    id: string;
    version: string;
    title: string;
    sections: UpdateAnnouncementSections;
    reminderRevision: number;
    publishedAt: number;
}

export class UnsupportedUpdateAnnouncementSchemaError extends Error {
    constructor() {
        super('update announcement schema is unsupported');
        this.name = 'UnsupportedUpdateAnnouncementSchemaError';
    }
}

export class UpdateAnnouncementUnavailableError extends Error {
    constructor() {
        super('更新公告暂时无法读取');
        this.name = 'UpdateAnnouncementUnavailableError';
    }
}
```

- [ ] **Step 4: 创建精确历史种子**

Create `update-announcement-seeds.ts`. Use valid stable UUIDs, `published` status, revision `1`, and set all three timestamps to the Release time:

```ts
import { UpdateAnnouncement, UpdateAnnouncementStoreData } from './update-announcement-types';

const noChange = '<p>本版本无玩家可见更新</p>';

const seed = (
    id: string,
    version: string,
    title: string,
    publishedAt: number,
    webHtml: string,
    gamePluginHtml: string,
    bridgePluginHtml: string,
): UpdateAnnouncement => ({
    id,
    version,
    title,
    sections: { webHtml, gamePluginHtml, bridgePluginHtml },
    status: 'published',
    reminderRevision: 1,
    createdAt: publishedAt,
    updatedAt: publishedAt,
    publishedAt,
});

export const createSeedUpdateAnnouncementData = (): UpdateAnnouncementStoreData => {
    const items = [
        seed(
            '00000000-0000-4000-8000-000000001802',
            'v1.8.2',
            '新增成员账号登录与大厅界面优化',
            Date.parse('2026-07-12T19:25:21Z'),
            '<ul><li>新增“成员账号登录”，持有管理员预设密码的玩家无需邀请码和昵称即可进入大厅。</li><li>保留“使用邀请码加入”方式。</li><li>更新大厅布局和深浅色适配。</li></ul>',
            noChange,
            '<ul><li>游戏 SteamID 与成员账号一致时自动完成本场确认。</li><li>未先进入网页大厅的真实玩家会收到提示。</li></ul>',
        ),
        seed(
            '00000000-0000-4000-8000-000000001803',
            'v1.8.3',
            '新增开赛前快速指引',
            Date.parse('2026-07-13T18:24:55Z'),
            '<p>新增登录前和登录后都能查看的开赛前快速指引，并提供完整规则 PDF 下载。</p>',
            noChange,
            noChange,
        ),
        seed(
            '00000000-0000-4000-8000-000000001804',
            'v1.8.4',
            '登录入口说明更加清楚',
            Date.parse('2026-07-13T20:10:18Z'),
            '<p>玩家现在可以按“收到成员密码”或“收到本场邀请码”选择入口，并能一直看到登录方式说明。</p>',
            noChange,
            noChange,
        ),
    ];
    return {
        schemaVersion: 1,
        announcements: Object.fromEntries(items.map((item) => [item.id, item])),
    };
};
```

- [ ] **Step 5: 实现安全存储**

Create `update-announcement-store.ts` following the identity-store write queue, with these required branches:

```ts
const parseData = (text: string): UpdateAnnouncementStoreData => {
    const parsed: unknown = JSON.parse(text);
    if (!isRecord(parsed)) throw new Error('update announcement store schema is invalid');
    if (parsed.schemaVersion !== 1) throw new UnsupportedUpdateAnnouncementSchemaError();
    if (!isRecord(parsed.announcements)) throw new Error('update announcement store schema is invalid');
    for (const [id, value] of Object.entries(parsed.announcements)) {
        validateAnnouncement(id, value);
    }
    return parsed as unknown as UpdateAnnouncementStoreData;
};

async load(): Promise<void> {
    const primaryExists = this.fileSystem.existsSync(this.filePath);
    const previousExists = this.fileSystem.existsSync(this.previousPath);
    if (!primaryExists && !previousExists) {
        this.data = createSeedUpdateAnnouncementData();
        await this.persist(this.data, false);
        return;
    }
    if (primaryExists) {
        try {
            this.data = parseData(this.fileSystem.readFileSync(this.filePath, 'utf8'));
            return;
        } catch (error) {
            if (error instanceof UnsupportedUpdateAnnouncementSchemaError || !previousExists) throw error;
        }
    }
    this.data = parseData(this.fileSystem.readFileSync(this.previousPath, 'utf8'));
    await this.persist(this.data, false);
}
```

`validateAnnouncement` must apply the following complete predicate before accepting a record:

```ts
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const VERSION_PATTERN = /^v\d+\.\d+\.\d+$/;
const STATUSES = new Set(['draft', 'published', 'hidden']);

const validateAnnouncement = (recordId: string, value: unknown): void => {
    if (!isRecord(value)
        || value.id !== recordId
        || typeof value.id !== 'string'
        || !UUID_PATTERN.test(value.id)
        || typeof value.version !== 'string'
        || !VERSION_PATTERN.test(value.version)
        || typeof value.title !== 'string'
        || !isRecord(value.sections)
        || typeof value.sections.webHtml !== 'string'
        || typeof value.sections.gamePluginHtml !== 'string'
        || typeof value.sections.bridgePluginHtml !== 'string'
        || typeof value.status !== 'string'
        || !STATUSES.has(value.status)
        || !Number.isInteger(value.reminderRevision)
        || Number(value.reminderRevision) < 0
        || typeof value.createdAt !== 'number'
        || !Number.isFinite(value.createdAt)
        || typeof value.updatedAt !== 'number'
        || !Number.isFinite(value.updatedAt)
        || (value.publishedAt !== null
            && (typeof value.publishedAt !== 'number' || !Number.isFinite(value.publishedAt)))) {
        throw new Error('update announcement store schema is invalid');
    }
    if ((value.status === 'draft' && (value.publishedAt !== null || value.reminderRevision !== 0))
        || (value.status !== 'draft' && (value.publishedAt === null || value.reminderRevision < 1))) {
        throw new Error('update announcement store schema is invalid');
    }
};
```

After validating records, reject duplicate persisted versions with a `Set<string>`. `persist` must write a same-directory temp file, parse the current primary before copying it to previous, rename the temp file, and remove leftovers in `finally`.

- [ ] **Step 6: 运行存储测试**

Run:

```powershell
npx tsx --test src/update-announcements/update-announcement-store.test.ts
```

Expected: 所有存储测试 PASS，测试临时目录在结束时清理。

- [ ] **Step 7: 提交**

```powershell
git add web-command-center/src/update-announcements
git commit -m "feat: add update announcement storage"
```

---

### Task 3: 实现公告业务生命周期

**Files:**

- Create: `web-command-center/src/update-announcements/update-announcement-service.ts`
- Create: `web-command-center/src/update-announcements/update-announcement-service.test.ts`
- Modify: `web-command-center/src/update-announcements/update-announcement-types.ts`

**Interfaces:**

- Consumes: `UpdateAnnouncementStore`, `sanitizeAnnouncementHtml`.
- Produces: `UpdateAnnouncementService.initialize()`, `isAvailable()`, `listPublic()`, `listAdmin()`, `saveAnnouncement(input)`, `setStatus(input)`.
- Mutation methods return `{ announcement, publicChanged }`.

- [ ] **Step 1: 写业务规则失败测试**

Use a real temporary store and this fixture so every assertion uses an exact clock and UUID:

```ts
const makeService = async (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `update-service-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    let now = 2_000_000_000_000;
    let sequence = 0;
    const store = new UpdateAnnouncementStore(path.join(dir, 'update-announcements.json'));
    const service = new UpdateAnnouncementService(store, {
        now: () => now,
        idFactory: () => `11111111-1111-4111-8111-${String(++sequence).padStart(12, '0')}`,
        logger: { warn: () => undefined },
    });
    await service.initialize();
    return { dir, service, setNow: (value: number) => { now = value; } };
};

const emptySections = { webHtml: '', gamePluginHtml: '', bridgePluginHtml: '' };

test('format uniqueness and empty publish rules are enforced', async (t) => {
    const runtime = await makeService('validation');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    await assert.rejects(runtime.service.saveAnnouncement({ version: '1.9.0', title: '错误', sections: emptySections }), /版本号格式/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.8.4', title: '重复', sections: emptySections }), /已存在/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.2', title: '', sections: { webHtml: '内容' } }), /标题/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.2', title: '超长标题'.repeat(11), sections: { webHtml: '内容' } }), /40/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.2', title: '内容过长', sections: { webHtml: 'x'.repeat(12_001) } }), /过长/);
    const emptyDraft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '空草稿', sections: emptySections });
    assert.equal(emptyDraft.announcement.status, 'draft');
    await assert.rejects(runtime.service.setStatus({ id: emptyDraft.announcement.id, status: 'published' }), /至少填写一个/);

    const occupied = await runtime.service.saveAnnouncement({ version: 'v1.9.1', title: '占用版本', sections: { webHtml: '内容' } });
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.1', title: '重复草稿', sections: { webHtml: '内容' } }), /已存在/);
    await runtime.service.setStatus({ id: occupied.announcement.id, status: 'published' });
    await runtime.service.setStatus({ id: occupied.announcement.id, status: 'hidden' });
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.1', title: '重复隐藏', sections: { webHtml: '内容' } }), /已存在/);
});

test('first publish sets time once and normal edits do not remind again', async (t) => {
    const runtime = await makeService('publish');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({
        version: 'v1.9.0', title: '新版本', sections: { webHtml: '<p>内容</p>' },
    });
    const published = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    assert.equal(published.announcement.publishedAt, 2_000_000_000_000);
    assert.equal(published.announcement.reminderRevision, 1);
    runtime.setNow(2_000_000_001_000);
    const edited = await runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.0', title: '修改内容',
        sections: { webHtml: '<p>新内容</p>' },
    });
    assert.equal(edited.announcement.publishedAt, 2_000_000_000_000);
    assert.equal(edited.announcement.reminderRevision, 1);
    const reminded = await runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.0', title: '再次提醒',
        sections: { webHtml: '<p>新内容</p>' }, remindAgain: true,
    });
    assert.equal(reminded.announcement.reminderRevision, 2);
});

test('published version correction requires confirmation and reminds exactly once', async (t) => {
    const runtime = await makeService('version-change');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '版本', sections: { webHtml: '内容' } });
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    await assert.rejects(runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.1', title: '版本', sections: { webHtml: '内容' },
    }), /二次确认/);
    const corrected = await runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.1', title: '版本', sections: { webHtml: '内容' },
        confirmVersionChange: true,
    });
    assert.equal(corrected.announcement.version, 'v1.9.1');
    assert.equal(corrected.announcement.reminderRevision, 2);
});

test('hide and republish preserve time and optionally remind', async (t) => {
    const runtime = await makeService('republish');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '版本', sections: { webHtml: '内容' } });
    const published = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'hidden' });
    const republished = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    assert.equal(republished.announcement.publishedAt, published.announcement.publishedAt);
    assert.equal(republished.announcement.reminderRevision, 1);
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'hidden' });
    const reminded = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published', remindAgain: true });
    assert.equal(reminded.announcement.reminderRevision, 2);
});

test('public projection hides private states and supplies all three sections', async (t) => {
    const runtime = await makeService('projection');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '版本', sections: { webHtml: '<p>公开</p>' } });
    assert.equal(runtime.service.listPublic().some((item) => item.id === draft.announcement.id), false);
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    const item = runtime.service.listPublic().find((entry) => entry.id === draft.announcement.id);
    assert.equal(item?.sections.gamePluginHtml, '<p>本版本无玩家可见更新</p>');
    assert.equal(item?.sections.bridgePluginHtml, '<p>本版本无玩家可见更新</p>');
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'hidden' });
    assert.equal(runtime.service.listPublic().some((entry) => entry.id === draft.announcement.id), false);
});

test('initialization failure preserves the unknown file and disables service methods', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `update-service-unavailable-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99, announcements: {} }), 'utf8');
    const service = new UpdateAnnouncementService(new UpdateAnnouncementStore(file), { logger: { warn: () => undefined } });
    await service.initialize();
    assert.throws(() => service.listPublic(), /暂时无法读取/);
    await assert.rejects(service.saveAnnouncement({ version: 'v2.0.0', title: '不可写', sections: emptySections }), /暂时无法读取/);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 99);
});
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
cd web-command-center
npx tsx --test src/update-announcements/update-announcement-service.test.ts
```

Expected: FAIL，提示服务模块或方法不存在。

- [ ] **Step 3: 补充输入、错误和结果类型**

Add to `update-announcement-types.ts`:

```ts
export interface SaveUpdateAnnouncementInput {
    id?: string;
    version: string;
    title: string;
    sections: Partial<UpdateAnnouncementSections>;
    remindAgain?: boolean;
    confirmVersionChange?: boolean;
}

export interface SetUpdateAnnouncementStatusInput {
    id: string;
    status: 'published' | 'hidden';
    remindAgain?: boolean;
}

export interface UpdateAnnouncementMutationResult {
    announcement: UpdateAnnouncement;
    publicChanged: boolean;
}

export type UpdateAnnouncementValidationCode =
    | 'not_found'
    | 'version_invalid'
    | 'version_duplicate'
    | 'title_required'
    | 'title_too_long'
    | 'content_too_long'
    | 'empty_publish'
    | 'version_change_confirmation_required'
    | 'status_transition_invalid';

export class UpdateAnnouncementValidationError extends Error {
    constructor(public readonly code: UpdateAnnouncementValidationCode, message: string) {
        super(message);
        this.name = 'UpdateAnnouncementValidationError';
    }
}
```

- [ ] **Step 4: 实现服务规则**

Use these exact constants and helpers:

```ts
const VERSION_PATTERN = /^v\d+\.\d+\.\d+$/;
const MAX_TITLE_CHARACTERS = 40;
const MAX_SECTION_HTML_LENGTH = 12_000;
const EMPTY_SECTION_HTML = '<p>本版本无玩家可见更新</p>';

const hasMeaningfulContent = (html: string) => html
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;|&#160;/gi, ' ')
    .trim().length > 0;
```

The constructor must accept:

```ts
constructor(
    private readonly store: UpdateAnnouncementStore,
    private readonly options: {
        now?: () => number;
        idFactory?: () => string;
        logger?: Pick<Console, 'warn'>;
    } = {},
) {}
```

Use `randomUUID` from `node:crypto` when `options.idFactory` is not provided, use `Date.now` when `options.now` is absent, and default the logger to `console`.

`initialize()` catches load errors, logs only a generic message, and records unavailable state without throwing. `isAvailable()` returns that state without exposing the error. Every data or mutation method calls `requireAvailable()`.

`saveAnnouncement()` must:

1. trim and validate version;
2. count title with `Array.from(title).length`;
3. reject any raw section over 12,000 characters;
4. sanitize all sections;
5. create new records only as draft with revision `0`;
6. preserve status and `publishedAt` when editing;
7. require `confirmVersionChange` when a previously published record changes version;
8. increment revision exactly once when version changes or `remindAgain` is true;
9. set `publicChanged` when the previous or resulting status is published.

`setStatus()` must reject missing records and invalid transitions, require meaningful content before publishing, set first publication time and revision `1`, preserve that time on later transitions, and only increment revision on republish when `remindAgain` is true.

`listPublic()` must replace empty section strings with `EMPTY_SECTION_HTML`, omit non-public timestamps/status, and sort by `publishedAt` descending with semantic version descending as the deterministic tie-breaker.

- [ ] **Step 5: 运行业务和存储测试**

Run:

```powershell
npx tsx --test src/update-announcements/*.test.ts src/announcement-html.test.ts
```

Expected: 所有测试 PASS。

- [ ] **Step 6: 提交**

```powershell
git add web-command-center/src/update-announcements web-command-center/src/announcement-html.test.ts
git commit -m "feat: add update announcement lifecycle"
```

---

### Task 4: 注册 HTTP API 与 Socket 同步

**Files:**

- Create: `web-command-center/src/routes/update-announcement-routes.ts`
- Create: `web-command-center/src/routes/update-announcement-routes.test.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/server.ts`
- Modify: `web-command-center/package.json`

**Interfaces:**

- Consumes: `UpdateAnnouncementService`.
- Produces: four routes and `WsEvents.UPDATE_ANNOUNCEMENTS`.
- Public Socket payload: `{ announcements: PublicUpdateAnnouncement[] }`.

- [ ] **Step 1: 备份服务器入口、事件类型和 package.json**

```powershell
Copy-Item web-command-center/src/server.ts web-command-center/src/server.ts.bak-20260714-update-announcements
Copy-Item web-command-center/src/types.ts web-command-center/src/types.ts.bak-20260714-update-announcements
Copy-Item web-command-center/package.json web-command-center/package.json.bak-20260714-update-announcements
```

- [ ] **Step 2: 写真实 HTTP 路由失败测试**

In `update-announcement-routes.test.ts`, start an Express app on port `0` with JSON middleware, a temporary initialized service, admin password `admin-test-password`, and a captured broadcast array. Assert:

```ts
const publicResponse = await fetch(`${baseUrl}/api/update-announcements`);
assert.equal(publicResponse.status, 200);
assert.deepEqual((await publicResponse.json()).announcements.map((item: any) => item.version), ['v1.8.4', 'v1.8.3', 'v1.8.2']);

const unauthorized = await fetch(`${baseUrl}/api/admin/update-announcements/list`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ adminPassword: 'wrong' }),
});
assert.equal(unauthorized.status, 401);
```

Use this request helper and complete lifecycle in the same initialized fixture:

```ts
const post = async (route: string, body: Record<string, unknown>) => {
    const response = await fetch(baseUrl + route, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ adminPassword: 'admin-test-password', ...body }),
    });
    return { response, body: await response.json() };
};

const created = await post('/api/admin/update-announcements/save', {
    announcement: {
        version: 'v1.9.0',
        title: '路由测试',
        sections: { webHtml: '<script>bad()</script><p>安全</p>' },
    },
});
assert.equal(created.response.status, 200);
assert.equal(created.body.announcement.status, 'draft');

const duplicate = await post('/api/admin/update-announcements/save', {
    announcement: { version: 'v1.9.0', title: '重复', sections: { webHtml: '内容' } },
});
assert.equal(duplicate.response.status, 409);

const empty = await post('/api/admin/update-announcements/save', {
    announcement: { version: 'v1.9.1', title: '空公告', sections: {} },
});
const emptyPublish = await post('/api/admin/update-announcements/status', {
    id: empty.body.announcement.id,
    status: 'published',
});
assert.equal(emptyPublish.response.status, 400);

const published = await post('/api/admin/update-announcements/status', {
    id: created.body.announcement.id,
    status: 'published',
});
assert.equal(published.response.status, 200);
const afterPublish = await fetch(baseUrl + '/api/update-announcements').then((response) => response.json());
const publicItem = afterPublish.announcements.find((item: any) => item.id === created.body.announcement.id);
assert.equal(publicItem.sections.webHtml, '<p>安全</p>');
assert.equal('status' in publicItem, false);

const hidden = await post('/api/admin/update-announcements/status', {
    id: created.body.announcement.id,
    status: 'hidden',
});
assert.equal(hidden.response.status, 200);
const afterHide = await fetch(baseUrl + '/api/update-announcements').then((response) => response.json());
assert.equal(afterHide.announcements.some((item: any) => item.id === created.body.announcement.id), false);

assert.ok(broadcasts.length >= 2);
assert.equal(JSON.stringify(broadcasts).includes('admin-test-password'), false);
assert.equal(JSON.stringify(broadcasts).includes('空公告'), false);
```

For the unavailable case, initialize a second service with a schemaVersion `99` file, register it on a second ephemeral Express server, call the public GET route, assert status `503` and `{ success: false, error: '更新公告暂时无法读取' }`, then close both servers and remove both temporary directories in `t.after()`.

- [ ] **Step 3: 运行路由测试并确认失败**

Run:

```powershell
cd web-command-center
npx tsx --test src/routes/update-announcement-routes.test.ts
```

Expected: FAIL，提示路由模块不存在。

- [ ] **Step 4: 实现路由及错误映射**

Create `update-announcement-routes.ts` with this dependency boundary:

```ts
interface RegisterUpdateAnnouncementRoutesDeps {
    adminPassword?: string;
    service: UpdateAnnouncementService;
    broadcastPublic: (announcements: PublicUpdateAnnouncement[]) => void;
}

export function registerUpdateAnnouncementRoutes(
    app: Express,
    deps: RegisterUpdateAnnouncementRoutesDeps,
): void;
```

Register:

```ts
app.get('/api/update-announcements', publicListHandler);
app.post('/api/admin/update-announcements/list', requireAdmin, adminListHandler);
app.post('/api/admin/update-announcements/save', requireAdmin, saveHandler);
app.post('/api/admin/update-announcements/status', requireAdmin, statusHandler);
```

The handlers use these exact service calls and response keys:

```ts
const publicListHandler: RequestHandler = (_req, res) => {
    try {
        res.json({ success: true, announcements: deps.service.listPublic() });
    } catch (error) {
        sendUpdateAnnouncementError(res, error);
    }
};

const adminListHandler: RequestHandler = (_req, res) => {
    try {
        res.json({ success: true, announcements: deps.service.listAdmin() });
    } catch (error) {
        sendUpdateAnnouncementError(res, error);
    }
};

const saveHandler: RequestHandler = async (req, res) => {
    try {
        const result = await deps.service.saveAnnouncement(req.body?.announcement || {});
        if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
        res.json({ success: true, announcement: result.announcement });
    } catch (error) {
        sendUpdateAnnouncementError(res, error);
    }
};

const statusHandler: RequestHandler = async (req, res) => {
    try {
        const result = await deps.service.setStatus({
            id: String(req.body?.id || ''),
            status: req.body?.status,
            remindAgain: req.body?.remindAgain === true,
        });
        if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
        res.json({ success: true, announcement: result.announcement });
    } catch (error) {
        sendUpdateAnnouncementError(res, error);
    }
};
```

Map `version_duplicate` to `409`, other validation errors to `400`, unavailable errors to `503`, wrong passwords to `401`, and unexpected errors to a generic `500` message. Never return `error.stack` or file paths. After a mutation, call `broadcastPublic(service.listPublic())` only when `publicChanged` is true.

- [ ] **Step 5: 接入服务器和 Socket**

Add to `WsEvents`:

```ts
UPDATE_ANNOUNCEMENTS = 'UPDATE_ANNOUNCEMENTS',
```

In `server.ts`, create the store path and service once:

```ts
const updateAnnouncementStore = new UpdateAnnouncementStore(
    process.env.UPDATE_ANNOUNCEMENT_STORE_PATH
        || path.resolve(__dirname, '..', 'runtime', 'update-announcements.json'),
);
const updateAnnouncementService = new UpdateAnnouncementService(updateAnnouncementStore);

const broadcastUpdateAnnouncements = (announcements: PublicUpdateAnnouncement[]) => {
    io.emit(WsEvents.UPDATE_ANNOUNCEMENTS, { announcements });
};
```

Add `import path from 'node:path'` and imports for the store, service, public type and route registrar. In the existing Socket connection callback, append:

```ts
if (updateAnnouncementService.isAvailable()) {
    socket.emit(WsEvents.UPDATE_ANNOUNCEMENTS, {
        announcements: updateAnnouncementService.listPublic(),
    });
}
```

Register the routes with `ADMIN_PASSWORD`. On every Socket connection, emit the current public list only if the service is available. Change startup to await both initialization calls while allowing `updateAnnouncementService.initialize()` to resolve in unavailable mode:

```ts
Promise.all([
    initializeIdentityRuntime(),
    updateAnnouncementService.initialize(),
])
    .then(() => httpServer.listen(PORT, () => console.log(`草人杯指挥台已启动: http://localhost:${PORT}`)))
    .catch((error) => {
        console.error('[Startup] 核心服务初始化失败，服务未启动：', error);
        process.exitCode = 1;
    });
```

- [ ] **Step 6: 添加后端测试命令**

Add to `package.json` scripts:

```json
"test:update-announcements": "tsx --test src/announcement-html.test.ts src/update-announcements/*.test.ts src/routes/update-announcement-routes.test.ts"
```

- [ ] **Step 7: 运行后端测试和类型检查**

Run:

```powershell
npm run test:update-announcements
npm run typecheck
```

Expected: 两条命令均退出码 0。

- [ ] **Step 8: 提交**

```powershell
git add web-command-center/src/routes/update-announcement-routes.ts web-command-center/src/routes/update-announcement-routes.test.ts web-command-center/src/types.ts web-command-center/src/server.ts web-command-center/package.json
git commit -m "feat: expose update announcement APIs"
```

---

### Task 5: 建立 UI 合约、抽屉骨架和纯已读逻辑

**Files:**

- Create: `web-command-center/public/js/update-announcement-read-state.js`
- Create: `web-command-center/public/css/update-announcements.css`
- Create: `web-command-center/scripts/check-update-announcement-ui.cjs`
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/package.json`

**Interfaces:**

- Produces browser/Node API `CaorenUpdateAnnouncementReadState` with `parse`, `findUnread`, and `markRead`.
- Produces stable public and admin DOM IDs consumed by Tasks 6 and 7.

- [ ] **Step 1: 备份 index.html 和 package.json**

```powershell
Copy-Item web-command-center/public/index.html web-command-center/public/index.html.bak-20260714-update-announcement-ui
Copy-Item web-command-center/package.json web-command-center/package.json.bak-20260714-update-announcement-ui
```

- [ ] **Step 2: 写 UI 合约和已读逻辑失败测试**

Create `check-update-announcement-ui.cjs`. Read index, CSS and JS files; require the read-state module. Require these IDs:

```js
const requiredIds = [
  'update-announcement-trigger',
  'update-announcement-unread-dot',
  'update-announcement-backdrop',
  'update-announcement-drawer',
  'update-announcement-close',
  'update-announcement-status',
  'update-announcement-list',
  'update-announcement-admin-panel',
  'update-announcement-admin-status',
  'update-announcement-admin-list',
  'update-announcement-editor-form',
  'update-announcement-id-input',
  'update-announcement-version-input',
  'update-announcement-title-input',
  'update-announcement-web-editor',
  'update-announcement-game-plugin-editor',
  'update-announcement-bridge-plugin-editor',
  'update-announcement-remind-again',
];
```

Assert the trigger appears before `id="login-area"`, the drawer has `role="dialog"`, and the three fixed section labels exist. Test pure logic:

```js
const readState = require('../public/js/update-announcement-read-state.js');
const announcements = [
  { id: 'a', reminderRevision: 1 },
  { id: 'b', reminderRevision: 2 },
];
assert.deepEqual(readState.findUnread(announcements, { a: 1 }), ['b']);
assert.deepEqual(readState.markRead({ a: 1 }, announcements), { a: 1, b: 2 });
assert.deepEqual(readState.parse('{"a":2}'), { a: 2 });
assert.deepEqual(readState.parse('{broken'), {});
```

- [ ] **Step 3: 运行合约测试并确认失败**

Run:

```powershell
cd web-command-center
node scripts/check-update-announcement-ui.cjs
```

Expected: FAIL，首先报告缺少公告入口或已读模块。

- [ ] **Step 4: 实现 UMD 纯已读模块**

Create `update-announcement-read-state.js`:

```js
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.CaorenUpdateAnnouncementReadState = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
    function parse(raw) {
        try {
            const value = JSON.parse(String(raw || '{}'));
            if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
            return Object.fromEntries(Object.entries(value).filter(function (entry) {
                return typeof entry[0] === 'string'
                    && Number.isInteger(entry[1])
                    && entry[1] >= 0;
            }));
        } catch (_error) {
            return {};
        }
    }

    function findUnread(announcements, read) {
        return (announcements || [])
            .filter(function (item) {
                return Number(read[item.id] || 0) < Number(item.reminderRevision || 0);
            })
            .map(function (item) { return item.id; });
    }

    function markRead(read, announcements) {
        const next = Object.assign({}, read || {});
        (announcements || []).forEach(function (item) {
            next[item.id] = Math.max(Number(next[item.id] || 0), Number(item.reminderRevision || 0));
        });
        return next;
    }

    return { parse: parse, findUnread: findUnread, markRead: markRead };
});
```

- [ ] **Step 5: 添加公共入口、抽屉和管理员静态区域**

Add the trigger beside the rules link:

```html
<button id="update-announcement-trigger" class="header-text-link update-announcement-trigger" type="button" aria-haspopup="dialog" aria-controls="update-announcement-drawer">
    更新公告
    <span id="update-announcement-unread-dot" class="update-announcement-unread-dot" hidden aria-label="有未读更新"></span>
</button>
```

Add a backdrop and dialog near the end of body, before scripts:

```html
<div id="update-announcement-backdrop" class="update-announcement-backdrop" hidden></div>
<aside id="update-announcement-drawer" class="update-announcement-drawer" role="dialog" aria-modal="true" aria-labelledby="update-announcement-drawer-title" hidden>
    <div class="update-announcement-drawer-header">
        <div><span class="section-eyebrow">版本历史</span><h2 id="update-announcement-drawer-title">更新公告</h2></div>
        <button id="update-announcement-close" type="button" aria-label="关闭更新公告">关闭</button>
    </div>
    <div id="update-announcement-status" class="muted-line" aria-live="polite">正在读取更新公告……</div>
    <div id="update-announcement-list" class="update-announcement-list"></div>
</aside>
```

Inside `admin-tab-announcement`, keep the existing lobby announcement panel and append this complete management region:

```html
<section id="update-announcement-admin-panel" class="match-options-panel update-announcement-admin-panel" aria-labelledby="update-announcement-admin-title">
    <div class="update-announcement-admin-header">
        <div>
            <span class="section-eyebrow">长期版本历史</span>
            <h4 id="update-announcement-admin-title">更新公告管理</h4>
        </div>
        <div class="update-announcement-admin-actions">
            <select id="update-announcement-filter" aria-label="筛选更新公告状态">
                <option value="all">全部状态</option>
                <option value="draft">草稿</option>
                <option value="published">已发布</option>
                <option value="hidden">隐藏</option>
            </select>
            <button id="update-announcement-refresh-btn" type="button">刷新</button>
            <button id="update-announcement-new-btn" class="primary-btn" type="button">新增公告</button>
        </div>
    </div>
    <div id="update-announcement-admin-status" class="match-options-status" aria-live="polite">更新公告尚未读取。</div>
    <div id="update-announcement-admin-list" class="update-announcement-admin-list"></div>

    <section id="update-announcement-editor-form" class="update-announcement-editor-form" hidden aria-labelledby="update-announcement-editor-title">
        <div class="update-announcement-editor-heading">
            <h5 id="update-announcement-editor-title">新建更新公告</h5>
            <button id="update-announcement-cancel-btn" type="button">取消编辑</button>
        </div>
        <input id="update-announcement-id-input" type="hidden">
        <div class="update-announcement-admin-row">
            <label>版本号<input id="update-announcement-version-input" type="text" maxlength="32" placeholder="例如 v1.8.5"></label>
            <label>标题<input id="update-announcement-title-input" type="text" maxlength="40" placeholder="40 个字符以内"></label>
        </div>

        <section class="update-announcement-section-editor">
            <h6>一、网页端</h6>
            <div class="update-announcement-rich-toolbar" role="toolbar" aria-label="网页端内容格式">
                <button type="button" onclick="formatUpdateAnnouncementEditor('webHtml', 'bold')"><strong>B</strong></button>
                <button type="button" onclick="formatUpdateAnnouncementEditor('webHtml', 'insertUnorderedList')">项目符号</button>
                <button type="button" onclick="formatUpdateAnnouncementEditor('webHtml', 'insertOrderedList')">编号</button>
                <button type="button" onclick="linkUpdateAnnouncementEditor('webHtml')">链接</button>
            </div>
            <div id="update-announcement-web-editor" class="update-announcement-rich-editor" contenteditable="true" role="textbox" aria-multiline="true"></div>
        </section>

        <section class="update-announcement-section-editor">
            <h6>二、游戏插件</h6>
            <div class="update-announcement-rich-toolbar" role="toolbar" aria-label="游戏插件内容格式">
                <button type="button" onclick="formatUpdateAnnouncementEditor('gamePluginHtml', 'bold')"><strong>B</strong></button>
                <button type="button" onclick="formatUpdateAnnouncementEditor('gamePluginHtml', 'insertUnorderedList')">项目符号</button>
                <button type="button" onclick="formatUpdateAnnouncementEditor('gamePluginHtml', 'insertOrderedList')">编号</button>
                <button type="button" onclick="linkUpdateAnnouncementEditor('gamePluginHtml')">链接</button>
            </div>
            <div id="update-announcement-game-plugin-editor" class="update-announcement-rich-editor" contenteditable="true" role="textbox" aria-multiline="true"></div>
        </section>

        <section class="update-announcement-section-editor">
            <h6>三、桥接插件</h6>
            <div class="update-announcement-rich-toolbar" role="toolbar" aria-label="桥接插件内容格式">
                <button type="button" onclick="formatUpdateAnnouncementEditor('bridgePluginHtml', 'bold')"><strong>B</strong></button>
                <button type="button" onclick="formatUpdateAnnouncementEditor('bridgePluginHtml', 'insertUnorderedList')">项目符号</button>
                <button type="button" onclick="formatUpdateAnnouncementEditor('bridgePluginHtml', 'insertOrderedList')">编号</button>
                <button type="button" onclick="linkUpdateAnnouncementEditor('bridgePluginHtml')">链接</button>
            </div>
            <div id="update-announcement-bridge-plugin-editor" class="update-announcement-rich-editor" contenteditable="true" role="textbox" aria-multiline="true"></div>
        </section>

        <label class="update-announcement-reminder-option">
            <input id="update-announcement-remind-again" type="checkbox">
            <span><strong>重新提醒玩家</strong><small>仅在已发布或曾发布的公告中使用。</small></span>
        </label>
        <div class="update-announcement-editor-actions">
            <button id="update-announcement-save-btn" class="primary-btn" type="button">保存</button>
        </div>
    </section>
</section>
```

There is intentionally no delete button or delete endpoint token in this markup.

Link `/css/update-announcements.css` after `app.css`, and load `update-announcement-read-state.js` before the two later controllers.

- [ ] **Step 6: 写完整基础样式**

Create `update-announcements.css` with this complete base:

```css
body.update-announcement-open { overflow: hidden; }

.update-announcement-trigger { position: relative; gap: 7px; }
.update-announcement-unread-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--danger-text);
    box-shadow: 0 0 0 2px var(--surface);
}
.update-announcement-backdrop {
    position: fixed;
    z-index: 990;
    inset: 0;
    background: rgba(15, 23, 42, 0.42);
    backdrop-filter: blur(2px);
}
.update-announcement-drawer {
    position: fixed;
    z-index: 1000;
    top: 0;
    right: 0;
    width: min(560px, 92vw);
    height: 100dvh;
    overflow-y: auto;
    padding: 22px;
    border-left: 1px solid var(--border-subtle);
    background: var(--surface-raised);
    color: var(--text-default);
    box-shadow: -18px 0 48px rgba(15, 23, 42, 0.22);
}
.update-announcement-drawer-header,
.update-announcement-admin-header,
.update-announcement-editor-heading,
.update-announcement-admin-actions,
.update-announcement-editor-actions {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
}
.update-announcement-drawer-header { margin-bottom: 16px; }
.update-announcement-drawer-header h2,
.update-announcement-admin-header h4,
.update-announcement-editor-heading h5,
.update-announcement-section-editor h6 { margin: 0; color: var(--text-strong); }
.update-announcement-list,
.update-announcement-admin-list { display: grid; gap: 10px; margin-top: 14px; }
.update-announcement-item,
.update-announcement-admin-item,
.update-announcement-editor-form {
    border: 1px solid var(--border-subtle);
    border-radius: var(--workspace-radius);
    background: var(--surface);
}
.update-announcement-item-toggle {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr) auto;
    gap: 9px;
    width: 100%;
    padding: 14px;
    text-align: left;
    border: 0;
    background: transparent;
}
.update-announcement-version { color: var(--primary); font-weight: 800; }
.update-announcement-title { min-width: 0; overflow-wrap: anywhere; color: var(--text-strong); font-weight: 800; }
.update-announcement-time { color: var(--text-muted); font-size: 12px; white-space: nowrap; }
.update-announcement-new-badge {
    padding: 2px 7px;
    border-radius: 999px;
    background: var(--danger-surface);
    color: var(--danger-text);
    font-size: 11px;
    font-weight: 800;
}
.update-announcement-item-body { padding: 0 14px 16px; border-top: 1px solid var(--border-subtle); }
.update-announcement-section { padding-top: 14px; }
.update-announcement-section h3 { margin: 0 0 7px; color: var(--text-strong); font-size: 15px; }
.update-announcement-section-content { color: var(--text-default); line-height: 1.7; overflow-wrap: anywhere; }
.update-announcement-section-content a { color: var(--primary); }
.update-announcement-admin-panel { margin-top: 16px; }
.update-announcement-admin-actions { flex-wrap: wrap; }
.update-announcement-admin-item { padding: 14px; }
.update-announcement-admin-item-head {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr) auto;
    gap: 10px;
    align-items: center;
}
.update-announcement-admin-item-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 12px; }
.update-announcement-status-chip { padding: 3px 8px; border-radius: 999px; font-size: 12px; font-weight: 800; }
.update-announcement-status-chip[data-status="published"] { background: var(--success-surface); color: var(--success-text); }
.update-announcement-status-chip[data-status="draft"] { background: var(--warning-surface); color: var(--warning-text); }
.update-announcement-status-chip[data-status="hidden"] { background: var(--surface-muted); color: var(--text-muted); }
.update-announcement-editor-form { display: grid; gap: 14px; margin-top: 16px; padding: 16px; }
.update-announcement-admin-row { display: grid; grid-template-columns: minmax(150px, .45fr) minmax(260px, 1fr); gap: 12px; }
.update-announcement-section-editor { display: grid; gap: 8px; }
.update-announcement-rich-toolbar { display: flex; gap: 6px; flex-wrap: wrap; }
.update-announcement-rich-editor {
    min-height: 140px;
    padding: 12px;
    border: 1px solid var(--border-strong);
    border-radius: 6px;
    background: var(--surface);
    color: var(--text-default);
    line-height: 1.65;
}
.update-announcement-rich-editor:focus-visible {
    border-color: var(--primary);
    outline: 3px solid var(--focus-ring);
    outline-offset: 1px;
}
.update-announcement-reminder-option { display: flex; align-items: flex-start; gap: 10px; }
.update-announcement-reminder-option span { display: grid; gap: 3px; }
.update-announcement-reminder-option small { color: var(--text-muted); }
```

Append these mobile rules:

```css
@media (max-width: 768px) {
    .site-header { flex-direction: column; align-items: stretch; }
    .site-header-actions { justify-content: flex-start; flex-wrap: wrap; }
    .update-announcement-drawer {
        width: 100%;
        max-width: none;
    }
    .update-announcement-close,
    .update-announcement-trigger,
    .update-announcement-admin-actions button {
        min-height: 44px;
    }
    .update-announcement-admin-row {
        grid-template-columns: 1fr;
    }
    .update-announcement-drawer { padding: 16px; }
    .update-announcement-item-toggle,
    .update-announcement-admin-item-head { grid-template-columns: 1fr; }
    .update-announcement-time { white-space: normal; }
}

@media (max-width: 420px) {
    .update-announcement-admin-header,
    .update-announcement-editor-heading { align-items: stretch; flex-direction: column; }
    .update-announcement-admin-actions,
    .update-announcement-admin-actions > * { width: 100%; }
    .update-announcement-rich-editor { min-height: 120px; }
}
```

Do not use selectors such as `[style*="background"]` or hard-coded light-only surfaces.

- [ ] **Step 7: 添加 UI 测试命令并运行**

Add:

```json
"test:update-announcement-ui": "node scripts/check-update-announcement-ui.cjs"
```

Run:

```powershell
npm run test:update-announcement-ui
```

Expected: PASS。

- [ ] **Step 8: 提交**

```powershell
git add web-command-center/public/index.html web-command-center/public/css/update-announcements.css web-command-center/public/js/update-announcement-read-state.js web-command-center/scripts/check-update-announcement-ui.cjs web-command-center/package.json
git commit -m "feat: add update announcement workspace"
```

---

### Task 6: 实现玩家公告抽屉

**Files:**

- Create: `web-command-center/public/js/update-announcement-public.js`
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/scripts/check-update-announcement-ui.cjs`

**Interfaces:**

- Consumes: `CaorenUpdateAnnouncementReadState`, `/api/update-announcements`, `window.__caorenCupSocket`.
- Produces: live drawer rendering, red dot, “新” snapshot badges and accessible open/close behavior.

- [ ] **Step 1: 备份已修改的 index 和 UI 合约测试**

```powershell
Copy-Item web-command-center/public/index.html web-command-center/public/index.html.bak-20260714-update-announcement-public
Copy-Item web-command-center/scripts/check-update-announcement-ui.cjs web-command-center/scripts/check-update-announcement-ui.cjs.bak-20260714-update-announcement-public
```

- [ ] **Step 2: 先扩展失败合约**

Require the public controller to contain these behavior tokens:

```js
for (const token of [
  '/api/update-announcements',
  'UPDATE_ANNOUNCEMENTS',
  'Asia/Shanghai',
  'caoren-update-announcement-read-v1',
  'aria-expanded',
  'Escape',
  'update-announcement-open',
]) {
  assert.ok(publicJs.includes(token), `missing public update announcement behavior: ${token}`);
}
```

Run `npm run test:update-announcement-ui`; expect FAIL because the controller does not exist.

- [ ] **Step 3: 实现公开控制器**

Create `update-announcement-public.js` as an IIFE. Use this state:

```js
const STORAGE_KEY = 'caoren-update-announcement-read-v1';
let announcements = [];
let openUnreadSnapshot = [];
let memoryReadState = {};
let triggerBeforeOpen = null;
```

Use the following controller structure; all player-visible title/version/time nodes use `textContent`, and only server-sanitized section fields use `innerHTML`:

```js
(function () {
    'use strict';
    const readApi = window.CaorenUpdateAnnouncementReadState;
    const STORAGE_KEY = 'caoren-update-announcement-read-v1';
    const trigger = document.getElementById('update-announcement-trigger');
    const dot = document.getElementById('update-announcement-unread-dot');
    const backdrop = document.getElementById('update-announcement-backdrop');
    const drawer = document.getElementById('update-announcement-drawer');
    const closeButton = document.getElementById('update-announcement-close');
    const status = document.getElementById('update-announcement-status');
    const list = document.getElementById('update-announcement-list');
    let announcements = [];
    let openUnreadSnapshot = [];
    let memoryReadState = {};
    let triggerBeforeOpen = null;

    function loadReadState() {
        try {
            memoryReadState = readApi.parse(localStorage.getItem(STORAGE_KEY));
        } catch (_error) {}
        return Object.assign({}, memoryReadState);
    }

    function saveReadState(value) {
        memoryReadState = Object.assign({}, value);
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(memoryReadState)); } catch (_error) {}
    }

    function formatChinaTimestamp(value) {
        const parts = new Intl.DateTimeFormat('zh-CN', {
            timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
            hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
        }).formatToParts(new Date(value));
        const values = Object.fromEntries(parts.map(function (part) { return [part.type, part.value]; }));
        return values.year + '-' + values.month + '-' + values.day + ' '
            + values.hour + ':' + values.minute + ':' + values.second;
    }

    function isSnapshotItem(item) {
        return openUnreadSnapshot.some(function (snapshot) {
            return snapshot.id === item.id && snapshot.reminderRevision === item.reminderRevision;
        });
    }

    function updateUnreadDot() {
        const unread = new Set(readApi.findUnread(announcements, loadReadState()));
        const visible = announcements.some(function (item) {
            return unread.has(item.id) && !isSnapshotItem(item);
        });
        dot.hidden = !visible;
    }

    function createSection(title, html) {
        const section = document.createElement('section');
        section.className = 'update-announcement-section';
        const heading = document.createElement('h3');
        heading.textContent = title;
        const content = document.createElement('div');
        content.className = 'update-announcement-section-content';
        content.innerHTML = html;
        section.append(heading, content);
        return section;
    }

    function renderAnnouncements() {
        list.replaceChildren();
        if (!announcements.length) {
            status.textContent = '暂时没有已发布的更新公告。';
            return;
        }
        status.textContent = '共 ' + announcements.length + ' 个已发布版本。';
        announcements.forEach(function (item, index) {
            const article = document.createElement('article');
            article.className = 'update-announcement-item';
            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'update-announcement-item-toggle';
            const bodyId = 'update-announcement-body-' + item.id;
            toggle.setAttribute('aria-controls', bodyId);
            toggle.setAttribute('aria-expanded', index === 0 ? 'true' : 'false');

            const version = document.createElement('span');
            version.className = 'update-announcement-version';
            version.textContent = item.version;
            const title = document.createElement('span');
            title.className = 'update-announcement-title';
            title.textContent = item.title;
            const time = document.createElement('time');
            time.className = 'update-announcement-time';
            time.textContent = formatChinaTimestamp(item.publishedAt);
            toggle.append(version, title, time);
            if (isSnapshotItem(item)) {
                const badge = document.createElement('span');
                badge.className = 'update-announcement-new-badge';
                badge.textContent = '新';
                toggle.append(badge);
            }

            const body = document.createElement('div');
            body.id = bodyId;
            body.className = 'update-announcement-item-body';
            body.hidden = index !== 0;
            body.append(
                createSection('一、网页端', item.sections.webHtml),
                createSection('二、游戏插件', item.sections.gamePluginHtml),
                createSection('三、桥接插件', item.sections.bridgePluginHtml),
            );
            toggle.addEventListener('click', function () {
                const expanded = toggle.getAttribute('aria-expanded') === 'true';
                toggle.setAttribute('aria-expanded', expanded ? 'false' : 'true');
                body.hidden = expanded;
            });
            article.append(toggle, body);
            list.append(article);
        });
    }

    function applyAnnouncements(next) {
        announcements = Array.isArray(next) ? next : [];
        renderAnnouncements();
        updateUnreadDot();
    }

    async function refreshPublicAnnouncements() {
        try {
            const response = await fetch('/api/update-announcements', { headers: { Accept: 'application/json' } });
            const data = await response.json();
            if (!response.ok || !data.success) throw new Error(data.error || '更新公告暂时无法读取');
            applyAnnouncements(data.announcements);
        } catch (_error) {
            status.textContent = '更新公告暂时无法读取，请稍后重试。';
            updateUnreadDot();
        }
    }

    async function openDrawer() {
        triggerBeforeOpen = document.activeElement;
        drawer.hidden = false;
        backdrop.hidden = false;
        document.body.classList.add('update-announcement-open');
        closeButton.focus();
        await refreshPublicAnnouncements();
        const unread = new Set(readApi.findUnread(announcements, loadReadState()));
        openUnreadSnapshot = announcements
            .filter(function (item) { return unread.has(item.id); })
            .map(function (item) { return { id: item.id, reminderRevision: item.reminderRevision }; });
        renderAnnouncements();
        updateUnreadDot();
    }

    function closeDrawer() {
        if (drawer.hidden) return;
        saveReadState(readApi.markRead(loadReadState(), openUnreadSnapshot));
        openUnreadSnapshot = [];
        drawer.hidden = true;
        backdrop.hidden = true;
        document.body.classList.remove('update-announcement-open');
        updateUnreadDot();
        if (triggerBeforeOpen && typeof triggerBeforeOpen.focus === 'function') triggerBeforeOpen.focus();
    }

    function trapFocus(event) {
        if (drawer.hidden || event.key !== 'Tab') return;
        const focusable = Array.from(drawer.querySelectorAll('button, a[href], input, select, textarea, [tabindex]:not([tabindex="-1"])'))
            .filter(function (element) { return !element.disabled && !element.hidden; });
        if (!focusable.length) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }

    trigger.addEventListener('click', openDrawer);
    closeButton.addEventListener('click', closeDrawer);
    backdrop.addEventListener('click', closeDrawer);
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && !drawer.hidden) closeDrawer();
        trapFocus(event);
    });
    const socket = window.__caorenCupSocket;
    if (socket && typeof socket.on === 'function') {
        socket.on('UPDATE_ANNOUNCEMENTS', function (payload) {
            applyAnnouncements(payload && payload.announcements);
        });
    }
    refreshPublicAnnouncements();
})();
```

The snapshot stores both ID and revision. A later Socket revision cannot match the opening snapshot, so it remains unread and restores the red dot even while the drawer is open.

- [ ] **Step 4: 加载控制器并运行测试**

Add after `update-announcement-read-state.js`:

```html
<script src="/js/update-announcement-public.js"></script>
```

Run:

```powershell
npm run test:update-announcement-ui
npm run typecheck
```

Expected: 两条命令均退出码 0。

- [ ] **Step 5: 提交**

```powershell
git add web-command-center/public/js/update-announcement-public.js web-command-center/public/index.html web-command-center/scripts/check-update-announcement-ui.cjs
git commit -m "feat: add player update announcement drawer"
```

---

### Task 7: 实现管理员版本公告管理

**Files:**

- Create: `web-command-center/public/js/update-announcement-admin.js`
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/js/lobby-app.js`
- Modify: `web-command-center/public/css/update-announcements.css`
- Modify: `web-command-center/scripts/check-update-announcement-ui.cjs`

**Interfaces:**

- Consumes: three admin POST endpoints and the existing hidden `extra-input` administrator credential.
- Consumes custom event: `caoren:admin-view-changed` with `{ detail: { view } }`.
- Produces list/filter/editor actions without permanent delete.

- [ ] **Step 1: 创建任务专用备份**

```powershell
Copy-Item web-command-center/public/index.html web-command-center/public/index.html.bak-20260714-update-announcement-admin
Copy-Item web-command-center/public/js/lobby-app.js web-command-center/public/js/lobby-app.js.bak-20260714-update-announcement-admin
Copy-Item web-command-center/public/css/update-announcements.css web-command-center/public/css/update-announcements.css.bak-20260714-update-announcement-admin
Copy-Item web-command-center/scripts/check-update-announcement-ui.cjs web-command-center/scripts/check-update-announcement-ui.cjs.bak-20260714-update-announcement-admin
```

- [ ] **Step 2: 扩展失败合约**

Require the admin controller to contain all endpoint paths, all three statuses, `confirmVersionChange`, `remindAgain`, version-change confirmation text, and no permanent delete request. Require `lobby-app.js` to dispatch `caoren:admin-view-changed`.

Run `npm run test:update-announcement-ui`; expect FAIL because the admin controller is absent.

- [ ] **Step 3: 派发管理员视图事件**

At the end of `switchAdminView(view)` in `lobby-app.js`, add:

```js
window.dispatchEvent(new CustomEvent('caoren:admin-view-changed', {
    detail: { view: activeView }
}));
```

- [ ] **Step 4: 实现管理员 API 与渲染控制器**

Create `update-announcement-admin.js` as an IIFE. Centralize authenticated calls:

```js
async function adminRequest(path, payload) {
    const adminPassword = document.getElementById('extra-input')?.value
        || prompt('请输入管理员密码：')
        || '';
    if (!adminPassword) throw new Error('未输入管理员密码');
    const response = await fetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(Object.assign({ adminPassword: adminPassword }, payload || {})),
    });
    const data = await response.json();
    if (!response.ok || !data.success) throw new Error(data.error || '更新公告操作失败');
    return data;
}
```

After `adminRequest`, implement the controller with this exact state and behavior:

```js
    const adminStatus = document.getElementById('update-announcement-admin-status');
    const adminList = document.getElementById('update-announcement-admin-list');
    const filter = document.getElementById('update-announcement-filter');
    const form = document.getElementById('update-announcement-editor-form');
    const formTitle = document.getElementById('update-announcement-editor-title');
    const idInput = document.getElementById('update-announcement-id-input');
    const versionInput = document.getElementById('update-announcement-version-input');
    const titleInput = document.getElementById('update-announcement-title-input');
    const webEditor = document.getElementById('update-announcement-web-editor');
    const gameEditor = document.getElementById('update-announcement-game-plugin-editor');
    const bridgeEditor = document.getElementById('update-announcement-bridge-plugin-editor');
    const remindCheckbox = document.getElementById('update-announcement-remind-again');
    let records = [];
    let originalVersion = '';
    let previouslyPublished = false;

    const statusLabels = { draft: '草稿', published: '已发布', hidden: '隐藏' };
    const editorBySection = {
        webHtml: webEditor,
        gamePluginHtml: gameEditor,
        bridgePluginHtml: bridgeEditor,
    };

    function formatChinaTimestamp(value) {
        if (!value) return '尚未发布';
        const parts = new Intl.DateTimeFormat('zh-CN', {
            timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
            hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
        }).formatToParts(new Date(value));
        const values = Object.fromEntries(parts.map(function (part) { return [part.type, part.value]; }));
        return values.year + '-' + values.month + '-' + values.day + ' '
            + values.hour + ':' + values.minute + ':' + values.second;
    }

    function actionButton(label, handler, className) {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = label;
        if (className) button.className = className;
        button.addEventListener('click', handler);
        return button;
    }

    function renderAdminList() {
        adminList.replaceChildren();
        const selected = filter.value;
        const visible = records.filter(function (item) {
            return selected === 'all' || item.status === selected;
        });
        if (!visible.length) {
            const empty = document.createElement('p');
            empty.className = 'muted-line';
            empty.textContent = '当前筛选条件下没有更新公告。';
            adminList.append(empty);
            return;
        }
        visible.forEach(function (item) {
            const article = document.createElement('article');
            article.className = 'update-announcement-admin-item';
            const head = document.createElement('div');
            head.className = 'update-announcement-admin-item-head';
            const chip = document.createElement('span');
            chip.className = 'update-announcement-status-chip';
            chip.dataset.status = item.status;
            chip.textContent = statusLabels[item.status] || item.status;
            const title = document.createElement('strong');
            title.textContent = item.version + ' · ' + item.title;
            const published = document.createElement('time');
            published.textContent = formatChinaTimestamp(item.publishedAt);
            head.append(chip, title, published);

            const meta = document.createElement('p');
            meta.className = 'muted-line';
            meta.textContent = '最后编辑：' + formatChinaTimestamp(item.updatedAt);
            const actions = document.createElement('div');
            actions.className = 'update-announcement-admin-item-actions';
            actions.append(actionButton('编辑', function () { openEditor(item); }));
            if (item.status === 'draft') {
                actions.append(actionButton('发布', function () { changeStatus(item, 'published'); }, 'primary-btn'));
            } else if (item.status === 'published') {
                actions.append(actionButton('隐藏', function () { changeStatus(item, 'hidden'); }));
            } else {
                actions.append(actionButton('重新发布', function () { changeStatus(item, 'published'); }, 'primary-btn'));
            }
            article.append(head, meta, actions);
            adminList.append(article);
        });
    }

    function openEditor(item) {
        const value = item || null;
        idInput.value = value ? value.id : '';
        versionInput.value = value ? value.version : '';
        titleInput.value = value ? value.title : '';
        webEditor.innerHTML = value ? value.sections.webHtml : '';
        gameEditor.innerHTML = value ? value.sections.gamePluginHtml : '';
        bridgeEditor.innerHTML = value ? value.sections.bridgePluginHtml : '';
        remindCheckbox.checked = false;
        originalVersion = value ? value.version : '';
        previouslyPublished = Boolean(value && value.publishedAt);
        formTitle.textContent = value ? '编辑 ' + value.version : '新建更新公告';
        form.hidden = false;
        versionInput.focus();
    }

    function closeEditor() {
        form.hidden = true;
        idInput.value = '';
        originalVersion = '';
        previouslyPublished = false;
        remindCheckbox.checked = false;
    }

    async function refreshAdminAnnouncements() {
        adminStatus.textContent = '正在读取更新公告……';
        try {
            const data = await adminRequest('/api/admin/update-announcements/list');
            records = Array.isArray(data.announcements) ? data.announcements : [];
            renderAdminList();
            adminStatus.textContent = '已读取 ' + records.length + ' 条更新公告。';
        } catch (error) {
            adminStatus.textContent = error.message || '更新公告读取失败。';
        }
    }

    async function saveAnnouncement() {
        const versionChanged = previouslyPublished
            && originalVersion
            && versionInput.value.trim() !== originalVersion;
        let confirmedVersionChange = false;
        if (versionChanged) {
            confirmedVersionChange = confirm('发布后的版本号已改变。保存后会重新提醒所有玩家，确定继续吗？');
            if (!confirmedVersionChange) return;
        }
        adminStatus.textContent = '正在保存更新公告……';
        try {
            await adminRequest('/api/admin/update-announcements/save', {
                announcement: {
                    id: idInput.value || undefined,
                    version: versionInput.value,
                    title: titleInput.value,
                    sections: {
                        webHtml: webEditor.innerHTML,
                        gamePluginHtml: gameEditor.innerHTML,
                        bridgePluginHtml: bridgeEditor.innerHTML,
                    },
                    remindAgain: remindCheckbox.checked,
                    confirmVersionChange: confirmedVersionChange,
                },
            });
            closeEditor();
            await refreshAdminAnnouncements();
            adminStatus.textContent = '更新公告已保存。';
        } catch (error) {
            adminStatus.textContent = error.message || '更新公告保存失败。';
        }
    }

    async function changeStatus(item, targetStatus) {
        const action = targetStatus === 'hidden' ? '隐藏' : item.status === 'hidden' ? '重新发布' : '发布';
        if (!confirm('确定要' + action + ' ' + item.version + ' 吗？')) return;
        const remindAgain = item.status === 'hidden' && targetStatus === 'published'
            ? confirm('是否同时重新提醒所有玩家？选择“取消”只表示不重复提醒，公告仍会重新发布。')
            : false;
        adminStatus.textContent = '正在' + action + '更新公告……';
        try {
            await adminRequest('/api/admin/update-announcements/status', {
                id: item.id,
                status: targetStatus,
                remindAgain: remindAgain,
            });
            await refreshAdminAnnouncements();
            adminStatus.textContent = '更新公告已' + action + '。';
        } catch (error) {
            adminStatus.textContent = error.message || '更新公告状态修改失败。';
        }
    }

    function formatUpdateAnnouncementEditor(section, command) {
        const editor = editorBySection[section];
        if (!editor) return;
        editor.focus();
        document.execCommand(command, false, null);
    }

    function linkUpdateAnnouncementEditor(section) {
        const editor = editorBySection[section];
        if (!editor) return;
        const url = prompt('请输入链接地址，建议使用 https:// 开头：');
        if (!url) return;
        editor.focus();
        document.execCommand('createLink', false, url);
    }

    document.getElementById('update-announcement-refresh-btn').addEventListener('click', refreshAdminAnnouncements);
    document.getElementById('update-announcement-new-btn').addEventListener('click', function () { openEditor(null); });
    document.getElementById('update-announcement-cancel-btn').addEventListener('click', closeEditor);
    document.getElementById('update-announcement-save-btn').addEventListener('click', saveAnnouncement);
    filter.addEventListener('change', renderAdminList);
    window.addEventListener('caoren:admin-view-changed', function (event) {
        const controls = document.getElementById('admin-controls');
        if (event.detail && event.detail.view === 'announcement'
            && controls && controls.classList.contains('is-admin-session')) {
            refreshAdminAnnouncements();
        }
    });
    Object.assign(window, {
        formatUpdateAnnouncementEditor: formatUpdateAnnouncementEditor,
        linkUpdateAnnouncementEditor: linkUpdateAnnouncementEditor,
        refreshUpdateAnnouncements: refreshAdminAnnouncements,
    });
```

Wrap this body and `adminRequest` in one strict IIFE. API failures leave the editor open and preserve every input because only the success branch calls `closeEditor()`.

- [ ] **Step 5: 加载脚本并完善响应式样式**

Add:

```html
<script src="/js/update-announcement-admin.js"></script>
```

Ensure published/draft/hidden chips use `--success-*`, `--warning-*`, and muted surface variables. At 768px, render admin records as grouped blocks; at 360px, toolbar buttons wrap and editors keep a minimum 120px editing height.

- [ ] **Step 6: 运行 UI、后端和类型测试**

Run:

```powershell
npm run test:update-announcement-ui
npm run test:update-announcements
npm run typecheck
```

Expected: 三条命令均退出码 0。

- [ ] **Step 7: 提交**

```powershell
git add web-command-center/public/index.html web-command-center/public/js/lobby-app.js web-command-center/public/js/update-announcement-admin.js web-command-center/public/css/update-announcements.css web-command-center/scripts/check-update-announcement-ui.cjs
git commit -m "feat: add update announcement administration"
```

---

### Task 8: 全量回归、浏览器验收与交付检查

**Files:**

- Modify only if verification finds a defect: files introduced or modified in Tasks 1–7.
- Create ignored screenshots under: `release-build/update-announcement-qa/`

**Interfaces:**

- Consumes the complete feature.
- Produces verified local screenshots and a clean, reviewable branch; performs no deployment or push.

- [ ] **Step 1: 运行所有相关自动测试**

Run from `web-command-center`:

```powershell
npm run typecheck
npm run test:update-announcements
npm run test:update-announcement-ui
npm run test:lobby-identity
npm run test:fixed-member-ui
npm run test:caoren-modules
npm run test:postmatch-fun-stats
npm run test:match-command-policy
npm run test:rules-content
npm run test:rules-pdf
npm run test:rules-guide
```

Expected: 每条命令退出码 0。任何失败都先定位原因，不得删除或弱化原有测试来换取通过。

- [ ] **Step 2: 启动隔离的本地 QA 服务**

Use a test-only store path under ignored `runtime/`, and do not reuse production data:

```powershell
$env:PORT='3103'
$env:UPDATE_ANNOUNCEMENT_STORE_PATH=(Resolve-Path '.\runtime').Path + '\update-announcements-qa.json'
npm run dev
```

Expected: `http://127.0.0.1:3103/` 可访问，首次读取包含 v1.8.2、v1.8.3、v1.8.4。

- [ ] **Step 3: 验收登录前、登录后和管理员流程**

Check all of the following in both light and dark themes:

1. 登录前按钮可见，三条种子使红点出现；
2. 抽屉桌面从右侧打开，最新版本展开，旧版本折叠；
3. 打开后红点消失但“新”标记保留，关闭后刷新不再出现；
4. 清除本地已读键后红点恢复；
5. 正常登录后同一入口仍可使用；
6. 管理员公告模块同时显示大厅公告与更新公告管理；
7. 草稿不会出现在玩家抽屉；
8. 发布后实时出现，隐藏后实时消失；
9. 普通编辑不重新提醒，勾选后红点恢复；
10. 发布后修改版本号必须二次确认并重新提醒；
11. 三段全空不能发布，单段有内容时其余两段显示默认文案；
12. 抽屉打开期间再次发布新版本或提高修订号时，新的红点重新出现；
13. 临时禁止 localStorage 后仍能阅读公告，并在当前标签页使用内存已读状态；
14. 更新公告存储不可用时，玩家看到通用错误且登录不受影响；
15. 错误状态不清空管理员正在编辑的内容。

- [ ] **Step 4: 验收尺寸、键盘和溢出**

Capture screenshots at 360px, 768px, 1440px and a wide desktop size into `release-build/update-announcement-qa/`. Verify:

- no body-level horizontal overflow;
- mobile drawer is full-screen;
- buttons remain at least 44px on mobile;
- long title and links wrap;
- Tab stays inside an open drawer;
- Escape closes it and returns focus;
- backdrop click closes it;
- all admin records remain readable on mobile.

- [ ] **Step 5: 停止本地服务并检查临时数据**

Stop the dev process. Confirm QA JSON、screenshots、logs and backups are ignored:

```powershell
git status --short --ignored
```

Expected: only intended source/docs changes are tracked; ignored QA files begin with `!!` and are not staged.

- [ ] **Step 6: 检查提交内容**

Run:

```powershell
git diff origin/main...HEAD --check
git diff origin/main...HEAD --stat
git status --short --branch
git log --oneline --decorate origin/main..HEAD
```

Expected: no whitespace errors, no private Bot files, no backups, no runtime JSON, no package archives, and no unrelated user changes.

- [ ] **Step 7: 仅在修复验证缺陷时提交最终修正**

If verification required source changes, back up each affected existing file first, rerun the failed test plus the full relevant suite, then commit only those corrections:

```powershell
git add web-command-center
git commit -m "fix: polish update announcement experience"
```

If no defects were found, do not create an empty commit.

- [ ] **Step 8: 向用户交付本地结果**

Report modified files, schema, initial seeds, APIs, tests, screenshots, known production risks and the fact that no push, PR, deployment or Release occurred. Ask for screenshot review before any publication action.
