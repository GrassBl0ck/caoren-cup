# 管理员更新公告会话授权 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 已进入大厅的管理员能直接管理更新公告，不再被重复要求输入管理员密码。

**Architecture:** 网页管理员面板通过现有 Socket.IO 连接发起公告管理请求。服务端在每个请求执行时，以 `socket.data.playerId` 查找当前大厅玩家并要求角色为 `Admin`；公告输入校验和领域操作抽到共享模块，保留的 HTTP 密码接口与 Socket 共用同一逻辑。

**Tech Stack:** Node.js、TypeScript、Express、Socket.IO、原生 JavaScript、node:test。

## Global Constraints

- 管理员密码、旧登录方式和 `/api/admin/update-announcements/*` HTTP 密码校验必须保留。
- 管理员网页不缓存、显示、记录或通过 HTTP 再次发送管理员密码。
- 不新增设备令牌、长期令牌、自动登录或原生依赖；HTTP 的 HTTPS 限制不变。
- Socket 管理事件只允许当前已认证且仍为 `Admin` 的连接，绝不广播草稿或管理员列表。
- 公告版本、标题、内容、发布、隐藏、重新提醒与公开公告广播保持兼容。
- 修改已有文件前创建 UTF-8、Git 忽略备份；不提交运行数据、备份、日志、构建物或私有插件。

---

### Task 1: 抽取可被 HTTP 与 Socket 共用的公告管理操作

**Files:**
- Create: `web-command-center/src/update-announcements/update-announcement-admin-operations.ts`
- Create: `web-command-center/src/update-announcements/update-announcement-admin-operations.test.ts`
- Modify: `web-command-center/src/routes/update-announcement-routes.ts`
- Test: `web-command-center/src/routes/update-announcement-routes.test.ts`

**Interfaces:**
- Consumes: `UpdateAnnouncementService`、`SaveUpdateAnnouncementInput`、`UpdateAnnouncementValidationError`、`UpdateAnnouncementUnavailableError`。
- Produces: `normalizeUpdateAnnouncementSaveInput(raw)`、`normalizeUpdateAnnouncementStatusInput(raw)`、`runUpdateAnnouncementAdminOperation(service, operation, raw)`、`toUpdateAnnouncementAdminFailure(error)`。
- Preserves: HTTP 的 JSON 状态码与 `{ success: false, error }` 响应格式。

- [ ] **Step 1: 写失败测试**

```ts
test('shared admin operations validate input and project safe errors', async () => {
  assert.equal(normalizeUpdateAnnouncementSaveInput({ version: 1 }), null);
  assert.deepEqual(
    toUpdateAnnouncementAdminFailure(
      new UpdateAnnouncementValidationError('version_invalid', '版本号格式必须为 vX.Y.Z'),
    ),
    { status: 400, error: '版本号格式必须为 vX.Y.Z' },
  );
});
```

- [ ] **Step 2: 运行测试确认失败**

Run: `npm run test:update-announcements`

Expected: FAIL，缺少共享操作模块。

- [ ] **Step 3: 实现共享模块**

```ts
export type UpdateAnnouncementAdminOperation = 'list' | 'save' | 'status';

export const toUpdateAnnouncementAdminFailure = (error: unknown) => {
  if (error instanceof UpdateAnnouncementValidationError) {
    return { status: error.code === 'version_duplicate' ? 409 : 400, error: error.message };
  }
  if (error instanceof UpdateAnnouncementUnavailableError) {
    return { status: 503, error: '更新公告暂时无法读取' };
  }
  return { status: 500, error: '更新公告操作失败' };
};
```

迁移现有路由中的对象检查、`SECTION_KEYS` 和保存输入规范化。状态操作只接受 `id: string`、`status: 'draft' | 'published' | 'hidden'`、`remindAgain: boolean`；领域调用只返回操作结果与 `publicChanged`，不依赖 HTTP 或 Socket。

- [ ] **Step 4: 改造 HTTP 路由**

保留 `requireAdmin` 对 `adminPassword` 的比较与原路径。HTTP handler 改调共享模块；当结果 `publicChanged` 为真时继续执行：

```ts
deps.broadcastPublic(deps.service.listPublic());
```

- [ ] **Step 5: 运行公告回归**

Run: `npm run test:update-announcements`

Expected: PASS，旧的 HTTP 密码、畸形 JSON、重复版本、发布与恢复测试全部继续通过。

- [ ] **Step 6: 提交**

```bash
git add web-command-center/src/update-announcements/update-announcement-admin-operations.ts web-command-center/src/update-announcements/update-announcement-admin-operations.test.ts web-command-center/src/routes/update-announcement-routes.ts web-command-center/src/routes/update-announcement-routes.test.ts
git commit -m "refactor: share update announcement admin operations"
```

### Task 2: 注册管理员 Socket 公告事件

**Files:**
- Create: `web-command-center/src/update-announcements/update-announcement-admin-socket.ts`
- Create: `web-command-center/src/update-announcements/update-announcement-admin-socket.test.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/server.ts`

**Interfaces:**
- Consumes: Task 1 共享操作、`findPlayerById(getSession(), socket.data.playerId)`、`UpdateAnnouncementService`。
- Produces: `UPDATE_ANNOUNCEMENT_ADMIN_LIST`、`UPDATE_ANNOUNCEMENT_ADMIN_SAVE`、`UPDATE_ANNOUNCEMENT_ADMIN_SET_STATUS` Socket acknowledgement。
- Preserves: `UPDATE_ANNOUNCEMENTS` 只广播公开投影。

- [ ] **Step 1: 写 Socket 鉴权失败测试**

复用 `fixed-member-socket.test.ts` 的 Fake Socket/IO 模式，新增带 acknowledgement 的 `triggerWithAck`。覆盖：

```ts
assert.deepEqual(await outsider.triggerWithAck(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_LIST, {}), {
  success: false, error: '管理员会话已失效，请重新登录',
});
admin.data.playerId = 'admin';
const saved = await admin.triggerWithAck(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_SAVE, {
  announcement: { version: 'v1.8.5', title: '公告', sections: { webHtml: '<p>内容</p>' } },
});
assert.equal(saved.success, true);
assert.equal(saved.announcement.status, 'draft');
```

发布该草稿后断言只有 `WsEvents.UPDATE_ANNOUNCEMENTS` 被 `io.emit` 广播，且 payload 不含草稿。

- [ ] **Step 2: 运行测试确认失败**

Run: `npm run test:update-announcements`

Expected: FAIL，Socket 事件尚未注册。

- [ ] **Step 3: 实现 Socket 注册模块**

```ts
const requireSocketAdmin = (socket: Socket) => {
  const playerId = typeof socket.data.playerId === 'string' ? socket.data.playerId : '';
  const player = playerId ? findPlayerById(getSession(), playerId) : undefined;
  return player?.role === 'Admin' && player.isOnline ? player : undefined;
};

const reject = (ack: Ack) =>
  ack({ success: false, error: '管理员会话已失效，请重新登录' });
```

每个 handler 先调用 `requireSocketAdmin`，失败只通过 acknowledgement 返回错误。成功时运行 Task 1 操作；保存/状态变更若 `publicChanged` 为真，调用注入的 `broadcastPublic(service.listPublic())`。

- [ ] **Step 4: 装配事件**

在 `WsEvents` 增加：

```ts
UPDATE_ANNOUNCEMENT_ADMIN_LIST = 'UPDATE_ANNOUNCEMENT_ADMIN_LIST',
UPDATE_ANNOUNCEMENT_ADMIN_SAVE = 'UPDATE_ANNOUNCEMENT_ADMIN_SAVE',
UPDATE_ANNOUNCEMENT_ADMIN_SET_STATUS = 'UPDATE_ANNOUNCEMENT_ADMIN_SET_STATUS',
```

在 `server.ts` 中，使用已创建的 `io`、`updateAnnouncementService`、`broadcastUpdateAnnouncements` 调用 `registerUpdateAnnouncementAdminSocketHandlers`。保留 `registerUpdateAnnouncementRoutes`。

- [ ] **Step 5: 运行 Socket 与身份回归**

Run: `npm run test:update-announcements && npm run test:lobby-identity`

Expected: PASS；普通/未登录 Socket 被拒绝，管理员可操作，固定成员与旧游戏码登录不回归。

- [ ] **Step 6: 提交**

```bash
git add web-command-center/src/update-announcements/update-announcement-admin-socket.ts web-command-center/src/update-announcements/update-announcement-admin-socket.test.ts web-command-center/src/types.ts web-command-center/src/server.ts
git commit -m "feat: authorize update announcements by admin socket session"
```

### Task 3: 移除网页公告管理密码弹窗

**Files:**
- Modify: `web-command-center/public/js/update-announcement-admin.js`
- Modify: `web-command-center/scripts/check-update-announcement-ui.cjs`
- Test: `web-command-center/scripts/check-update-announcement-ui.cjs`

**Interfaces:**
- Consumes: `window.__caorenCupSocket` 与 Task 2 事件名。
- Produces: `adminSocketRequest(event, payload)` 与状态栏错误 `管理员会话已失效，请重新登录`。
- Preserves: 编辑器、版本变更二次确认、重新提醒、并发锁、筛选和公共公告抽屉。

- [ ] **Step 1: 扩展 UI 合约为失败状态**

```js
assert.doesNotMatch(adminScript, /prompt\('请输入管理员密码：'\)/);
assert.doesNotMatch(adminScript, /adminPassword\s*:/);
assert.match(adminScript, /UPDATE_ANNOUNCEMENT_ADMIN_LIST/);
assert.match(adminScript, /管理员会话已失效，请重新登录/);
```

- [ ] **Step 2: 运行合约测试确认失败**

Run: `npm run test:update-announcement-ui`

Expected: FAIL，当前脚本仍有密码 prompt 和 HTTP fetch。

- [ ] **Step 3: 实现 Socket acknowledgement 请求器**

```js
function adminSocketRequest(event, payload) {
  const socket = window.__caorenCupSocket;
  if (!socket || !socket.connected) {
    return Promise.reject(new Error('管理员会话已失效，请重新登录'));
  }
  return new Promise(function (resolve, reject) {
    const timeout = window.setTimeout(function () {
      reject(new Error('管理员会话已失效，请重新登录'));
    }, 10000);
    socket.emit(event, payload || {}, function (result) {
      window.clearTimeout(timeout);
      if (!result || result.success !== true) {
        reject(new Error(result?.error || '管理员会话已失效，请重新登录'));
        return;
      }
      resolve(result);
    });
  });
}
```

列表调用 `UPDATE_ANNOUNCEMENT_ADMIN_LIST`，保存调用 `...SAVE`，发布/隐藏调用 `...SET_STATUS`。删除 `adminPassword`、`prompt('请输入管理员密码：')` 和该模块的 HTTP `fetch`。失败时保持表单打开，避免用户文本丢失。

- [ ] **Step 4: 运行 UI 与公告测试**

Run: `npm run test:update-announcement-ui && npm run test:update-announcements`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add web-command-center/public/js/update-announcement-admin.js web-command-center/scripts/check-update-announcement-ui.cjs
git commit -m "fix: remove repeated admin password prompt for announcements"
```

### Task 4: 全量验证与交付

**Files:**
- Modify: 仅当前述测试发现问题时修改对应源文件和测试；不创建生产配置或运行数据。

**Interfaces:**
- Consumes: Task 1–3 的 Socket 管理事件与网页请求器。
- Produces: 测试结果与本地浏览器验证记录；不自动推送、合并、发布或部署。

- [ ] **Step 1: 运行完整回归**

```powershell
cd D:\OpenSourcework\caoren-cup-rules-guide\web-command-center
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

Expected: 每项退出码为 0。

- [ ] **Step 2: 本地浏览器验证**

用隔离运行数据启动本地服务。以管理员身份进入“公告”，验证读取、保存草稿、发布和隐藏都不出现管理员密码弹窗；断开 Socket 后点击保存，状态栏显示 `管理员会话已失效，请重新登录`，编辑内容不丢失。

- [ ] **Step 3: 复核 Git 卫生**

```powershell
git status --short --ignored
git diff origin/main...HEAD --check
git diff origin/main...HEAD --stat
```

Expected: 只包含设计、计划、源代码和测试；备份、运行数据、日志、构建物和私有插件保持忽略。

- [ ] **Step 4: 交付**

汇报文件、鉴权边界、测试、截图和生产影响。网页端需要单独部署；游戏插件、桥接插件和桌面客户端无需更新。未经新授权，不推送、创建 PR、合并、发布或部署。

