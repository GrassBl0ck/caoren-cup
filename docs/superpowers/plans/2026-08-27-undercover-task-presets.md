# 卧底任务多预设 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为网页端卧底任务模板增加服务端共享的多预设管理能力。

**Architecture:** 新增独立的预设存储模块，将预设保存到 `web-command-center/runtime/undercover-task-presets.json`，服务启动时初始化或恢复系统默认预设。通过现有 Socket.IO `ADMIN_ACTION` 通道提供列表、新建、应用、重命名、删除操作；当前对局仍使用 `session.taskTemplate`，前端在现有模板编辑器中增加预设管理控件。

**Tech Stack:** TypeScript、Node.js `fs`、Socket.IO、现有网页端原生 JavaScript、Node test runner、npm scripts。

## Global Constraints

- 预设共享范围为当前网页端服务实例，不引入数据库。
- “系统默认”预设固定存在且不可重命名、不可删除。
- 预设管理和应用仅允许管理员在大厅或身份发放前执行。
- 应用预设必须清空卧底任务进度、操作记录、计数和确认状态。
- 修改文件前先创建不纳入 Git 的备份；不执行 Git 提交、推送、部署或服务器操作。

---

### Task 1: 建立预设存储模块

**Files:**
- Create: `web-command-center/src/task-preset-store.ts`
- Test: `web-command-center/src/task-preset-store.test.ts`

**Interfaces:**
- `TaskPresetRecord { id: string; name: string; taskTemplate: TaskTemplate; createdAt: number; updatedAt: number; system?: boolean }`
- `loadTaskPresets(): TaskPresetRecord[]`
- `saveTaskPresets(presets: TaskPresetRecord[]): void`
- `createTaskPreset(name: string, taskTemplate: TaskTemplate): TaskPresetRecord`
- `renameTaskPreset(id: string, name: string): TaskPresetRecord`
- `deleteTaskPreset(id: string): void`
- `getTaskPreset(id: string): TaskPresetRecord | undefined`

- [ ] **Step 1: 先备份相关现有文件**

在仓库外不操作；在仓库内为将修改的 `socket-handlers.ts`、`types.ts` 和 `lobby-app.js` 创建带时间戳的 `.bak-*` 文件，并确认这些备份不会加入 Git。

- [ ] **Step 2: 编写存储失败测试**

覆盖文件不存在时生成系统默认、正常读写、重复/空名称拒绝、系统默认不可删除，以及损坏 JSON 备份后恢复默认。测试通过注入临时路径或导出的路径工厂隔离真实 `runtime` 文件。

- [ ] **Step 3: 实现原子 JSON 存储**

实现 `TaskPresetRecord` 及上述函数；默认记录使用固定 ID `system-default` 和 `getDefaultTaskTemplate()`。写文件时先写 `${path}.tmp`，再 `renameSync`；损坏文件先改名为 `undercover-task-presets.corrupt-<timestamp>.json`。

- [ ] **Step 4: 运行专项测试**

运行：`npm --prefix web-command-center exec tsx --test src/task-preset-store.test.ts`

预期：所有存储初始化、校验、恢复测试 PASS。

### Task 2: 接入会话与 Socket 管理接口

**Files:**
- Modify: `web-command-center/src/socket-handlers.ts`
- Modify: `web-command-center/src/types.ts`
- Modify: `web-command-center/src/server.ts`
- Test: `web-command-center/src/task-preset-logic.test.ts`

**Interfaces:**
- 新增 Socket 管理动作：`LIST_TASK_PRESETS`、`CREATE_TASK_PRESET`、`RENAME_TASK_PRESET`、`DELETE_TASK_PRESET`、`APPLY_TASK_PRESET`。
- 列表响应只返回预设元数据和模板摘要所需字段；应用和保存失败通过现有 `WsEvents.NOTIFICATION` 返回中文错误。

- [ ] **Step 1: 编写 Socket 行为失败测试**

测试管理员可以列出/创建/重命名/删除普通预设；非管理员、比赛中、空名称、重复名称、删除系统默认和不存在 ID 均被拒绝；应用预设会替换 `session.taskTemplate` 并重置卧底玩家的 `taskGrid`、`taskActionLog`、计数、`isReady` 和 `undercoverTaskAckStage`。

- [ ] **Step 2: 接入启动初始化**

在 `server.ts` 启动恢复会话后调用 `loadTaskPresets()`，确保预设文件首次启动自动生成；不得把预设列表塞入 `GameSession` 快照。

- [ ] **Step 3: 实现管理员动作**

在现有 `ADMIN_ACTION` 分支加入权限、阶段和参数校验；创建使用客户端传来的编辑模板，应用使用存储中的深拷贝模板并复用现有 `UPDATE_TASK_TEMPLATE` 的任务重置逻辑；每次成功写入后广播状态或单独返回列表。

- [ ] **Step 4: 运行后端专项与现有卧底测试**

运行：`npm --prefix web-command-center exec tsx --test src/task-preset-logic.test.ts src/undercover-logic.test.ts src/session-persistence.test.ts`

预期：新增行为和现有卧底/会话持久化测试全部 PASS。

### Task 3: 增加模板编辑器预设 UI

**Files:**
- Modify: `web-command-center/public/index.html`
- Modify: `web-command-center/public/js/lobby-app.js`
- Modify: `web-command-center/public/css/app.css`（仅在现有样式不足时增加预设区样式）
- Test: `web-command-center/scripts/check-task-preset-ui.cjs`

**Interfaces:**
- 前端维护 `window._taskPresets` 和当前选中预设 ID。
- 暴露 `refreshTaskPresets`、`createTaskPresetFromEditor`、`applySelectedTaskPreset`、`renameSelectedTaskPreset`、`deleteSelectedTaskPreset`。

- [ ] **Step 1: 编写静态 UI 检查**

检查页面包含预设列表、应用、另存为、重命名、删除控件；系统默认项没有删除/重命名入口；现有 JSON 导入导出和本地备份按钮仍存在。

- [ ] **Step 2: 添加预设区域**

在任务模板编辑弹窗顶部放置列表和操作按钮，沿用现有按钮、通知和确认弹窗风格；根据当前阶段和管理员身份禁用操作。

- [ ] **Step 3: 接入 Socket 列表和操作**

打开模板编辑器或收到登录后的管理员状态时发送 `LIST_TASK_PRESETS`；处理列表响应，渲染名称、系统标记和更新时间；另存为读取当前编辑器模板，应用前调用现有危险确认，重命名/删除使用轻量确认。

- [ ] **Step 4: 运行 UI 检查**

运行：`node web-command-center/scripts/check-task-preset-ui.cjs`

预期：输出 `task preset UI checks passed`。

### Task 4: 持久化与回归验证

**Files:**
- Modify: `web-command-center/src/session-persistence.test.ts`（如需补充兼容断言）
- Modify: `web-command-center/package.json`（仅增加专项测试脚本）

- [ ] **Step 1: 补充服务重启场景测试**

写入一个普通预设，重新通过存储模块加载，断言名称、模板内容和系统默认记录均保留；删除普通预设后确认当前会话模板不变。

- [ ] **Step 2: 检查快照边界**

确认 `live-session-snapshot.json` 仍只保存当前 `session.taskTemplate`，预设列表位于独立文件；旧快照恢复不因缺少预设文件失败。

- [ ] **Step 3: 运行完整网页端检查**

运行：`npm --prefix web-command-center run check`

预期：类型检查、卧底逻辑、任务 UI、会话持久化及现有全部检查 PASS。

- [ ] **Step 4: 检查实际差异和忽略项**

运行：`git status --short --ignored`、`git diff --stat`、`git diff --check`；确认没有把 `runtime/undercover-task-presets.json`、备份文件、日志或构建产物纳入改动。

## 验收标准

- 管理员能创建至少两个命名预设，并在大厅或身份发放前切换。
- 普通玩家无法执行任何预设管理动作。
- 比赛开始后应用、重命名和删除均被拒绝。
- 应用预设会重置卧底任务状态并要求重新确认。
- 服务重启后预设仍可读取；系统默认始终存在且不可删除。
- 原有 JSON 导入/导出、浏览器本地备份、撤销/重做和现有卧底流程不受影响。
