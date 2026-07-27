# 草人杯 SwiftlyS2 粒子菜单第一阶段实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在独立本地项目中构建 SwiftlyS2 两层可点击粒子测试菜单，并在获得单独授权后于测试服验证与现有 CounterStrikeSharp/CaorenCup 的共存。

**Architecture:** 现有 CaorenCup 继续使用 CounterStrikeSharp，绝不迁移。独立 SwiftlyS2 插件使用 ProcessUserCmd 处理输入、虚拟光标、视角锁和攻击拦截；第一阶段不跨框架调用任何草人杯业务。

**Tech Stack:** 新版 SwiftlyS2（起始核查提交 2093caed6eba0a483712fb090a72b79b227d8449）、C# 托管插件、官方模板指定的 .NET 版本、CS2、MetaMod。

## Global Constraints

- 私有项目固定为 D:\OpenSourcework\caoren-particle-menu-swiftlys2，绝不进入 caoren-cup-open-source。
- 不修改或提交 bot-improver-controller；所有文本与代码使用 UTF-8。
- 既有文件修改前先备份；备份、构建产物、日志、VPK、ZIP、密钥配置不提交。
- 创建私有目录、下载 SwiftlyS2、安装到任何服务器、上传 Workshop 或修改服务器配置，都必须先取得用户单独明确同意。
- 不热卸载或热重载 SwiftlyS2 或 CounterStrikeSharp；共存测试只允许完整重启。

---

## 文件结构

- src/CaorenParticleMenu/MenuState.cs：两层菜单、选择和关闭等待状态机。
- src/CaorenParticleMenu/MenuPlugin.cs：SwiftlyS2 生命周期和 ProcessUserCmd Hook。
- src/CaorenParticleMenu/MenuRenderer.cs：当前页面粒子创建、切页释放和清理。
- src/CaorenParticleMenu/CompatibilityProbe.cs：框架共存和异常诊断。
- tests/MenuStateTests.cs：不依赖 CS2 的状态机测试。
- docs/coexistence-test-plan.md：测试服矩阵。
- docs/rollback.md：失败回滚步骤。

## Task 1: 创建私有项目的授权门

**Files:**
- Create after authorization: D:\OpenSourcework\caoren-particle-menu-swiftlys2\

- [ ] **Step 1: 向用户说明精确影响。**

将创建私有本地目录，并下载新版 SwiftlyS2 官方源码与模板依赖；不会改公开仓库或连接服务器。

- [ ] **Step 2: 等待明确授权。**

Expected: 用户明确同意创建目录并下载新版 SwiftlyS2。

- [ ] **Step 3: 创建并固定版本。**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\OpenSourcework\caoren-particle-menu-swiftlys2' -ErrorAction Stop
git clone https://github.com/swiftly-solution/swiftlys2.git 'D:\OpenSourcework\caoren-particle-menu-swiftlys2\vendor\swiftlys2'
git -C 'D:\OpenSourcework\caoren-particle-menu-swiftlys2\vendor\swiftlys2' checkout 2093caed6eba0a483712fb090a72b79b227d8449
git -C 'D:\OpenSourcework\caoren-particle-menu-swiftlys2\vendor\swiftlys2' rev-parse HEAD
```

Expected: 输出固定提交号。

## Task 2: 两层菜单状态机

**Files:**
- Create: D:\OpenSourcework\caoren-particle-menu-swiftlys2\src\CaorenParticleMenu\MenuState.cs
- Create: D:\OpenSourcework\caoren-particle-menu-swiftlys2\tests\MenuStateTests.cs

**Interfaces:**
- MenuPageId：Root、Child。
- MenuPhase：Closed、Active、ClosingWaitRelease。
- MenuState.Apply(MenuInput input) 返回 MenuTransition。

- [ ] **Step 1: 写失败测试。**

```csharp
var state = MenuState.OpenRoot();
Assert.Equal(MenuPageId.Root, state.Page);
Assert.Equal(MenuPageId.Child, state.Apply(MenuInput.ConfirmRootOpen).State.Page);
Assert.Equal(MenuPageId.Root, state.Apply(MenuInput.Back).State.Page);
Assert.Equal(MenuPhase.ClosingWaitRelease, state.Apply(MenuInput.CloseWhileHeld).State.Phase);
Assert.Equal(MenuPhase.Closed, state.Apply(MenuInput.ReleaseAttack).State.Phase);
```

- [ ] **Step 2: 运行测试确认失败。**

Run: 使用 Task 1 确定的官方 SwiftlyS2 模板测试命令。

Expected: 因 MenuState 不存在而失败。

- [ ] **Step 3: 最小实现。**

Root 第一项进入 Child，Child 第一项返回 Root，每页第二项请求关闭。关闭后必须停在 ClosingWaitRelease，只有原始左键松开才 Closed 并请求清理。

- [ ] **Step 4: 运行测试确认通过并提交私有项目。**

Expected: 所有状态机断言通过；私有项目不配置公开 remote。

## Task 3: ProcessUserCmd 输入探针与安全拦截

**Files:**
- Create: src/CaorenParticleMenu/MenuPlugin.cs
- Create: src/CaorenParticleMenu/CompatibilityProbe.cs

- [ ] **Step 1: 先实现只记录命令的前置 Hook。**

读取并记录 CSGOUserCmdPB 的 Buttonstate1、Buttonstate2、Buttonstate3、Mousedx、Mousedy、Viewangles、InputHistory 与攻击历史索引；日志不得包含 IP、令牌或服务器私密路径。

- [ ] **Step 2: 验证 Hook 行为。**

Expected: 左键单击/长按能识别攻击边沿，鼠标位移可读，且 Hook 在武器逻辑前执行。

- [ ] **Step 3: 实现菜单期间命令净化。**

活动或 ClosingWaitRelease 状态下清除 Attack、Attack2、Use、Weapon1、Weapon2、Weapon3；保存打开菜单时的 Viewangles 并逐命令写回；鼠标增量仅更新菜单光标。

- [ ] **Step 4: 验证武器安全。**

用步枪、手枪、狙击枪、刀和投掷物测试单击、长按、连点、按住左键关闭。

Expected: 无 weapon_fire、弹药变化、伤害、枪声、弹孔或投掷物；松开后下一次全新按下才可开火。

## Task 4: 原生粒子菜单与清理

**Files:**
- Create: src/CaorenParticleMenu/MenuRenderer.cs
- Modify: src/CaorenParticleMenu/MenuPlugin.cs

- [ ] **Step 1: 写实体预算测试。**

```csharp
Assert.True(MenuEntityBudget.CanRender(2, 12));
Assert.False(MenuEntityBudget.CanRender(11, 12));
Assert.Equal(4, MenuEntityBudget.RequiredEntities(2));
```

- [ ] **Step 2: 实现预算。**

```csharp
public static int RequiredEntities(int itemCount) => itemCount + 2;
public static bool CanRender(int itemCount, int maxEntities) =>
    itemCount >= 0 && RequiredEntities(itemCount) <= maxEntities;
```

- [ ] **Step 3: 使用 CS2 原生粒子渲染 Root 与 Child。**

切页顺序固定为 Clear(player) 后 Render(player)。死亡、断线、换图、插件停止和捕获异常均调用相同 Clear(player)。超预算时拒绝打开且不启用输入锁。

- [ ] **Step 4: 本地验证。**

Expected: 菜单、返回、关闭和所有清理路径均无粒子残留。

## Task 5: 获授权后的测试服共存验证

- [ ] **Step 1: 先向用户申请测试服安装授权。**

影响：测试服同时安装新版 SwiftlyS2 和私有菜单插件；不改正式服、不上传 Workshop、不接业务。

- [ ] **Step 2: 获同意后完整备份。**

备份测试服务器相关插件和配置，计算 SHA-256；备份不提交。

- [ ] **Step 3: 执行完整重启矩阵。**

依次测试：仅 SwiftlyS2；仅 CSS/CaorenCup；两者加载顺序 A；两者加载顺序 B。每种组合测试重启、连续换图、进出服务器、死亡、网页桥接、菜单操作和数小时运行。

- [ ] **Step 4: 失败回滚。**

停止加载 SwiftlyS2 菜单插件，恢复测试服备份，完整重启；确认 CSS/CaorenCup 与网页桥接正常。不得热卸载框架。

## 覆盖自检

- 私有目录与公开仓库隔离：Task 1。
- 两层菜单与关闭等待：Task 2。
- 鼠标、视角锁、攻击前拦截：Task 3。
- 原生粒子、实体预算、统一清理：Task 4。
- CSS/SwiftlyS2 共存与回滚：Task 5。
- Workshop/MultiAddonManager 在第一阶段通过后再单独设计。
