# Duel Runtime Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 统一修复网页单挑和游戏内独立单挑的额外枪械、回合默认持刀和官方赛后地图投票问题。

**Architecture:** 桥接插件成为两种单挑临时 CVar、装备规则和结束清理的唯一管理者；纯 C# 策略类负责合法武器、默认槽位和引擎保护回合数。网页端只发送完整单挑配置，桥接插件按“最终事件先入队、重启、首个新回合再恢复 CVar”的两阶段顺序收尾。

**Tech Stack:** C# 12 / .NET 8 / CounterStrikeSharp API 367 / xUnit 2.9 / TypeScript 5.4 / Node.js `node:test` / `tsx`

## Global Constraints

- 所有文本文件使用 UTF-8；修改现有文件前先创建不会纳入 Git 的 `*.bak-20260727-duel-runtime-polish` 备份。
- 游戏内独立单挑不得向网页发布比赛、比分、快照或赛后记录；网页单挑必须保留最终回合和赛后统计。
- 两种单挑都禁止地图枪、主动丢枪和死亡掉枪，但允许合法主武器、副武器、刀和投掷物之间切换。
- 手枪阶段默认拿副武器；步枪和狙击阶段默认拿主武器；切枪最多补试一次。
- 插件配置总回合数保持不变，引擎回合上限固定为 `TotalRounds + 1`。
- 最终回合不得进入 CS2 官方赛后界面或地图投票；危险 CVar 只能在清理重启后的首个 `round_start` 恢复。
- 不修改 `game-plugin/`、`desktop-client/` 或玩家可见网页界面。
- 不提交 `bin/`、`obj/`、`node_modules/`、备份、日志、ZIP、私有配置或 `release-*` 目录。
- 本计划不授权推送、Release 或服务器部署。

---

## File Structure

- Create: `web-command-center/CaorenCupPlugin/DuelRuntimePolicy.cs` — 纯逻辑：合法枪械、首选武器、丢枪判断、引擎回合上限和临时 CVar 计划。
- Create: `web-command-center/CaorenCupPlugin/DuelCleanupState.cs` — 纯状态：两阶段清理是否等待重启回合、原控制模式和幂等消费。
- Create: `web-command-center/CaorenCupPlugin.Tests/DuelRuntimePolicyTests.cs` — 武器与 CVar 策略测试。
- Create: `web-command-center/CaorenCupPlugin.Tests/DuelCleanupStateTests.cs` — 两阶段清理状态测试。
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs` — 共享运行时激活、丢枪拦截、配装校验、默认切枪、网页/游戏内最终回合和两阶段清理。
- Modify: `web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs` — 保持通用捕获/恢复职责，补充批量应用入口。
- Modify: `web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs` — 插件集成边界、命令顺序和遥测隔离回归。
- Create: `web-command-center/src/duel-runtime-config.ts` — 构造网页单挑传给桥接插件的唯一配置命令。
- Create: `web-command-center/src/duel-runtime-config.test.ts` — 网页配置负载和命令收敛测试。
- Modify: `web-command-center/src/game-flow-manager.ts` — 删除网页端重复 CVar 命令，改用配置构造器。
- Modify: `web-command-center/package.json` — 增加单挑运行时测试脚本。
- Modify: `scripts/test-duel-mode-local.ps1` — 将新网页测试纳入本地门禁。
- Modify: `docs/duel-mode-test-flow.md` — 补充真人武器和赛后界面回归项。

---

### Task 1: Add pure duel runtime policy

**Files:**
- Create: `web-command-center/CaorenCupPlugin/DuelRuntimePolicy.cs`
- Create: `web-command-center/CaorenCupPlugin.Tests/DuelRuntimePolicyTests.cs`

**Interfaces:**
- Consumes: existing internal `DuelStage` and public `DuelGameConfig`.
- Produces:
  - `internal enum DuelPreferredWeaponSlot { Primary, Secondary }`
  - `internal sealed record DuelLoadoutRule(IReadOnlySet<string> AllowedFirearms, string PreferredWeapon, DuelPreferredWeaponSlot PreferredSlot)`
  - `internal sealed record DuelCvarSetting(string Name, string Value, string Fallback)`
  - `DuelRuntimePolicy.BuildLoadoutRule(DuelStage stage, string? primary, string? secondary)`
  - `DuelRuntimePolicy.IsFirearm(string? designerName)`
  - `DuelRuntimePolicy.ShouldBlockDrop(string? designerName)`
  - `DuelRuntimePolicy.EngineRoundLimit(int configuredTotalRounds)`
  - `DuelRuntimePolicy.BuildCvarPlan(DuelGameConfig config)`

- [ ] **Step 1: Write failing policy tests**

Create `DuelRuntimePolicyTests.cs` with these exact cases:

```csharp
using CaorenCupPlugin;

namespace CaorenCupPlugin.Tests;

public sealed class DuelRuntimePolicyTests
{
    [Theory]
    [InlineData(DuelStage.Pistol, "", "weapon_usp_silencer", "weapon_usp_silencer", DuelPreferredWeaponSlot.Secondary)]
    [InlineData(DuelStage.Rifle, "weapon_ak47", "weapon_deagle", "weapon_ak47", DuelPreferredWeaponSlot.Primary)]
    [InlineData(DuelStage.Sniper, "weapon_awp", "", "weapon_awp", DuelPreferredWeaponSlot.Primary)]
    public void BuildLoadoutRule_selects_stage_weapon(
        DuelStage stage, string primary, string secondary, string preferred, DuelPreferredWeaponSlot slot)
    {
        var rule = DuelRuntimePolicy.BuildLoadoutRule(stage, primary, secondary);
        Assert.Equal(preferred, rule.PreferredWeapon);
        Assert.Equal(slot, rule.PreferredSlot);
        Assert.Contains(preferred, rule.AllowedFirearms);
    }

    [Theory]
    [InlineData("weapon_ak47", true)]
    [InlineData("weapon_deagle", true)]
    [InlineData("weapon_knife", false)]
    [InlineData("weapon_flashbang", false)]
    [InlineData("item_assaultsuit", false)]
    public void ShouldBlockDrop_blocks_only_firearms(string item, bool expected) =>
        Assert.Equal(expected, DuelRuntimePolicy.ShouldBlockDrop(item));

    [Fact]
    public void Engine_round_limit_reserves_one_round() =>
        Assert.Equal(37, DuelRuntimePolicy.EngineRoundLimit(36));

    [Fact]
    public void Cvar_plan_contains_weapon_guards_and_reserved_round()
    {
        var plan = DuelRuntimePolicy.BuildCvarPlan(new DuelGameConfig(8, 16, 12, 1.25, "random2"));
        Assert.Contains(plan, item => item is { Name: "mp_maxrounds", Value: "37" });
        Assert.Contains(plan, item => item is { Name: "mp_weapons_allow_map_placed", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_death_drop_gun", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_roundtime", Value: "1.25" });
    }
}
```

- [ ] **Step 2: Run the new test and verify RED**

Run:

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter DuelRuntimePolicyTests
```

Expected: FAIL because `DuelRuntimePolicy`, `DuelLoadoutRule`, `DuelCvarSetting` and `DuelPreferredWeaponSlot` do not exist.

- [ ] **Step 3: Implement the pure policy**

Create `DuelRuntimePolicy.cs`. Use an ordinal-ignore-case set for firearms, ignore empty weapon names, classify `weapon_knife*`, `weapon_bayonet`, grenades, C4 and healthshot as non-firearms, and return these CVar settings:

```csharp
internal static class DuelRuntimePolicy
{
    public static int EngineRoundLimit(int configuredTotalRounds)
    {
        if (configuredTotalRounds < 1) throw new ArgumentOutOfRangeException(nameof(configuredTotalRounds));
        return checked(configuredTotalRounds + 1);
    }

    public static DuelLoadoutRule BuildLoadoutRule(DuelStage stage, string? primary, string? secondary)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsFirearm(primary)) allowed.Add(primary!);
        if (IsFirearm(secondary)) allowed.Add(secondary!);
        var preferred = stage == DuelStage.Pistol ? secondary : primary;
        if (!IsFirearm(preferred)) throw new InvalidOperationException("Duel stage has no preferred firearm.");
        return new DuelLoadoutRule(
            allowed,
            preferred!,
            stage == DuelStage.Pistol ? DuelPreferredWeaponSlot.Secondary : DuelPreferredWeaponSlot.Primary);
    }

    public static IReadOnlyList<DuelCvarSetting> BuildCvarPlan(DuelGameConfig config) =>
    [
        new("mp_maxrounds", EngineRoundLimit(config.TotalRounds).ToString(), "24"),
        new("mp_winlimit", "0", "0"),
        new("mp_match_can_clinch", "0", "1"),
        new("mp_roundtime", config.RoundTimeMinutes.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "1.92"),
        new("mp_freezetime", "0", "15"),
        new("mp_round_restart_delay", "2", "7"),
        new("mp_free_armor", "0", "0"),
        new("mp_halftime", "0", "1"),
        new("mp_autoteambalance", "0", "1"),
        new("mp_limitteams", "0", "2"),
        new("mp_weapons_allow_map_placed", "0", "1"),
        new("mp_death_drop_gun", "0", "1")
    ];
}
```

Define the two records and enum above the class. Keep the non-firearm name set private and explicit.

- [ ] **Step 4: Run policy tests and full bridge tests**

Run:

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter DuelRuntimePolicyTests
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
```

Expected: the focused tests PASS; the full suite reports at least 122 existing tests plus the new policy cases, with 0 failures.

- [ ] **Step 5: Commit the policy**

```powershell
git add web-command-center/CaorenCupPlugin/DuelRuntimePolicy.cs web-command-center/CaorenCupPlugin.Tests/DuelRuntimePolicyTests.cs
git commit -m "feat: add shared duel runtime policy"
```

---

### Task 2: Make the plugin own duel runtime CVar activation

**Files:**
- Modify: `web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs`
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs:613-718`
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs:2517-2575`
- Modify: `web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs`

**Interfaces:**
- Consumes: `DuelRuntimePolicy.BuildCvarPlan(DuelGameConfig)` from Task 1.
- Produces:
  - `DuelServerCvarScope.Apply(IEnumerable<DuelCvarSetting> settings)`
  - `CaorenCupPlugin.ActivateDuelRuntime(DuelGameConfig config)`
  - `ConfigureDuelMode` accepts payload property `roundTimeMinutes`.

- [ ] **Step 1: Back up existing files**

```powershell
Copy-Item web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs.bak-20260727-duel-runtime-polish
Copy-Item web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260727-duel-runtime-polish
Copy-Item web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs.bak-20260727-duel-runtime-polish
```

- [ ] **Step 2: Add failing CVar activation tests**

Add tests to `FinalReviewFixTests.cs` that construct `DuelServerCvarScope` with test delegates, call `Apply(DuelRuntimePolicy.BuildCvarPlan(config))`, and assert exact command presence and original-value restoration:

```csharp
[Fact]
public void Duel_runtime_cvars_are_applied_and_restored_as_one_scope()
{
    var executed = new List<string>();
    var scope = CreateCvarScope((name, fallback) => name switch
    {
        "mp_weapons_allow_map_placed" => "1",
        "mp_death_drop_gun" => "1",
        "mp_maxrounds" => "24",
        _ => fallback
    }, executed.Add);

    scope.Apply(DuelRuntimePolicy.BuildCvarPlan(new DuelGameConfig()));
    Assert.Contains("mp_maxrounds 37", executed);
    Assert.Contains("mp_weapons_allow_map_placed 0", executed);
    Assert.Contains("mp_death_drop_gun 0", executed);

    scope.RestoreAll();
    Assert.Contains("mp_maxrounds 24", executed);
    Assert.Contains("mp_weapons_allow_map_placed 1", executed);
    Assert.Contains("mp_death_drop_gun 1", executed);
}
```

Add this helper once at the bottom of `FinalReviewFixTests` so the new test uses the same constructor contract as the existing restore tests:

```csharp
private static DuelServerCvarScope CreateCvarScope(
    Func<string, string, string> read,
    Action<string> execute)
{
    var constructor = typeof(DuelServerCvarScope).GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        [typeof(Func<string, string, string>), typeof(Action<string>)],
        modifiers: null);
    Assert.NotNull(constructor);
    return Assert.IsType<DuelServerCvarScope>(constructor!.Invoke([read, execute]));
}
```

Refactor the existing CVar-scope tests to call this helper only when doing so keeps their failure setup and assertions unchanged.

- [ ] **Step 3: Run the focused test and verify RED**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter Duel_runtime_cvars_are_applied_and_restored_as_one_scope
```

Expected: FAIL because `DuelServerCvarScope.Apply` does not exist.

- [ ] **Step 4: Implement batch application and shared activation**

Add to `DuelServerCvarScope`:

```csharp
public void Apply(IEnumerable<DuelCvarSetting> settings)
{
    ArgumentNullException.ThrowIfNull(settings);
    foreach (var setting in settings)
    {
        Set(setting.Name, setting.Value, setting.Fallback);
    }
}
```

Add one shared helper to `CaorenCupPlugin` and replace the duplicated GameManaged assignments/CVar calls:

```csharp
private void ActivateDuelRuntime(DuelGameConfig config)
{
    if (!_duelServerCvars.IsReadyForNewDuel)
        throw new InvalidOperationException("Previous duel CVar restore is incomplete.");

    _duelPistolRounds = config.PistolRounds;
    _duelRifleRounds = config.RifleRounds;
    _duelSniperRounds = config.SniperRounds;
    _duelUtilityMode = config.UtilityMode;
    _duelFormalRound = 0;
    _duelLastAnnouncedStage = null;
    ClearDuelEquipmentState();
    _duelServerCvars.Apply(DuelRuntimePolicy.BuildCvarPlan(config));
    _duelModeEnabled = true;
    Server.ExecuteCommand("mp_warmup_end");
    Server.ExecuteCommand("mp_restartgame 1");
}
```

In `ConfigureDuelMode`, parse `roundTimeMinutes` with the existing invariant numeric helpers, construct `DuelGameConfig`, call `EnterWebManaged(config)`, then call `ActivateDuelRuntime(config)` inside `try/catch`. On failure, clear the session, restore captured CVar values, leave `_duelModeEnabled = false`, and log one error.

- [ ] **Step 5: Run tests and build**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj -c Release
```

Expected: all tests PASS; Release build has 0 warnings and 0 errors.

- [ ] **Step 6: Commit CVar ownership**

```powershell
git add web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs
git commit -m "refactor: centralize duel runtime cvars"
```

---

### Task 3: Enforce legal firearms and preferred round weapon

**Files:**
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs:188-220`
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs:2840-2920`
- Modify: `web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs`

**Interfaces:**
- Consumes: `DuelLoadoutRule`, `DuelRuntimePolicy.ShouldBlockDrop`, and `BuildLoadoutRule` from Task 1.
- Produces:
  - `OnDuelDropCommand(CCSPlayerController? player, CommandInfo command)`
  - `RemoveUnexpectedDuelFirearms(CCSPlayerController player, DuelLoadoutRule rule)`
  - `QueuePreferredDuelWeapon(CCSPlayerController player, DuelLoadoutRule rule)`
  - `TrySelectPreferredDuelWeapon(CCSPlayerController player, DuelLoadoutRule rule, bool allowRetry)`

- [ ] **Step 1: Add failing source and behavior boundary tests**

Add tests to `FinalReviewFixTests.cs` that verify:

```csharp
[Fact]
public void Duel_drop_listener_and_single_retry_are_wired()
{
    var source = ReadPluginSource();
    Assert.Contains("AddCommandListener(\"drop\", OnDuelDropCommand, HookMode.Pre)", source);
    Assert.Contains("RemoveUnexpectedDuelFirearms(player, rule)", source);
    Assert.Contains("QueuePreferredDuelWeapon(player, rule)", source);
    Assert.Contains("allowRetry: false", source);
}
```

Add the source locator explicitly; it walks upward from the test output directory and fails with a useful message if the project tree cannot be found:

```csharp
private static string ReadPluginSource()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        var candidate = Path.Combine(
            directory.FullName,
            "CaorenCupPlugin",
            "CaorenCupPlugin.cs");
        if (File.Exists(candidate)) return File.ReadAllText(candidate);
    }

    throw new FileNotFoundException("Could not locate CaorenCupPlugin.cs from the test output directory.");
}
```

Also add a pure assertion that an allowed pistol/rifle remains in `AllowedFirearms` while an unrelated map gun does not.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter "Duel_drop_listener_and_single_retry_are_wired|BuildLoadoutRule"
```

Expected: FAIL because the listener and helpers are not wired.

- [ ] **Step 3: Register and implement firearm drop blocking**

Register the listener beside `jointeam`:

```csharp
AddCommandListener("drop", OnDuelDropCommand, HookMode.Pre);
```

Implement:

```csharp
private HookResult OnDuelDropCommand(CCSPlayerController? player, CommandInfo command)
{
    if (!_duelModeEnabled || !IsRealPlayer(player)) return HookResult.Continue;
    if (!DuelRuntimePolicy.ShouldBlockDrop(PlayerEquipment(player).ActiveWeapon)) return HookResult.Continue;
    player!.PrintToChat($" {ChatColors.Green}[草人杯]{ChatColors.Default} 单挑期间不能丢弃枪械。");
    return HookResult.Handled;
}
```

Do not block grenade drops.

- [ ] **Step 4: Validate inventory after loadout**

After resolving `primary` and `secondary`, build one rule and use it for validation and selection:

```csharp
var rule = DuelRuntimePolicy.BuildLoadoutRule(stage, primary, secondary);
// Existing RemoveWeapons/GiveNamedItem/armor/utility sequence.
RemoveUnexpectedDuelFirearms(player, rule);
QueuePreferredDuelWeapon(player, rule);
```

`RemoveUnexpectedDuelFirearms` must iterate a snapshot of `WeaponServices.MyWeapons`, check `weapon.IsValid`, and call `weapon.Remove()` only when `DuelRuntimePolicy.IsFirearm(weapon.DesignerName)` is true and the name is not in `rule.AllowedFirearms`. Never remove knife, grenade, C4, armor or allowed firearms.

- [ ] **Step 5: Select the stage weapon with one retry**

Use `slot1` for `Primary` and `slot2` for `Secondary`. Both callbacks must re-check `_isUnloading`, `_duelModeEnabled`, player validity and SteamID before acting:

```csharp
private void QueuePreferredDuelWeapon(CCSPlayerController player, DuelLoadoutRule rule)
{
    var steamId = player.SteamID;
    Server.NextFrame(() =>
    {
        if (!CanApplyDuelLoadoutContinuation(player, steamId)) return;
        TrySelectPreferredDuelWeapon(player, rule, allowRetry: true);
    });
}
```

Define the continuation guard instead of duplicating callback checks:

```csharp
private bool CanApplyDuelLoadoutContinuation(CCSPlayerController player, ulong steamId)
{
    return !_isUnloading
        && _duelModeEnabled
        && IsRealPlayer(player)
        && player.SteamID == steamId;
}
```

`TrySelectPreferredDuelWeapon` executes the correct slot command. When `PlayerEquipment(player).ActiveWeapon` is not `rule.PreferredWeapon` and `allowRetry` is true, schedule exactly one more `Server.NextFrame` call with `allowRetry: false`. The second failure is logged at debug level and does not reapply the loadout.

- [ ] **Step 6: Run tests and build**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj -c Release
```

Expected: all tests PASS; build has 0 warnings and 0 errors.

- [ ] **Step 7: Commit weapon enforcement**

```powershell
git add web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs
git commit -m "fix: enforce duel weapon rules"
```

---

### Task 4: Implement plugin-controlled completion and two-phase cleanup

**Files:**
- Create: `web-command-center/CaorenCupPlugin/DuelCleanupState.cs`
- Create: `web-command-center/CaorenCupPlugin.Tests/DuelCleanupStateTests.cs`
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs:1089-1112`
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs:1323-1537`
- Modify: `web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs`

**Interfaces:**
- Consumes: existing `DuelControlMode`, outbound FIFO queue, `DuelTelemetryIsolationState`, and CVar retry methods.
- Produces:
  - `DuelCleanupState.Begin(DuelControlMode mode)`
  - `DuelCleanupState.TryConsumeRestartRound(out DuelControlMode mode)`
  - `DuelCleanupState.Reset()`
  - `BeginDuelCleanup(DuelControlMode mode)`
  - `CompleteDuelCleanupAfterRestart()`

- [ ] **Step 1: Write failing cleanup-state tests**

Create `DuelCleanupStateTests.cs`:

```csharp
using CaorenCupPlugin;

namespace CaorenCupPlugin.Tests;

public sealed class DuelCleanupStateTests
{
    [Fact]
    public void Restart_round_is_consumed_once()
    {
        var state = new DuelCleanupState();
        state.Begin(DuelControlMode.WebManaged);
        Assert.True(state.TryConsumeRestartRound(out var mode));
        Assert.Equal(DuelControlMode.WebManaged, mode);
        Assert.False(state.TryConsumeRestartRound(out _));
    }

    [Fact]
    public void Duplicate_begin_is_idempotent_for_same_mode()
    {
        var state = new DuelCleanupState();
        state.Begin(DuelControlMode.GameManaged);
        state.Begin(DuelControlMode.GameManaged);
        Assert.True(state.RestartPending);
    }
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter DuelCleanupStateTests
```

Expected: FAIL because `DuelCleanupState` does not exist.

- [ ] **Step 3: Implement the cleanup state**

Create a small sealed class that rejects `None`, permits duplicate `Begin` for the same mode, throws on a conflicting mode, exposes `RestartPending`, consumes once, and resets to `None`.

```csharp
internal sealed class DuelCleanupState
{
    public bool RestartPending { get; private set; }
    public DuelControlMode Mode { get; private set; }

    public void Begin(DuelControlMode mode)
    {
        if (mode == DuelControlMode.None) throw new ArgumentOutOfRangeException(nameof(mode));
        if (RestartPending && Mode != mode) throw new InvalidOperationException("Conflicting duel cleanup mode.");
        Mode = mode;
        RestartPending = true;
    }

    public bool TryConsumeRestartRound(out DuelControlMode mode)
    {
        mode = Mode;
        if (!RestartPending) return false;
        RestartPending = false;
        return true;
    }

    public void Reset() { RestartPending = false; Mode = DuelControlMode.None; }
}
```

- [ ] **Step 4: Add failing plugin ordering tests**

Add source/integration boundary tests asserting:

- WebManaged final `QueueEvent("round_end", ...)` and `QueueSnapshot()` occur before `BeginDuelCleanup(DuelControlMode.WebManaged)`.
- GameManaged final path still returns before web telemetry queuing.
- `BeginDuelCleanup` executes `mp_restartgame 1` but does not call `RestoreGameManagedDuelCvarsWithRetry`.
- `CompleteDuelCleanupAfterRestart` performs restoration and is called from `OnRoundStart` before telemetry isolation release.
- `mp_maxrounds` is not restored in the high-score `round_end` callback.

- [ ] **Step 5: Refactor final-round handling**

In `OnRoundEnd`, preserve `wasGameManaged` and add `wasWebManaged`. For WebManaged, detect `scoreCT + scoreT >= Config.TotalRounds`, queue the final event and snapshot exactly once, then call `BeginDuelCleanup(WebManaged)`. For GameManaged, broadcast the result and call `BeginDuelCleanup(GameManaged)` without queuing web telemetry.

Use a single `BeginDuelCleanup` implementation:

```csharp
private void BeginDuelCleanup(DuelControlMode mode)
{
    if (_duelCleanupState.RestartPending) return;
    _duelCleanupState.Begin(mode);
    _gameManagedDuelRuntimeActive = false;
    _duelModeEnabled = false;
    _teamLockEnabled = false;
    _teamAssignments.Clear();
    _teamAssignmentBypass.Clear();
    _duelCvarRestorePending = _duelServerCvars.PendingRestoreCount > 0;
    if (mode == DuelControlMode.GameManaged)
    {
        _heartbeatResponseOrder.BeginBarrier();
        _duelTelemetryIsolation.BeginCleanupRestart();
        _duelTelemetryIsolation.UpdateCvarRestoreReady(false);
    }
    Server.ExecuteCommand("mp_restartgame 1");
}
```

Do not clear the session or restore CVar values here.

- [ ] **Step 6: Complete cleanup on the restart round**

At the start of `OnRoundStart`, detect and consume the cleanup round before marking a new duel round or applying loadouts:

```csharp
if (_duelCleanupState.TryConsumeRestartRound(out var cleanupMode))
{
    CompleteDuelCleanupAfterRestart(cleanupMode);
}
```

`CompleteDuelCleanupAfterRestart` clears equipment/AWP/protection state, resets local match stats, clears the duel session, restores all captured CVar values with the existing retry path, then calls `_duelTelemetryIsolation.CompleteCleanupRoundStart()` and commits released heartbeat state for GameManaged. If restoration remains pending, isolation stays active and the safety timer continues retrying.

On plugin unload or map-change cleanup, use an explicit immediate-restore path because no cleanup `round_start` is guaranteed. That path must be idempotent and must not start another restart while unloading.

- [ ] **Step 7: Run cleanup tests and full regression**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter "DuelCleanupStateTests|FinalReviewFixTests|DuelGameSessionTests"
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj -c Release
```

Expected: all tests PASS; build has 0 warnings and 0 errors.

- [ ] **Step 8: Commit two-phase cleanup**

```powershell
git add web-command-center/CaorenCupPlugin/DuelCleanupState.cs web-command-center/CaorenCupPlugin.Tests/DuelCleanupStateTests.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin.Tests/FinalReviewFixTests.cs
git commit -m "fix: avoid duel end map vote"
```

---

### Task 5: Replace web-side CVar commands with one runtime config command

**Files:**
- Create: `web-command-center/src/duel-runtime-config.ts`
- Create: `web-command-center/src/duel-runtime-config.test.ts`
- Modify: `web-command-center/src/game-flow-manager.ts:694-763`
- Modify: `web-command-center/package.json`

**Interfaces:**
- Consumes: `normalizeDuelRounds`, `normalizeDuelRoundTimeMinutes`, `normalizeDuelUtilityMode`, session `matchOptions`, and `matchId`.
- Produces:
  - `buildDuelRuntimeConfigPayload(matchId, matchOptions, requestedAt)`
  - payload fields `matchId`, `rounds`, `roundTimeMinutes`, `utilityMode`, `requestedAt`.

- [ ] **Step 1: Back up existing web files**

```powershell
Copy-Item web-command-center/src/game-flow-manager.ts web-command-center/src/game-flow-manager.ts.bak-20260727-duel-runtime-polish
Copy-Item web-command-center/package.json web-command-center/package.json.bak-20260727-duel-runtime-polish
```

- [ ] **Step 2: Write the failing payload test**

Create `duel-runtime-config.test.ts`:

```typescript
import assert from 'node:assert/strict';
import test from 'node:test';
import { buildDuelRuntimeConfigPayload } from './duel-runtime-config';

test('builds the only formal-start payload owned by the plugin', () => {
  const payload = buildDuelRuntimeConfigPayload('match-1', {
    duelRounds: { pistol: 8, rifle: 16, sniper: 12 },
    duelRoundTimeMinutes: 1.25,
    duelUtilityMode: 'random2',
  }, 1234);
  assert.deepEqual(payload, {
    matchId: 'match-1',
    rounds: { pistol: 8, rifle: 16, sniper: 12 },
    roundTimeMinutes: 1.25,
    utilityMode: 'random2',
    requestedAt: 1234,
  });
  assert.equal('command' in payload, false);
});
```

- [ ] **Step 3: Run the test and verify RED**

```powershell
cd web-command-center
npx tsx --test src/duel-runtime-config.test.ts
```

Expected: FAIL because `duel-runtime-config.ts` does not exist.

- [ ] **Step 4: Implement the payload builder**

Create `duel-runtime-config.ts` using the existing normalization functions. Reject an empty `matchId`, use the provided `requestedAt` unchanged, and return only the five fields asserted above.

- [ ] **Step 5: Remove duplicate web CVar ownership**

Delete `queueDuelRulesCommands`. Change `queueDuelFormalStart` to exactly one runtime configuration enqueue:

```typescript
const queueDuelFormalStart = () => {
    const session = getSession();
    enqueuePluginCommand(
        'CONFIGURE_DUEL_MODE',
        buildDuelRuntimeConfigPayload(session.matchId, session.matchOptions, Date.now()),
    );
};
```

Keep the separate `RESET_LIVE_MATCH_STATS` command and map/warmup preparation flow unchanged. Add `test:duel-runtime` to `package.json`:

```json
"test:duel-runtime": "tsx --test src/duel-runtime-config.test.ts"
```

- [ ] **Step 6: Run web tests and typecheck**

```powershell
cd web-command-center
npm run test:duel-runtime
npm run typecheck
npm run test:match-command-policy
```

Expected: all commands exit 0; TypeScript reports no errors.

- [ ] **Step 7: Commit web command consolidation**

```powershell
git add web-command-center/src/duel-runtime-config.ts web-command-center/src/duel-runtime-config.test.ts web-command-center/src/game-flow-manager.ts web-command-center/package.json
git commit -m "refactor: send duel runtime config once"
```

---

### Task 6: Update local gate and real-server acceptance checklist

**Files:**
- Modify: `scripts/test-duel-mode-local.ps1`
- Modify: `docs/duel-mode-test-flow.md`

**Interfaces:**
- Consumes: `npm run test:duel-runtime` from Task 5 and the finished plugin behavior.
- Produces: one local gate covering web configuration, bridge tests and Release build; one explicit真人 checklist.

- [ ] **Step 1: Back up the gate and checklist**

```powershell
Copy-Item scripts/test-duel-mode-local.ps1 scripts/test-duel-mode-local.ps1.bak-20260727-duel-runtime-polish
Copy-Item docs/duel-mode-test-flow.md docs/duel-mode-test-flow.md.bak-20260727-duel-runtime-polish
```

- [ ] **Step 2: Extend the local gate**

After TypeScript typecheck and before bridge tests, add:

```powershell
Invoke-Step 'Web duel runtime config tests' $webRoot {
    npm run test:duel-runtime
}
```

Change the bridge build step to `dotnet build -c Release` so the deployment configuration is always verified.

- [ ] **Step 3: Add exact真人 acceptance items**

Add a “武器与结束流程回归” subsection containing these checkboxes:

```markdown
- [ ] 地图放置枪械不能进入参赛者库存。
- [ ] 主武器或副武器按 `drop` 不会落地；投掷物仍可正常使用和切换。
- [ ] 参赛者死亡后没有枪械掉落供对方拾取。
- [ ] 手枪阶段连续 5 回合默认拿出手枪，不偶发持刀。
- [ ] 步枪阶段连续 5 回合默认拿出主武器，不偶发持刀。
- [ ] 狙击阶段连续 5 回合默认拿出狙击枪，不偶发持刀。
- [ ] 网页单挑最终回合保留比分和赛后统计，且不出现 CS2 官方地图投票。
- [ ] 游戏内独立单挑最终回合不出现在网页，且不出现 CS2 官方地图投票。
- [ ] 结束后用服务器控制台确认 `mp_maxrounds`、`mp_weapons_allow_map_placed`、`mp_death_drop_gun` 已恢复。
```

- [ ] **Step 4: Run the complete local gate**

```powershell
.\scripts\test-duel-mode-local.ps1
```

Expected:

- web typecheck PASS;
- duel runtime config test PASS;
- lobby JavaScript syntax PASS;
- bridge tests report 0 failures;
- Release build reports 0 warnings and 0 errors.

- [ ] **Step 5: Audit UTF-8, ignored files and diff**

```powershell
git status --short --ignored
git diff --check
git diff --stat
rg -n -e "T[B]D" -e "T[O]DO" -e "implement l[a]ter" -e "fill in d[e]tails" docs/superpowers/plans/2026-07-27-duel-runtime-polish.md
```

Expected: backups/build outputs/dependencies are ignored; no forbidden file is staged; `git diff --check` has no output; placeholder search has no matches.

- [ ] **Step 6: Commit gate and documentation**

```powershell
git add scripts/test-duel-mode-local.ps1 docs/duel-mode-test-flow.md
git commit -m "test: cover duel runtime regressions"
```

---

### Task 7: Whole-branch review and handoff

**Files:**
- Review: all files changed since `235bdbf`.
- Do not modify unrelated files.

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces: a locally verified branch ready for review; no push, deployment, tag or Release.

- [ ] **Step 1: Review the complete branch diff**

```powershell
git diff --stat 235bdbf..HEAD
git diff 235bdbf..HEAD -- web-command-center/CaorenCupPlugin web-command-center/CaorenCupPlugin.Tests web-command-center/src web-command-center/package.json scripts/test-duel-mode-local.ps1 docs/duel-mode-test-flow.md
```

Check each approved requirement against an actual code path and test. Confirm GameManaged never reaches `QueueEvent("round_end", ...)`, while WebManaged final telemetry precedes restart.

- [ ] **Step 2: Run final verification from a clean state**

```powershell
.\scripts\test-duel-mode-local.ps1
git status --short --branch
git status --short --ignored
git log --oneline --decorate -8
```

Expected: gate passes; only ignored backup/build/dependency files remain; branch contains the design commit plus focused implementation commits.

- [ ] **Step 3: Verify deployment boundaries without deploying**

```powershell
git diff --name-only 235bdbf..HEAD
```

Expected: runtime files are limited to `web-command-center/` plus tests/scripts/docs. `game-plugin/`, `desktop-client/`, private bot controller, ZIP files and production configuration must not appear.

- [ ] **Step 4: Use completion verification and branch-finishing workflow**

Invoke `superpowers:verification-before-completion`, then `superpowers:finishing-a-development-branch`. Present local merge, push/PR, keep, or discard choices. Pushing, Release creation and server deployment remain separate user approvals.
