# 异能模式阶段 1：网页角色 BP 实施计划

> **执行要求：** 必须使用 `subagent-driven-development`（推荐）或 `executing-plans` 技能逐任务执行，并在每个提交点复核测试结果。

**目标：** 在现有大厅流程中加入可配置的角色暗 Ban 和人数自适应蛇形选角，保证服务器端判定、敌方信息保密、超时处理和进程重启恢复正确。

**架构：** 新增纯 TypeScript `AbilityDraftService` 承担全部 BP 规则；`game-flow-manager` 只负责阶段编排和定时器；Socket 层只鉴权并调用服务；`sanitizeForPublic` 根据查看者生成不同公开状态；前端只渲染服务器返回的合法状态。阶段 1 不发送插件命令，BP 完成后仍进入现有 `PreGameSetup`。

**技术栈：** Node.js、TypeScript、Socket.IO、现有原生 HTML/CSS/JavaScript、Node 内置测试运行器。

**依据规格：** `docs/superpowers/specs/2026-07-17-ability-mode-design.md` 第 3～5 节。

**执行保护：** 开始前先运行 `git status --short --branch`，保留当前工作区内与本阶段无关的用户改动。每次首次修改现有文件前，按项目规则创建 `*.bak-YYYYMMDD-ability-bp` 备份并确认备份被 Git 忽略；新建文件不需要备份。所有新增和修改文件使用 UTF-8。

**已确认默认行为：** 新建普通比赛大厅的异能模式默认关闭，需要管理员手动开启；单挑模式不使用异能，固定跳过角色 Ban 和选角。

---

## Task 1：建立职业目录、配置类型和测试入口

**Files:**

- Create: `web-command-center/src/ability-catalog.ts`
- Create: `web-command-center/src/ability-catalog.test.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/session-manager.ts`
- Modify: `web-command-center/package.json`

**Step 1：先写失败测试**

在 `ability-catalog.test.ts` 验证：目录恰好包含 13 个稳定 ID；每项包含中文名称、被动说明、主动说明、充能模型和目录版本；仅 `istaru` 带 `globalUnique: true`；返回目录副本不能修改内部目录。

运行：

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npx tsx --test src/ability-catalog.test.ts
```

预期：失败，提示模块或导出不存在。

**Step 2：实现最小职业目录**

导出以下稳定 ID：`medic`、`berserker`、`assassin`、`tank`、`istaru`、`capitalist`、`balance`、`glass_cannon`、`utility_specialist`、`commander`、`sky_courier`、`snow_golem`、`witch`。目录只存公开不可变元数据，不存比赛状态。

在 `types.ts` 增加：

```ts
export type AbilityId = /* 13 个字符串字面量 */;
export type ChargeModel = 'A' | 'B' | 'C';
```

在 `MatchOptions` 增加默认配置：

```ts
abilityModeEnabled?: boolean;
abilityBanCountPerTeam?: number;
abilityBanSeconds?: number;
abilityDraftBatchSeconds?: number;
```

`createInitialSession()` 默认异能模式关闭、每队 Ban 1 个、Ban 45 秒、每批选角 30 秒，确保旧比赛流程默认行为不变。

**Step 3：加入统一测试脚本并验证**

在 `package.json` 增加：

```json
"test:ability-mode": "tsx --test src/ability-*.test.ts src/identity/public-sanitization.test.ts"
```

运行：

```powershell
npm run test:ability-mode
npm run typecheck
```

预期：全部通过。

**Step 4：提交**

```powershell
git add web-command-center/src/ability-catalog.ts web-command-center/src/ability-catalog.test.ts web-command-center/src/types.ts web-command-center/src/session-manager.ts web-command-center/package.json
git commit -m "feat(web): add ability catalog and BP options"
```

## Task 2：实现纯 BP 规则服务

**Files:**

- Create: `web-command-center/src/ability-draft-service.ts`
- Create: `web-command-center/src/ability-draft-service.test.ts`
- Modify: `web-command-center/src/types.ts`

**Step 1：定义可序列化状态并写失败测试**

增加明确类型：`AbilityBanState`、`AbilityDraftBatch`、`AbilityDraftState`、`AbilityAssignment`。状态中不得保存 Timer 对象，只保存 `timeoutAt`。

测试以下纯函数接口：

```ts
calculateSafeBanLimit(input): number;
buildAbilityDraftBatches(firstTeam, orderedA, orderedB): AbilityDraftBatch[];
createAbilityBanState(input): AbilityBanState;
updateAbilityBanSelection(state, playerId, abilityIds): RuleResult;
confirmAbilityBan(state, playerId): RuleResult;
resolveAbilityBans(state, random): AbilityBanResolution;
createAbilityDraftState(input): AbilityDraftState;
updateAbilityChoice(state, playerId, abilityId): RuleResult;
confirmAbilityChoice(state, playerId): RuleResult;
finishCurrentAbilityBatch(state, random): RuleResult;
```

覆盖：0 Ban；每人最多 N 个不同职业；确认后不可改；只统计已确认票；末位并列使用注入的随机函数；候选不足不补齐；双方撞 Ban 不补；同队职业唯一；伊斯塔露全场唯一；敌方可重复普通职业；同批并发冲突先确认者成功；超时仅随机补未确认玩家。

运行：

```powershell
npx tsx --test src/ability-draft-service.test.ts
```

预期：失败，提示规则服务未实现。

**Step 2：实现人数自适应批次**

队内顺序使用传入数组，不在服务内猜测队长。重点样例：

- 5v5、A 首选：`A1 → B2 → A2 → B2 → A2 → B1`；
- 4v4、B 首选：`B1 → A2 → B2 → A2 → B1`；
- 5v3：较短队伍用尽后跳过空批次，所有 8 人恰好出现一次；
- 1v1：`首选方1 → 对方1`。

**Step 3：实现安全 Ban 上限**

不要使用容易漏掉伊斯塔露全场唯一约束的简单减法。对 13 个职业的小目录枚举可能形成的最终不同 Ban 集，并调用同一合法分配检查器；返回能保证双方所有席位仍可分配的最大每队 Ban 数。测试 1v1、3v3、5v5 和不等人数。

**Step 4：实现并验证全部规则**

所有随机行为必须通过参数注入 `random: () => number`，测试不得依赖真实随机数。错误结果返回稳定错误码与中文提示，不抛出可由玩家输入触发的异常。

运行：

```powershell
npm run test:ability-mode
npm run typecheck
```

预期：全部通过。

**Step 5：提交**

```powershell
git add web-command-center/src/ability-draft-service.ts web-command-center/src/ability-draft-service.test.ts web-command-center/src/types.ts
git commit -m "feat(web): implement ability BP rules"
```

## Task 3：把新阶段接入状态机和流程管理器

**Files:**

- Create: `web-command-center/src/ability-flow.test.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/state-machine.ts`
- Modify: `web-command-center/src/game-flow-manager.ts`
- Modify: `web-command-center/src/game-timers.ts`
- Modify: `web-command-center/src/game-constants.ts`
- Modify: `web-command-center/src/server.ts`

**Step 1：先写流程失败测试**

验证：

- 异能模式关闭：`SidePick → PreGameSetup`；
- 异能模式开启且 Ban=0：`SidePick → AbilityDraft`；
- 异能模式开启且 Ban>0：`SidePick → AbilityBan → AbilityDraft`；
- 选边权属于 A 时，B 获得职业首选权，反之亦然；
- 全部在线参赛者确认 Ban 后可提前结束；
- BP 完成后进入 `PreGameSetup`；
- 管理员不能直接从未完成的 BP 阶段跳过到 `PreGameSetup`。

运行：

```powershell
npx tsx --test src/ability-flow.test.ts
```

预期：失败，提示新阶段或流程入口尚未实现。

**Step 2：增加阶段和会话字段**

在 `GamePhase` 增加 `AbilityBan`、`AbilityDraft`；在 `GameSession` 增加 `abilityBanState?`、`abilityDraftState?` 和最终 `abilityAssignments`。初始化、重置和终止必须清空这些字段。

**Step 3：接入阶段编排**

在 `performPhaseTransition` 中创建 Ban/选角状态。队内顺序由“队长在前，其余保持 `teams[team].players` 的选人顺序”生成。SidePick 结束时根据配置选择下一阶段，不能改变单挑模式和异能关闭时的旧路径。

**Step 4：接入可恢复超时轮询**

为 Ban 和选角增加 clear/set timer；同时在 `server.ts` 的 1 秒轮询中检查 `timeoutAt`。Timer 只用于及时触发，`timeoutAt` 才是事实来源，以便进程恢复后继续结算。

运行：

```powershell
npx tsx --test src/ability-flow.test.ts
npm run test:ability-mode
npm run typecheck
```

预期：全部通过。

**Step 5：提交**

```powershell
git add web-command-center/src/ability-flow.test.ts web-command-center/src/types.ts web-command-center/src/state-machine.ts web-command-center/src/game-flow-manager.ts web-command-center/src/game-timers.ts web-command-center/src/game-constants.ts web-command-center/src/server.ts
git commit -m "feat(web): integrate ability BP phases"
```

## Task 4：实现 Socket 操作与服务器端授权

**Files:**

- Create: `web-command-center/src/ability-socket-policy.ts`
- Create: `web-command-center/src/ability-socket-policy.test.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/socket-handlers.ts`

**Step 1：先写授权失败测试**

新增事件：`ABILITY_BAN_UPDATE`、`ABILITY_BAN_CONFIRM`、`ABILITY_PICK_UPDATE`、`ABILITY_PICK_CONFIRM`。测试未登录冒用 playerId、管理员/观众投票、非当前批玩家选角、替队友确认、超出选择数、非法职业 ID 和重复确认均被拒绝。

运行：

```powershell
npx tsx --test src/ability-socket-policy.test.ts
```

预期：失败，提示授权策略模块不存在。

**Step 2：实现纯授权策略**

`ability-socket-policy.ts` 只判断 actor、phase、rosterTeam、currentBatch 和输入结构，返回 `{ allowed, code, message }`。Socket 处理器不复制规则，授权通过后调用 `AbilityDraftService`。

**Step 3：接入广播和提前结算**

每次合法更新后广播。Ban 阶段在双方所有在线参赛玩家都确认后可以提前结算，断线玩家不阻止 Ban 提前结算；选角阶段只有当前批次全部席位都确认后才能提前结算，掉线且未确认的席位必须等本批倒计时结束后再随机分配。服务端永远重新校验，不信任前端禁用按钮。

运行：

```powershell
npx tsx --test src/ability-socket-policy.test.ts
npm run test:ability-mode
npm run typecheck
```

预期：全部通过。

**Step 4：提交**

```powershell
git add web-command-center/src/ability-socket-policy.ts web-command-center/src/ability-socket-policy.test.ts web-command-center/src/types.ts web-command-center/src/socket-handlers.ts
git commit -m "feat(web): add secure ability BP socket actions"
```

## Task 5：实现按查看者过滤的保密状态

**Files:**

- Modify: `web-command-center/src/identity/public-sanitization.test.ts`
- Modify: `web-command-center/src/player-utils.ts`

**Step 1：扩展失败测试**

构造 A 队玩家、B 队玩家、管理员和匿名查看者，验证：

- Ban 结算前，A 只能看到 A 的选择和确认详情，不能看到 B 的票型或候选；B 同理；
- 管理员可看到双方完整状态；匿名与观众看不到任一队票型；
- Ban 结算后所有人只能看到最终公开 Ban，不公开历史票型；
- 当前选角批次中，本队能看见队友暂存/确认选择，敌队和观众看不到；
- 批次结束后，该批已确认职业向双方公开；
- 玩家本人的职业始终能看见；完整 SteamID 继续沿用现有隐私规则。

运行：

```powershell
npx tsx --test src/identity/public-sanitization.test.ts
```

预期：新增断言失败。

**Step 2：最小实现过滤器**

不要先浅拷贝再删除嵌套秘密对象；为 `abilityBanState` 和 `abilityDraftState` 显式构造公开 DTO，避免引用泄露。管理员权限仍以服务端 `Player.role` 为准。

**Step 3：验证并提交**

```powershell
npm run test:ability-mode
npm run typecheck
git add web-command-center/src/identity/public-sanitization.test.ts web-command-center/src/player-utils.ts
git commit -m "fix(web): protect hidden ability BP choices"
```

## Task 6：实现 BP 快照保存与版本迁移

**Files:**

- Create: `web-command-center/src/session-persistence.test.ts`
- Modify: `web-command-center/src/session-persistence.ts`

**Step 1：先写持久化失败测试**

把序列化和还原核心提取为可传入对象的纯函数，测试：

- Snapshot v2 保存异能配置、Ban 状态、选角状态、最终分配和 `timeoutAt`；
- 仍不保存 `sessionCode`、`bindCode` 和 Timer；
- v1 快照可迁移，缺少异能字段时采用“模式关闭、无 BP 状态”，不能直接拒绝整个旧快照；
- 恢复已超时状态后，下一次轮询立即结算；
- 恢复未超时状态后保留原绝对截止时间，不重新获得完整倒计时。

运行：

```powershell
npx tsx --test src/session-persistence.test.ts
```

预期：失败，当前 Snapshot v1 不保存 BP 状态，也没有 v1 到 v2 迁移。

**Step 2：实现 Snapshot v2 与 v1 迁移**

保留现有 `live-session-snapshot.json` 路径，并把写入改为“同目录临时文件写完后替换正式文件”，避免进程中断留下半份 JSON。不得改动或删除 `runtime/identity-store.json`。恢复时只清除不可序列化 Timer，不清除 BP `timeoutAt`。

**Step 3：验证并提交**

```powershell
npx tsx --test src/session-persistence.test.ts
npm run test:ability-mode
npm run typecheck
git add web-command-center/src/session-persistence.test.ts web-command-center/src/session-persistence.ts
git commit -m "feat(web): persist and restore ability BP"
```

## Task 7：添加网页 Ban/选角界面

**Files:**

- Create: `web-command-center/public/js/ability-bp-ui.js`
- Create: `web-command-center/scripts/check-ability-bp-ui.cjs`
- Modify: `web-command-center/public/js/lobby-app.js`
- Modify: `web-command-center/public/css/app.css`
- Modify: `web-command-center/public/css/caoren-motion.css`
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/package.json`

**Step 1：先写前端失败检查**

`check-ability-bp-ui.cjs` 使用 Node `vm` 加载无 DOM 依赖的 UI 辅助模块，验证职业说明转义、剩余时间文本、选择上限提示、确认后锁定文案、敌方隐藏占位和批次进度文案。再静态检查 `index.html` 已加载新脚本且版本参数更新。

运行：

```powershell
node scripts/check-ability-bp-ui.cjs
```

预期：失败，新 UI 模块不存在。

**Step 2：实现无状态 UI 辅助模块**

`ability-bp-ui.js` 只接收公开 DTO 并返回安全 HTML/文案，导出到 `window.CaorenAbilityBpUi`，同时兼容 Node 测试。所有玩家名和服务器文案必须 HTML 转义。

**Step 3：接入大厅渲染和操作**

在 `lobby-app.js` 增加 `AbilityBan`、`AbilityDraft` 阶段渲染与四个 Socket 操作函数。界面至少显示：职业卡、被动/主动、A/B/C 模型、当前上限、本人暂存选择、确认按钮、批次顺序、倒计时和敌方隐藏状态。不要显示比赛中的实时充能。

**Step 4：加入响应式与深色样式**

复用现有 `.map-bp-*` 视觉语言，但使用独立 `.ability-bp-*` 类，保证手机宽度下按钮和说明不溢出；为减少动态效果设置和深色主题补齐样式。

**Step 5：加入测试脚本并验证**

在 `package.json` 把 `node scripts/check-ability-bp-ui.cjs` 合入 `test:ability-mode`。

```powershell
npm run test:ability-mode
npm run typecheck
```

预期：全部通过。

**Step 6：提交**

```powershell
git add web-command-center/public/js/ability-bp-ui.js web-command-center/scripts/check-ability-bp-ui.cjs web-command-center/public/js/lobby-app.js web-command-center/public/css/app.css web-command-center/public/css/caoren-motion.css web-command-center/public/index.html web-command-center/package.json
git commit -m "feat(web): add ability Ban and draft UI"
```

## Task 8：添加管理员配置、兼容路径和阶段门禁

**Files:**

- Modify: `web-command-center/src/socket-handlers.ts`
- Modify: `web-command-center/src/game-flow-manager.ts`
- Modify: `web-command-center/public/js/lobby-app.js`
- Modify: `web-command-center/src/ability-flow.test.ts`

**Step 1：先写配置与门禁失败测试**

验证只有管理员能在 Lobby 修改开关、Ban 数、Ban 秒数和选角批次秒数；Ban 数必须为 `0..实时安全上限` 的整数，两个倒计时必须为正整数，不额外虚构未经确认的最大秒数；Ban 数超过实时安全上限时不能开始流程，并返回“当前最多可 Ban X 个”；进入 `AbilityBan` 后配置锁定；单挑模式固定跳过异能 BP；异能关闭后可按现有普通比赛进入赛前准备。

运行：

```powershell
npx tsx --test src/ability-flow.test.ts
```

预期：失败，新增的配置授权和安全上限门禁断言尚未满足。

**Step 2：实现服务端配置动作**

新增明确的 `ADMIN_ACTION` 分支，不接受任意键覆盖 `matchOptions`。开始 BP 前重新根据当前双方人数计算安全上限，不能依赖页面早先显示的值。

**Step 3：实现管理员界面**

在 Lobby 管理区增加易懂控件和安全上限提示；当人数变化时使用最新广播状态刷新提示。配置锁定后只读显示本局规则。

**Step 4：回归旧路径并提交**

```powershell
npm run test:ability-mode
npm run test:lobby-identity
npm run test:match-command-policy
npm run typecheck
git add web-command-center/src/socket-handlers.ts web-command-center/src/game-flow-manager.ts web-command-center/public/js/lobby-app.js web-command-center/src/ability-flow.test.ts
git commit -m "feat(web): add ability BP admin controls and gates"
```

## Task 9：阶段 1 整体验证与文档收口

**Files:**

- Create: `web-command-center/docs/ability-bp-manual-test.md`
- Modify only if failures require fixes: files changed in Task 1～8

**Step 1：运行自动化验证**

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npm run test:ability-mode
npm run test:lobby-identity
npm run test:match-command-policy
npm run typecheck
```

预期：全部退出码为 0。

**Step 2：本地双队视图人工验证**

启动本地服务，用管理员、A 队玩家、B 队玩家和观众四个会话验证：0 Ban 跳过、1 Ban 撞 Ban、并列随机、提前确认、超时、5v5 蛇形批次、敌方隐藏、同队冲突、伊斯塔露全局唯一、刷新重连和进程重启恢复。

把操作步骤和实际结果写入 `web-command-center/docs/ability-bp-manual-test.md`；不得写生产服务器路径、密钥或真实玩家身份数据。

**Step 3：检查工作区边界**

```powershell
git status --short --ignored
git diff --cached --stat
```

确认未暂存已有的桌面客户端改动、私有 Bot 插件、构建产物、日志、备份或运行数据。

**Step 4：提交验证记录**

```powershell
git add web-command-center/docs/ability-bp-manual-test.md
git commit -m "test(web): document ability BP verification"
```

## 阶段完成条件

- 网页 BP 所有自动化测试与类型检查通过；
- 人工验证记录完整；
- 异能关闭和单挑模式没有行为回归；
- 对方未结算 Ban、当前批次选择和完整 SteamID 不泄露；
- 重启恢复不重置截止时间、不丢失已确认选择；
- 最终 `abilityAssignments` 包含所有比赛席位且满足同队唯一、伊斯塔露全场唯一；
- 尚未接入插件同步，赛前页面明确标注“职业配置仅在网页完成，插件同步将在下一阶段实现”，避免误以为已经可以在正式服启用技能。
