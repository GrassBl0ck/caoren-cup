# 归档网页单挑并保留游戏内 `/duel` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将网页单挑专属代码、测试和文档迁移到 archive，只保留游戏内 `/duel`，并让 `/duel stop` 直接结束。

**Architecture:** 先建立完整备份和归档目录，再从共享 TypeScript/C# 文件中移除网页管理分支，保留 GameManaged 单挑运行层。前端、API、状态类型和持久化只保留普通比赛路径；桥接插件保留游戏内指令、规则和清理。

**Tech Stack:** TypeScript、原生 JavaScript、C#、Node test runner、dotnet build、PowerShell 文件移动。

## Global Constraints

- 归档优先于删除，归档文件保留原内容和相对路径。
- 不修改普通竞技、卧底、不平衡竞技和游戏内 `/duel` 的运行规则。
- `/duel stop` 直接终止；`/duel stop confirm` 不再识别。
- 不执行 Git 提交、推送、Release 或部署。

---

### Task 1: 备份并迁移网页单挑专属文件

**Files:**
- Create: `archive/web-command-center-duel/2026-08-28/`
- Move: `web-command-center/src/duel-runtime-config.ts`
- Move: `web-command-center/src/duel-runtime-config.test.ts`
- Move: 网页单挑专属设计/测试文档

- [ ] 创建带时间戳的工作区备份，备份不进入 Git。
- [ ] 将独立网页单挑文件移动到 archive，并记录原路径映射。
- [ ] 从生产路径删除仅供网页单挑使用的文档和测试入口。
- [ ] 运行 `rg -n -i "网页单挑|WebManaged|CONFIGURE_DUEL_MODE"`，确认剩余命中均位于待处理共享文件或 archive。

### Task 2: 清理网页前端、状态和 API

**Files:**
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/js/lobby-app.js`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/session-manager.ts`
- Modify: `web-command-center/src/server.ts`
- Modify: `web-command-center/src/routes/match-options-routes.ts`
- Modify: `web-command-center/src/game-flow-manager.ts`
- Modify: `web-command-center/src/plugin-api.ts`
- Modify: `web-command-center/src/socket-handlers.ts`

- [ ] 移除网页单挑模式控件、配置字段、地图/回合/道具面板和网页单挑状态展示。
- [ ] 删除 `matchMode: 'duel'` 的网页入口、自动跳转、网页等待和 `CONFIGURE_DUEL_MODE` 下发。
- [ ] 保留普通竞技的 `matchzy`、卧底、锁队及不平衡竞技目标人数流程。
- [ ] 让旧会话遇到历史单挑状态时安全回到普通大厅，不执行网页单挑逻辑。
- [ ] 运行 `npm run typecheck` 和相关网页测试，确认通过。

### Task 3: 清理桥接插件的 WebManaged 分支并简化 stop

**Files:**
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs`
- Modify: `web-command-center/CaorenCupPlugin/DuelGameSession.cs`
- Modify: `web-command-center/CaorenCupPlugin/DuelRuntimePolicy.cs`
- Modify: `web-command-center/CaorenCupPlugin/WebCommandGameThreadDispatcher.cs`
- Move: WebManaged 专属 C# 测试/辅助文件到 archive

- [ ] 保留 GameManaged 的 `/duel` 解析、参赛者收集、暂停/恢复、状态、地图和武器指令。
- [ ] 删除 WebManaged 控制模式、网页配置接收和网页遥测投影分支；共享运行规则保留在生产路径。
- [ ] 删除 `stop confirm` 二次确认解析和提示，让 `stop` 直接调用终止清理。
- [ ] 更新 C# 单元测试：新增 `stop` 直接结束断言，删除 confirm 相关断言。
- [ ] 运行桥接插件 `dotnet build` 和相关测试。

### Task 4: 文档、残留检查与最终验证

**Files:**
- Modify/Move: `README.md`、`docs/duel-mode-test-flow.md`、相关单挑设计/计划文档

- [ ] 文档改为“单挑由游戏内 `/duel` 管理”，网页端不再列为入口。
- [ ] 生产路径搜索不得出现网页单挑入口、`WebManaged`、`CONFIGURE_DUEL_MODE` 或 `/duel stop confirm`。
- [ ] 检查 archive 内容完整、生产路径无误移动、运行时数据和密钥未被触碰。
- [ ] 执行网页 `npm run check`、桥接插件构建；记录已有无关失败，不宣称全套通过。
- [ ] 执行 `git diff --check` 和 `git status --short --ignored`，确认无构建产物、备份或密钥进入 Git。
