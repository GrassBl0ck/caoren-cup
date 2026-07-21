# 草人杯全地图粒子菜单第一阶段 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不接入实际赛事业务的前提下，实现管理员可打开的两层粒子菜单原型，并验证输入保护、资源预载和清理行为。

**Architecture:** 菜单核心拆为纯 C# 领域层和 CounterStrikeSharp 适配层。领域层负责页面定义、返回栈、点击与清理状态，采用无需游戏服务器的测试覆盖；适配层负责命令、实体、预载与玩家生命周期，先以原生粒子验证，再接入独立创意工坊资源包和 MultiAddonManager。

**Tech Stack:** C# / .NET 8、CounterStrikeSharp API 1.0.367、MetaMod、MultiAddonManager、CS2 Workshop Tools、PowerShell。

## Global Constraints

- 使用 UTF-8；不修改或提交 `bot-improver-controller/` 及其构建/部署文件。
- 修改既有文件前创建 `.bak-YYYYMMDD-任务说明` 备份；备份、`bin/`、`obj/`、VPK、ZIP、日志和密钥配置不纳入 Git。
- 菜单随现有 `game-plugin/` 发布，不新建第四个正式游戏 DLL 或 Release ZIP。
- 自定义资源必须由独立 Workshop Addon 分发；不得仅复制到 CounterStrikeSharp 插件目录。
- 正式服务器安装、上传、重启或覆盖必须先取得用户同意。
- 每位玩家只能有一个活跃菜单页面；所有退出路径都解除输入限制并删除该页面实体。

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `game-plugin/Features/ParticleMenu/ParticleMenuContracts.cs` | 页面、项目、动作、会话状态和输入模型。 |
| `game-plugin/Features/ParticleMenu/ParticleMenuNavigator.cs` | 打开、选择、确认、返回、关闭。 |
| `game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs` | `ICaorenFeature` 入口、命令、资源预载与会话生命周期。 |
| `game-plugin/Features/ParticleMenu/ParticleMenuRenderer.cs` | 只创建当前页面的 `info_particle_system` 并统一释放。 |
| `game-plugin/Features/ParticleMenu/ParticleMenuInputController.cs` | 经验证的 UserCmd 输入保护与左键释放防误射。 |
| `game-plugin/Features/ParticleMenu/ParticleMenuConfig.cs` | 模块配置、默认值和限制校验。 |
| `game-plugin.Tests/` | 不依赖 CS2 进程的控制台测试。 |
| `workshop-menu-assets/` | Workshop Tools 资源源码、最小测试地图和发布说明。 |
| `docs/particle-menu/` | 输入 Hook 记录和测试服矩阵。 |

## Task 1: 建立可测试的菜单领域层

**Files:**
- Create: `game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`
- Create: `game-plugin.Tests/Program.cs`
- Create: `game-plugin/Features/ParticleMenu/ParticleMenuContracts.cs`
- Modify: `game-plugin/CaorenCup.sln`

**Interfaces:**
- Produces `MenuActionKind`, `MenuItem`, `MenuPage`, `ParticleMenuState`, `MenuInput`, `MenuTransition`。
- `MenuItem(string id, string label, MenuActionKind action, string? targetPageId = null)`。

- [ ] **Step 1: 写失败测试。**

```csharp
var root = new MenuPage("root", "测试主菜单", [
    new MenuItem("open-child", "打开子页面", MenuActionKind.OpenPage, "child"),
    new MenuItem("close", "关闭", MenuActionKind.Close)
]);
Expect("initial selection", new ParticleMenuState(root.Id).SelectedIndex == 0, failures);
Expect("open target", root.Items[0].TargetPageId == "child", failures);
```

- [ ] **Step 2: 运行失败测试。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 编译失败并指出上述领域类型尚不存在。

- [ ] **Step 3: 实现最小模型。**

```csharp
public enum MenuActionKind { OpenPage, Back, InvokeAction, Close }
public sealed record MenuItem(string Id, string Label, MenuActionKind Action, string? TargetPageId = null);
public sealed record MenuPage(string Id, string Title, IReadOnlyList<MenuItem> Items);
public sealed record ParticleMenuState(string CurrentPageId, int SelectedIndex = 0, bool IsOpen = true);
public sealed record MenuInput(int VerticalSteps, bool ConfirmPressed, bool BackPressed, bool ClosePressed);
public sealed record MenuTransition(ParticleMenuState State, bool RequiresRender, bool RequiresCleanup, string? InvokedActionId = null);
```

- [ ] **Step 4: 验证。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 输出 `All ParticleMenu tests passed.`

- [ ] **Step 5: 提交。**

```powershell
git add game-plugin.Tests game-plugin/Features/ParticleMenu/ParticleMenuContracts.cs game-plugin/CaorenCup.sln
git commit -m "test: add particle menu domain contracts"
```

## Task 2: 实现返回栈与动作解析

**Files:**
- Create: `game-plugin/Features/ParticleMenu/ParticleMenuNavigator.cs`
- Modify: `game-plugin.Tests/Program.cs`

**Interfaces:**
- Produces `ParticleMenuNavigator(IReadOnlyDictionary<string, MenuPage> pages, string rootPageId)`。
- `Apply(MenuInput input)` 返回 `MenuTransition`；`Open(string pageId)` 清空返回栈；`Close()` 返回清理请求。

- [ ] **Step 1: 写失败测试。**

```csharp
var pages = new Dictionary<string, MenuPage> { ["root"] = root, ["child"] = child };
var nav = new ParticleMenuNavigator(pages, "root");
Expect("moves selection", nav.Apply(new MenuInput(1, false, false, false)).State.SelectedIndex == 1, failures);
nav.Open("root");
Expect("opens child", nav.Apply(new MenuInput(0, true, false, false)).State.CurrentPageId == "child", failures);
Expect("back restores root", nav.Apply(new MenuInput(0, false, true, false)).State.CurrentPageId == "root", failures);
Expect("close cleans", nav.Apply(new MenuInput(0, false, false, true)).RequiresCleanup, failures);
```

- [ ] **Step 2: 运行失败测试。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 编译失败，提示 `ParticleMenuNavigator` 不存在。

- [ ] **Step 3: 实现导航器。**

```csharp
public sealed class ParticleMenuNavigator
{
    private readonly IReadOnlyDictionary<string, MenuPage> _pages;
    private readonly Stack<string> _history = new();
    public ParticleMenuState State { get; private set; }
    public ParticleMenuNavigator(IReadOnlyDictionary<string, MenuPage> pages, string rootPageId)
        => (_pages, State) = (pages, new ParticleMenuState(rootPageId));
    public void Open(string pageId) { _history.Clear(); State = new ParticleMenuState(pageId); }
    public MenuTransition Close() => new(State with { IsOpen = false }, false, true);
    public MenuTransition Apply(MenuInput input)
    {
        if (!State.IsOpen || input.ClosePressed) return Close();
        var page = _pages[State.CurrentPageId];
        var selected = Math.Clamp(State.SelectedIndex + input.VerticalSteps, 0, Math.Max(0, page.Items.Count - 1));
        State = State with { SelectedIndex = selected };
        if (input.BackPressed) return _history.Count == 0 ? Close() : GoBack();
        if (!input.ConfirmPressed || page.Items.Count == 0) return new(State, input.VerticalSteps != 0, false);
        var item = page.Items[selected];
        return item.Action switch
        {
            MenuActionKind.OpenPage when item.TargetPageId is not null && _pages.ContainsKey(item.TargetPageId) => OpenChild(item.TargetPageId),
            MenuActionKind.Back => _history.Count == 0 ? Close() : GoBack(),
            MenuActionKind.Close => Close(),
            MenuActionKind.InvokeAction => new(State, false, false, item.Id),
            _ => new(State, false, false)
        };
    }
    private MenuTransition OpenChild(string pageId) { _history.Push(State.CurrentPageId); State = new ParticleMenuState(pageId); return new(State, true, false); }
    private MenuTransition GoBack() { State = new ParticleMenuState(_history.Pop()); return new(State, true, false); }
}
```

`Apply` 必须把选择钳制到 `[0, Items.Count - 1]`；打开前验证目标页存在；根页返回等于关闭；关闭永远请求清理。

- [ ] **Step 4: 验证并提交。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 全部导航断言通过。

```powershell
git add game-plugin/Features/ParticleMenu/ParticleMenuNavigator.cs game-plugin.Tests/Program.cs
git commit -m "feat: add particle menu navigation"
```

## Task 3: 接入配置、管理员命令与统一清理

**Files:**
- Create: `game-plugin/Features/ParticleMenu/ParticleMenuConfig.cs`
- Create: `game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs`
- Create: `game-plugin/module-configs/particle-menu.json`
- Modify: `game-plugin/CaorenCupConfig.cs`
- Modify: `game-plugin/CaorenCupPlugin.cs`
- Modify: `game-plugin.Tests/Program.cs`

**Interfaces:**
- `ParticleMenuSettings` 默认：`Enabled=false`、`Command="css_particlemenu_test"`、`Permission="@css/root"`、`MaxItemsPerPage=8`、`MaxActiveEntitiesPerPlayer=24`、`InputUpdateHz=30`。
- `ParticleMenuFeature.CleanupPlayer(int slot, string reason)` 必须幂等。

- [ ] **Step 1: 写失败测试。**

```csharp
var settings = new ParticleMenuSettings();
Expect("disabled default", !settings.Enabled, failures);
Expect("entity cap default", settings.MaxActiveEntitiesPerPlayer == 24, failures);
Expect("invalid settings fail", !settings.IsValid(out _), failures);
```

- [ ] **Step 2: 运行失败测试。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 编译失败，提示 `ParticleMenuSettings` 不存在。

- [ ] **Step 3: 实现设置并注册模块。**

```csharp
public sealed class ParticleMenuSettings
{
    public bool Enabled { get; set; }
    public string Command { get; set; } = "css_particlemenu_test";
    public string Permission { get; set; } = "@css/root";
    public int MaxItemsPerPage { get; set; } = 8;
    public int MaxActiveEntitiesPerPlayer { get; set; } = 24;
    public int InputUpdateHz { get; set; } = 30;
    public bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(Command)) { error = "command"; return false; }
        if (MaxItemsPerPage is < 1 or > 8) { error = "max-items"; return false; }
        if (MaxActiveEntitiesPerPlayer is < 1 or > 24) { error = "max-entities"; return false; }
        if (InputUpdateHz is < 1 or > 30) { error = "input-hz"; return false; }
        error = string.Empty;
        return true;
    }
}
```

在 `CaorenCupConfig` 添加 `[JsonPropertyName("ParticleMenu")] public ParticleMenuSettings ParticleMenu { get; set; } = new();`，在主插件功能列表最后添加 `new ParticleMenuFeature()`。命令仅允许 `@css/root`；配置关闭只提示管理员。玩家断线、回合开始、地图结束和卸载均走同一 `CleanupPlayer/CleanupAll`。

- [ ] **Step 4: 验证并提交。**

Run: `dotnet build game-plugin/CaorenCup.csproj; dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 两个命令退出码为 0。

```powershell
git add game-plugin/CaorenCupConfig.cs game-plugin/CaorenCupPlugin.cs game-plugin/Features/ParticleMenu game-plugin/module-configs/particle-menu.json game-plugin.Tests
git commit -m "feat: add particle menu feature shell"
```

## Task 4: 实现受预算保护的原生粒子渲染

**Files:**
- Create: `game-plugin/Features/ParticleMenu/ParticleMenuRenderer.cs`
- Modify: `game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs`
- Modify: `game-plugin.Tests/Program.cs`

**Interfaces:**
- `int CountRequiredEntities(int itemCount)` 返回 `2 + itemCount`。
- `bool CanRender(int itemCount, int cap)` 仅当数量不超预算时为真。
- `Render(int playerSlot, MenuPage page)` 与 `Clear(int playerSlot)`。

- [ ] **Step 1: 写失败测试。**

```csharp
Expect("page count", ParticleMenuRenderer.CountRequiredEntities(6) == 8, failures);
Expect("budget rejected", !ParticleMenuRenderer.CanRender(8, 8), failures);
Expect("base layers", ParticleMenuRenderer.CountRequiredEntities(0) == 2, failures);
```

- [ ] **Step 2: 运行失败测试。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 编译失败，提示 `ParticleMenuRenderer` 不存在。

- [ ] **Step 3: 实现预算和实体生命周期。**

```csharp
public static int CountRequiredEntities(int itemCount) => 2 + itemCount;
public static bool CanRender(int itemCount, int cap) => itemCount >= 0 && CountRequiredEntities(itemCount) <= cap;
```

渲染前调用 `Clear(playerSlot)`。通过 `Utilities.CreateEntityByName<CParticleSystem>("info_particle_system")` 创建实体，设置 `EffectName`、`StartActive=true`、位置后调用 `DispatchSpawn()` 和 `AcceptInput("Start")`。每个实体登记在 `Dictionary<int, List<CParticleSystem>>`；清理对有效实体执行 `Stop`、`Remove`，最后删除字典项。原型只用已验证原生粒子 `explosion_c4_short`。

- [ ] **Step 4: 验证并提交。**

Run: `dotnet build game-plugin/CaorenCup.csproj; dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 成功并输出 `All ParticleMenu tests passed.`

```powershell
git add game-plugin/Features/ParticleMenu/ParticleMenuRenderer.cs game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs game-plugin.Tests/Program.cs
git commit -m "feat: render bounded particle menu pages"
```

## Task 5: 先验证输入 Hook，再实现输入保护

**Files:**
- Create: `game-plugin/Features/ParticleMenu/ParticleMenuInputController.cs`
- Modify: `game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs`
- Create: `docs/particle-menu/input-hook-spike.md`

**Interfaces:**
- `Begin(int slot)`、`EndAfterPrimaryReleased(int slot)`、`Clear(int slot)` 可重复调用。
- `IsMenuInputBlocked(int slot)` 只在菜单输入仍应被禁止时为真。

- [ ] **Step 1: 在隔离测试服务器验证当前版本的 UserCmd Hook。**

实现一个不提交的最小探针：菜单打开后记录左键边沿、清除攻击标记，关闭后直到检测到左键松开才恢复。记录实际 API 签名、CS2/CounterStrikeSharp 版本和结果。

Expected: 左键只确认一次，没有子弹、伤害、开火声音或关闭后的误射。

- [ ] **Step 2: Hook 不可行时立即停止。**

把失败复现写入 `docs/particle-menu/input-hook-spike.md`。不得用“开火后补偿”冒充解决；后续任务保持阻塞，等待选择可用原生 Hook 或改用 WASD 菜单。

- [ ] **Step 3: Hook 成功后实现控制器。**

鼠标更新按 `InputUpdateHz` 限频，只在虚拟光标跨越菜单项时重绘。菜单活动时清除攻击、副攻击、使用和切枪；确认调用 `navigator.Apply(new MenuInput(0, true, false, false))`；关闭后仅在左键松开时 `Clear(slot)`。

- [ ] **Step 4: 手工回归并提交。**

Run: 在测试服务器执行 `css_particlemenu_test`，验证打开、选择、返回、关闭、死亡、断线和换图。

Expected: 不遗留视角锁、输入锁或粒子实体。

```powershell
git add game-plugin/Features/ParticleMenu/ParticleMenuInputController.cs game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs docs/particle-menu/input-hook-spike.md
git commit -m "feat: protect game input while particle menu is open"
```

## Task 6: 制作资源包并验证全地图分发

**Files:**
- Create: `workshop-menu-assets/README.md`
- Create: `workshop-menu-assets/particles/`
- Create: `workshop-menu-assets/materials/`
- Create: `workshop-menu-assets/icons/`
- Create: `workshop-menu-assets/maps/`
- Create: `docs/particle-menu/phase1-test-matrix.md`
- Modify: `game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs`

**Interfaces:**
- 资源路径固定为 `particles/caoren_menu/menu_root.vpcf` 和 `particles/caoren_menu/menu_highlight.vpcf`。
- 在 `OnServerPrecacheResources` 预载这两个路径。

- [ ] **Step 1: 用 Workshop Tools 创建并编译最小资源包。**

资源包至少含背景、一个条目和高亮；最小测试地图能播放两个菜单粒子。README 记录源码、编译产物、Workshop ID、包大小和发布日期，但不记录服务器路径、IP、密钥或部署命令。

- [ ] **Step 2: 本地验证资源。**

Run: 在 Workshop Tools 打开最小测试地图，播放 `menu_root.vpcf` 和 `menu_highlight.vpcf`。

Expected: 无棋盘格缺失材质、无资源加载错误，两个粒子可见。

- [ ] **Step 3: 获得用户同意后仅在测试服务器安装 MultiAddonManager。**

配置 `mm_extra_addons "<资源包 ID>"`，不使用 `mm_client_extra_addons`；创建服务器配置备份，下载挂载后重载测试地图。

- [ ] **Step 4: 按测试矩阵验证。**

在一张官方图和一张既有创意工坊图中，分别让未下载资源与已缓存资源的客户端打开菜单。记录首次下载时间、重连、换图可见性、资源错误、平均 FPS 和 1% Low FPS。

- [ ] **Step 5: 只提交可公开源码和文档。**

```powershell
git add workshop-menu-assets docs/particle-menu/phase1-test-matrix.md game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs
git status --short --ignored
git diff --cached --stat
git commit -m "feat: add particle menu workshop assets"
```

## Task 7: 安全降级与验收

**Files:**
- Modify: `game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs`
- Modify: `game-plugin/module-configs/particle-menu.json`
- Modify: `game-plugin.Tests/Program.cs`
- Modify: `docs/particle-menu/phase1-test-matrix.md`

**Interfaces:**
- `MenuOpenResult CanOpen(ParticleMenuSettings settings, int itemCount)`。
- 资源预载失败、实体预算超限或渲染异常时必须清理且不得锁定输入。

- [ ] **Step 1: 写失败测试。**

```csharp
var result = ParticleMenuFeature.CanOpen(new ParticleMenuSettings { MaxActiveEntitiesPerPlayer = 2 }, 1);
Expect("over-budget rejected", !result.Allowed && result.Reason == "entity-budget", failures);
```

- [ ] **Step 2: 运行失败测试。**

Run: `dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 编译失败，提示 `CanOpen` 或 `MenuOpenResult` 不存在。

- [ ] **Step 3: 实现打开守卫。**

```csharp
public static MenuOpenResult CanOpen(ParticleMenuSettings settings, int itemCount)
    => ParticleMenuRenderer.CanRender(itemCount, settings.MaxActiveEntitiesPerPlayer)
        ? new(true, null)
        : new(false, "entity-budget");
```

在创建任何实体或输入锁之前调用；所有异常均调用 `CleanupPlayer(slot, "open-failed")` 并提示管理员使用文字备用菜单。

- [ ] **Step 4: 本地验证和测试服验收。**

Run: `dotnet build game-plugin/CaorenCup.csproj; dotnet run --project game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj`

Expected: 构建与测试均通过；测试矩阵的全部项目有结果。

- [ ] **Step 5: 提交。**

```powershell
git add game-plugin/Features/ParticleMenu/ParticleMenuFeature.cs game-plugin/module-configs/particle-menu.json game-plugin.Tests docs/particle-menu/phase1-test-matrix.md
git commit -m "fix: add particle menu safe fallback"
```

## 覆盖自检

- 全地图资源分发：Task 6。
- 菜单层级与返回栈：Task 1-2。
- 模块接入、配置与统一清理：Task 3。
- 当前页渲染和性能预算：Task 4、Task 7。
- 锁视角、禁止开枪和左键释放防误射：Task 5。
- 死亡、断线、换图、异常清理：Task 3、Task 5、Task 7。
- 下载、重连、换图和低配性能验证：Task 6-7。
