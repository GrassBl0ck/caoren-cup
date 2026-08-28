# 普通不平衡竞技目标人数 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为普通网页比赛增加可配置的 A/B 目标人数（支持 3v5），并在赛前关键节点阻止人数、分队或 SteamID 不完整的比赛开始。

**Architecture:** 在 `matchOptions` 保存不平衡竞技开关和目标人数；新增纯函数校验模块供流程和 API 复用。前端在大厅设置和分队面板显示目标与实时状态，后端在阶段推进、赛前开始和 team-lock 同步时执行同一套门禁。单挑模式绕过该校验并保持原逻辑。

**Tech Stack:** TypeScript、Node.js、原生 HTML/JavaScript、Node test runner、npm run check。

## Global Constraints

- 单挑模式不读取目标人数配置，不改变现有单挑分队和等待逻辑。
- 平衡竞技缺少目标字段时必须兼容旧会话，不增加新的阻断。
- 管理员和旁观者不计入目标人数。
- 不执行 Git 提交、推送、部署或外部应用操作。

---

### Task 1: 增加目标人数状态与共享校验

**Files:**
- Create: `web-command-center/src/unbalanced-roster.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/session-manager.ts`
- Test: `web-command-center/src/unbalanced-roster.test.ts`

**Interfaces:**
- Produces `normalizeUnbalancedRosterOptions(raw)` and `validateUnbalancedRoster(session)`，返回 `{ valid: boolean; blockers: string[]; targetA: number; targetB: number; actualA: number; actualB: number; unassigned: number; missingSteamIds: string[] }`。

- [ ] 写测试覆盖：旧会话默认平衡兼容、3v5 满足、4v4 不满足、未分队、总人数不等、未绑定 SteamID、管理员/旁观者排除。
- [ ] 运行 `npm test -- --test-name-pattern=unbalanced-roster`，确认新测试先失败。
- [ ] 在 `types.ts` 的 `MatchOptions` 增加 `unbalancedModeEnabled?: boolean`、`unbalancedTeamASize?: number`、`unbalancedTeamBSize?: number`；在 session 默认值中补兼容默认。
- [ ] 实现纯函数：仅当 `matchMode !== 'duel'` 且不平衡开关为真时启用；目标为正整数，实际 A/B、未分队、总人数和 SteamID 分别生成中文 blocker。
- [ ] 再运行定向测试，确认通过。

### Task 2: 接入大厅设置与阶段门禁

**Files:**
- Modify: `web-command-center/src/socket-handlers.ts`
- Modify: `web-command-center/src/game-flow-manager.ts`
- Modify: `web-command-center/src/plugin-api.ts`
- Test: `web-command-center/src/unbalanced-roster-flow.test.ts`

**Interfaces:**
- Consumes `validateUnbalancedRoster(session)`。
- Produces：保存模式时接收目标人数；阶段推进失败时通过现有通知机制返回 blocker；team-lock sync 遇到未绑定玩家时返回 400。

- [ ] 写流程测试：大厅保存 3v5；4v4 推进 MapBan 被拒；3v5 可推进；玩家退出后 PreGameSetup/正式开始被拒；team-lock 未绑定被拒。
- [ ] 运行定向测试确认失败。
- [ ] 在模式保存 action 中仅允许 Lobby 修改目标人数，校验正整数；切换平衡竞技时清理或忽略目标字段。
- [ ] 在 `advancePhase` 进入 MapBan、PreGameSetup 和 LiveGame 前调用共享校验；duel 和平衡竞技直接跳过。
- [ ] 在 team-lock API 的 `enqueueTeamAssignments` 前执行校验，并把 blocker 拼接到现有中文错误中；未绑定不再部分下发。
- [ ] 运行定向测试确认通过。

### Task 3: 更新网页设置、分队状态和错误展示

**Files:**
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/js/lobby-app.js`

**Interfaces:**
- Consumes socket state 中的 `matchOptions.unbalancedModeEnabled/unbalancedTeamASize/unbalancedTeamBSize` 及玩家 roster/steamIdBound。
- Produces：管理员可配置目标人数；分队区显示目标、实时计数、未分队和未绑定提示；平衡竞技和单挑保持原文案与按钮行为。

- [ ] 在模式设置增加“平衡竞技 / 不平衡竞技”控件及 A/B 正整数输入，保存时随现有 `saveMatchOptions` 发送。
- [ ] 在分队面板增加目标人数摘要和 blocker 展示；不平衡模式显示“达标/还差 N 人”。
- [ ] 在保存/刷新和阶段切换后禁用已锁定的目标输入，保持大厅阶段可编辑。
- [ ] team-lock 状态区展示后端返回的具体 blocker。
- [ ] 用静态检查确认单挑控制和旧平衡流程没有被条件误伤。

### Task 4: 验证与差异检查

**Files:**
- No new files.

- [ ] 在 `web-command-center` 运行 `npm test` 或项目现有测试命令，确认相关测试通过。
- [ ] 运行 `npm run check`。
- [ ] 执行 `git diff --check`、`git status --short --ignored`，确认没有构建产物、备份、日志、密钥或暂停组件改动。
- [ ] 检查残留引用：目标字段、校验函数、前端控件和错误文案均有调用，单挑路径无意外引用。
