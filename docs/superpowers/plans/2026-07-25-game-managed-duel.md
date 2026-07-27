# 游戏内独立单挑 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 `CaorenCupPlugin` 中增加不依赖网页的管理员游戏内单挑流程，同时保留并隔离现有网页单挑。

**Architecture:** 新增纯 C# `DuelGameSession` 作为可测试的状态机，新增纯命令解析器，并由现有 `CaorenCupPlugin` 负责 CounterStrikeSharp 事件、玩家、队伍与服务器命令适配。控制模式明确区分 `None`、`WebManaged`、`GameManaged`；游戏内模式屏蔽比赛遥测上报，但保留桥接心跳。

**Tech Stack:** C#、.NET 8、CounterStrikeSharp API 1.0.367、xUnit 2.9.2、PowerShell。

## Global Constraints

- 所有新增和修改文件使用 UTF-8。
- 修改既有文件前创建 `.bak-YYYYMMDD-任务简述` 备份；备份、`bin/`、`obj/`、ZIP、日志和密钥配置不得提交。
- 不修改或提交 `bot-improver-controller/`、其脚本、CI 改动及构建产物。
- 本功能只修改 `web-command-center/CaorenCupPlugin/`、对应测试和项目文档；不修改网页业务、娱乐插件本体或桌面客户端。
- 游戏内独立单挑不得读取、更新或展示在网页比赛状态中；普通插件心跳可以继续。
- 地图、回合数、回合时间和道具模式只允许在开赛前修改。
- T/CT 当前全部真人参赛；观察者和后来加入者不参赛；参赛身份与队伍以 SteamID 为准。
- 总回合至少 30，默认回合为 8/16/12，默认回合时间 1 分钟，默认无道具。
- 服务器上传、覆盖、插件重载或进程重启必须另行取得用户同意；本计划只实施本地代码和验证。
- 每次提交前运行 `git status --short --ignored` 与 `git diff --cached --stat`，只暂存本任务文件。

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `web-command-center/CaorenCupPlugin/DuelGameSession.cs` | 独立单挑配置、控制模式、参赛者、阶段、比分、暂停和结束状态机。 |
| `web-command-center/CaorenCupPlugin/DuelAdminCommandParser.cs` | `/duel` 子命令解析，不依赖 CounterStrikeSharp。 |
| `web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs` | 首次改写前捕获 cvar，并在结束时恢复。 |
| `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs` | 注册指令、适配玩家与事件、锁队、切图、暂停、遥测隔离和生命周期清理。 |
| `web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs` | 状态机边界测试。 |
| `web-command-center/CaorenCupPlugin.Tests/DuelAdminCommandParserTests.cs` | 管理员子命令语法测试。 |
| `docs/duel-mode-test-flow.md` | 增加游戏内独立单挑本地与真人验收清单。 |

## Task 1: 建立独立单挑领域状态机

**Files:**
- Create: `web-command-center/CaorenCupPlugin/DuelGameSession.cs`
- Create: `web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs`

**Interfaces:**
- Produces: `DuelControlMode`, `DuelLifecycle`, `DuelTeam`, `DuelGameStage`, `DuelGameConfig`, `DuelParticipant`, `DuelRoundResult`, `DuelGameSession`。
- `TryStart(IReadOnlyCollection<DuelParticipant> players, bool confirmWebTakeover, out string error)` 建立不可变参赛名单。
- `MarkRoundStarted()` 打开一个可计分正式回合。
- `RecordRoundEnd(DuelTeam winner)` 只结算已打开的正式回合并返回是否完赛。
- `UpdateConnectedPlayers(IReadOnlySet<string> connectedSteamIds)` 在参赛者掉线时自动暂停。
- `TryResume(out string error)` 在双方至少各有一名在线参赛者时恢复。

- [ ] **Step 1: 写状态机失败测试。**

创建 `DuelGameSessionTests.cs`，至少包含以下完整测试用例：

```csharp
using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class DuelGameSessionTests
{
    private static DuelParticipant T(string id = "t1") => new(id, "T玩家", DuelTeam.Terrorist);
    private static DuelParticipant Ct(string id = "ct1") => new(id, "CT玩家", DuelTeam.CounterTerrorist);

    [Fact]
    public void Start_requires_both_teams()
    {
        var session = new DuelGameSession();
        Assert.False(session.TryStart([T()], false, out var error));
        Assert.Equal("T 和 CT 双方都必须至少有一名真人玩家。", error);
    }

    [Fact]
    public void Web_mode_requires_explicit_takeover()
    {
        var session = new DuelGameSession();
        session.EnterWebManaged(new DuelGameConfig(8, 16, 12, 1, "none"));
        Assert.False(session.TryStart([T(), Ct()], false, out _));
        Assert.True(session.TryStart([T(), Ct()], true, out _));
        Assert.Equal(DuelControlMode.GameManaged, session.ControlMode);
    }

    [Fact]
    public void Round_boundaries_skip_zero_length_stage_and_finish_at_total()
    {
        var session = new DuelGameSession(new DuelGameConfig(0, 30, 0, 1, "none"));
        Assert.True(session.TryStart([T(), Ct()], false, out _));
        Assert.Equal(DuelGameStage.Rifle, session.CurrentStage);
        for (var i = 0; i < 29; i++) {
            session.MarkRoundStarted();
            Assert.False(session.RecordRoundEnd(DuelTeam.Terrorist).Finished);
        }
        session.MarkRoundStarted();
        var result = session.RecordRoundEnd(DuelTeam.CounterTerrorist);
        Assert.True(result.Finished);
        Assert.Equal(29, result.ScoreT);
        Assert.Equal(1, result.ScoreCt);
    }

    [Fact]
    public void Disconnect_pauses_and_reconnect_does_not_auto_resume()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);
        Assert.True(session.UpdateConnectedPlayers(new HashSet<string> { "ct1" }));
        Assert.Equal(DuelLifecycle.Paused, session.Lifecycle);
        session.UpdateConnectedPlayers(new HashSet<string> { "t1", "ct1" });
        Assert.Equal(DuelLifecycle.Paused, session.Lifecycle);
        Assert.True(session.TryResume(out _));
        Assert.Equal(DuelLifecycle.Running, session.Lifecycle);
    }

    [Fact]
    public void Resume_only_requires_one_online_participant_per_side()
    {
        var session = new DuelGameSession();
        session.TryStart([T("t1"), T("t2"), Ct("ct1"), Ct("ct2")], false, out _);
        session.UpdateConnectedPlayers(new HashSet<string> { "t1", "ct1" });
        Assert.True(session.TryResume(out _));
    }

    [Fact]
    public void Duplicate_round_end_does_not_double_score()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);
        session.MarkRoundStarted();
        session.RecordRoundEnd(DuelTeam.Terrorist);
        session.RecordRoundEnd(DuelTeam.Terrorist);
        Assert.Equal(1, session.ScoreT);
    }
}
```

- [ ] **Step 2: 运行测试并确认按预期失败。**

Run:

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter DuelGameSessionTests
```

Expected: FAIL，编译器指出 `DuelGameSession`、`DuelGameConfig` 等类型不存在。

- [ ] **Step 3: 实现最小状态模型。**

`DuelGameSession.cs` 使用下列公开模型和核心规则；实现时保持这些签名不变：

```csharp
namespace CaorenCupPlugin;

public enum DuelControlMode { None, WebManaged, GameManaged }
public enum DuelLifecycle { Idle, Running, Paused, Finished }
public enum DuelTeam { Terrorist, CounterTerrorist }
public enum DuelGameStage { Pistol, Rifle, Sniper }
public sealed record DuelGameConfig(int PistolRounds = 8, int RifleRounds = 16, int SniperRounds = 12, double RoundTimeMinutes = 1, string UtilityMode = "none")
{
    public int TotalRounds => PistolRounds + RifleRounds + SniperRounds;
}
public sealed record DuelParticipant(string SteamId, string Name, DuelTeam Team);
public sealed record DuelRoundResult(bool Counted, bool Finished, int ScoreT, int ScoreCt);

public sealed class DuelGameSession
{
    private readonly Dictionary<string, DuelParticipant> _participants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _connected = new(StringComparer.Ordinal);
    private bool _roundOpen;

    public DuelGameSession(DuelGameConfig? config = null) => Config = config ?? new();
    public DuelControlMode ControlMode { get; private set; }
    public DuelLifecycle Lifecycle { get; private set; }
    public DuelGameConfig Config { get; private set; }
    public IReadOnlyDictionary<string, DuelParticipant> Participants => _participants;
    public int CompletedRounds { get; private set; }
    public int ScoreT { get; private set; }
    public int ScoreCt { get; private set; }
    public string? PauseReason { get; private set; }
    public DuelGameStage CurrentStage => CompletedRounds < Config.PistolRounds
        ? DuelGameStage.Pistol
        : CompletedRounds < Config.PistolRounds + Config.RifleRounds ? DuelGameStage.Rifle : DuelGameStage.Sniper;

    public void EnterWebManaged(DuelGameConfig config) { Clear(); Config = config; ControlMode = DuelControlMode.WebManaged; }
    public bool TryStart(IReadOnlyCollection<DuelParticipant> players, bool confirmWebTakeover, out string error)
    {
        if (ControlMode == DuelControlMode.GameManaged && Lifecycle is DuelLifecycle.Running or DuelLifecycle.Paused)
        {
            error = "游戏内单挑已经在进行。";
            return false;
        }
        if (ControlMode == DuelControlMode.WebManaged && !confirmWebTakeover)
        {
            error = "检测到网页单挑状态，请输入 /duel start confirm 确认接管。";
            return false;
        }
        var unique = players.Where(p => !string.IsNullOrWhiteSpace(p.SteamId))
            .GroupBy(p => p.SteamId, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        if (!unique.Any(p => p.Team == DuelTeam.Terrorist) || !unique.Any(p => p.Team == DuelTeam.CounterTerrorist))
        {
            error = "T 和 CT 双方都必须至少有一名真人玩家。";
            return false;
        }
        Clear();
        foreach (var participant in unique) { _participants[participant.SteamId] = participant; _connected.Add(participant.SteamId); }
        ControlMode = DuelControlMode.GameManaged;
        Lifecycle = DuelLifecycle.Running;
        error = string.Empty;
        return true;
    }
    public void MarkRoundStarted() { if (ControlMode == DuelControlMode.GameManaged && Lifecycle == DuelLifecycle.Running) _roundOpen = true; }
    public DuelRoundResult RecordRoundEnd(DuelTeam winner)
    {
        if (ControlMode != DuelControlMode.GameManaged || Lifecycle != DuelLifecycle.Running || !_roundOpen)
            return new(false, false, ScoreT, ScoreCt);
        _roundOpen = false;
        CompletedRounds++;
        if (winner == DuelTeam.Terrorist) ScoreT++; else ScoreCt++;
        var finished = CompletedRounds >= Config.TotalRounds;
        if (finished) Lifecycle = DuelLifecycle.Finished;
        return new(true, finished, ScoreT, ScoreCt);
    }
    public bool UpdateConnectedPlayers(IReadOnlySet<string> connectedSteamIds)
    {
        _connected.Clear();
        foreach (var steamId in connectedSteamIds)
            if (_participants.ContainsKey(steamId)) _connected.Add(steamId);
        if (Lifecycle != DuelLifecycle.Running || _participants.Keys.All(_connected.Contains)) return false;
        Pause("有参赛玩家掉线");
        return true;
    }
    public bool TryResume(out string error)
    {
        if (ControlMode != DuelControlMode.GameManaged || Lifecycle != DuelLifecycle.Paused)
        {
            error = "当前单挑没有暂停。";
            return false;
        }
        var hasT = _participants.Values.Any(p => p.Team == DuelTeam.Terrorist && _connected.Contains(p.SteamId));
        var hasCt = _participants.Values.Any(p => p.Team == DuelTeam.CounterTerrorist && _connected.Contains(p.SteamId));
        if (!hasT || !hasCt)
        {
            error = "T 和 CT 双方都必须至少有一名在线参赛者才能恢复。";
            return false;
        }
        Lifecycle = DuelLifecycle.Running;
        PauseReason = null;
        error = string.Empty;
        return true;
    }
    public void Pause(string reason) { if (Lifecycle == DuelLifecycle.Running) { Lifecycle = DuelLifecycle.Paused; PauseReason = reason; _roundOpen = false; } }
    public void Clear()
    {
        _participants.Clear(); _connected.Clear(); CompletedRounds = 0; ScoreT = 0; ScoreCt = 0;
        _roundOpen = false; PauseReason = null; ControlMode = DuelControlMode.None; Lifecycle = DuelLifecycle.Idle;
    }
}
```

- [ ] **Step 4: 运行状态机测试。**

Run:

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter DuelGameSessionTests
```

Expected: PASS，6 个状态机测试全部通过。

- [ ] **Step 5: 提交领域状态机。**

```powershell
git add web-command-center/CaorenCupPlugin/DuelGameSession.cs web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs
git diff --cached --stat
git commit -m "feat: add game-managed duel session"
```

## Task 2: 实现 `/duel` 子命令解析与参数验证

**Files:**
- Create: `web-command-center/CaorenCupPlugin/DuelAdminCommandParser.cs`
- Create: `web-command-center/CaorenCupPlugin.Tests/DuelAdminCommandParserTests.cs`
- Modify: `web-command-center/CaorenCupPlugin/DuelGameSession.cs`
- Modify: `web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs`

**Interfaces:**
- Produces `DuelAdminCommandKind` 与 `DuelAdminCommand`。
- `DuelAdminCommandParser.Parse(IReadOnlyList<string> args)` 始终返回成功命令或带中文 `Error` 的 `Invalid`。
- `DuelGameSession.TryUpdateConfig(DuelGameConfig next, out string error)` 统一校验总回合、单项范围、时间和道具模式。

- [ ] **Step 1: 写解析和配置失败测试。**

```csharp
using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class DuelAdminCommandParserTests
{
    [Theory]
    [InlineData("help", DuelAdminCommandKind.Help)]
    [InlineData("status", DuelAdminCommandKind.Status)]
    [InlineData("start", DuelAdminCommandKind.Start)]
    [InlineData("start confirm", DuelAdminCommandKind.StartConfirm)]
    [InlineData("stop confirm", DuelAdminCommandKind.StopConfirm)]
    [InlineData("pause", DuelAdminCommandKind.Pause)]
    [InlineData("resume", DuelAdminCommandKind.Resume)]
    [InlineData("maps", DuelAdminCommandKind.Maps)]
    public void Parses_fixed_commands(string raw, DuelAdminCommandKind expected)
    {
        Assert.Equal(expected, DuelAdminCommandParser.Parse(raw.Split(' ')).Kind);
    }

    [Fact]
    public void Parses_rounds_time_utility_and_map()
    {
        Assert.Equal((8, 16, 12), DuelAdminCommandParser.Parse(["rounds", "8", "16", "12"]).Rounds);
        Assert.Equal(1.25, DuelAdminCommandParser.Parse(["time", "1.25"]).RoundTimeMinutes);
        Assert.Equal("random2", DuelAdminCommandParser.Parse(["utility", "random2"]).Value);
        Assert.Equal("3250543760", DuelAdminCommandParser.Parse(["map", "3250543760"]).Value);
    }

    [Fact]
    public void Invalid_syntax_returns_chinese_error()
    {
        var result = DuelAdminCommandParser.Parse(["rounds", "8", "x", "12"]);
        Assert.Equal(DuelAdminCommandKind.Invalid, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
```

在 `DuelGameSessionTests` 增加：总和 29、时间 0.2、未知道具被拒绝；0/30/0、时间 0.25 和 `random3` 被接受；运行中修改被拒绝。

- [ ] **Step 2: 运行并确认失败。**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter "DuelAdminCommandParserTests|DuelGameSessionTests"
```

Expected: FAIL，缺少解析器和配置更新接口。

- [ ] **Step 3: 实现解析器和集中校验。**

使用以下稳定接口：

```csharp
public enum DuelAdminCommandKind
{
    Invalid, Help, Status, Rounds, Time, Utility, Reset,
    Start, StartConfirm, Pause, Resume, Stop, StopConfirm, Maps, Map
}

public sealed record DuelAdminCommand(
    DuelAdminCommandKind Kind,
    (int Pistol, int Rifle, int Sniper)? Rounds = null,
    double? RoundTimeMinutes = null,
    string? Value = null,
    string? Error = null);

public static class DuelAdminCommandParser
{
    public static DuelAdminCommand Parse(IReadOnlyList<string> args)
    {
        var values = args.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        if (values.Length == 0) return new(DuelAdminCommandKind.Help);
        var verb = values[0].ToLowerInvariant();
        if (values.Length == 1)
        {
            var fixedKind = verb switch
            {
                "help" => DuelAdminCommandKind.Help,
                "status" => DuelAdminCommandKind.Status,
                "reset" => DuelAdminCommandKind.Reset,
                "start" => DuelAdminCommandKind.Start,
                "pause" => DuelAdminCommandKind.Pause,
                "resume" => DuelAdminCommandKind.Resume,
                "stop" => DuelAdminCommandKind.Stop,
                "maps" => DuelAdminCommandKind.Maps,
                _ => DuelAdminCommandKind.Invalid
            };
            return fixedKind == DuelAdminCommandKind.Invalid
                ? new(fixedKind, Error: "未知子命令，请使用 /duel help。")
                : new(fixedKind);
        }
        if (values.Length == 2 && verb == "start" && values[1].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return new(DuelAdminCommandKind.StartConfirm);
        if (values.Length == 2 && verb == "stop" && values[1].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return new(DuelAdminCommandKind.StopConfirm);
        if (values.Length == 2 && verb == "time" && double.TryParse(values[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes))
            return new(DuelAdminCommandKind.Time, RoundTimeMinutes: minutes);
        if (values.Length == 2 && verb == "utility") return new(DuelAdminCommandKind.Utility, Value: values[1].ToLowerInvariant());
        if (values.Length >= 2 && verb == "map") return new(DuelAdminCommandKind.Map, Value: string.Join(' ', values.Skip(1)));
        if (values.Length == 4 && verb == "rounds" && int.TryParse(values[1], out var pistol) && int.TryParse(values[2], out var rifle) && int.TryParse(values[3], out var sniper))
            return new(DuelAdminCommandKind.Rounds, (pistol, rifle, sniper));
        return new(DuelAdminCommandKind.Invalid, Error: "参数格式错误，请使用 /duel help 查看用法。");
    }
}
```

`TryUpdateConfig` 必须验证：空闲状态、每阶段 0～99、总和至少 30、时间 0.25～5、道具属于 `none/random1/random2/random3/full`。`ResetConfig()` 恢复 `new DuelGameConfig()`。

- [ ] **Step 4: 运行解析与状态机测试。**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj --filter "DuelAdminCommandParserTests|DuelGameSessionTests"
```

Expected: PASS。

- [ ] **Step 5: 提交命令领域层。**

```powershell
git add web-command-center/CaorenCupPlugin/DuelAdminCommandParser.cs web-command-center/CaorenCupPlugin/DuelGameSession.cs web-command-center/CaorenCupPlugin.Tests/DuelAdminCommandParserTests.cs web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs
git diff --cached --stat
git commit -m "feat: parse game-managed duel commands"
```

## Task 3: 接入管理员指令、地图和网页模式互斥

**Files:**
- Create: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260725-game-duel-commands`（本地备份，不提交）
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs`

**Interfaces:**
- Consumes: `DuelGameSession`、`DuelAdminCommandParser.Parse`。
- Produces: `OnDuelAdminCommand`、`StartGameManagedDuel`、`ShowDuelStatus`、`ApplyDuelAdminConfig`、`SwitchDuelMap`。
- `ConfigureDuelMode` 在 `GameManaged` 期间拒绝网页接管；否则进入 `WebManaged`。

- [ ] **Step 1: 建立主插件备份并记录基线。**

```powershell
Copy-Item web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260725-game-duel-commands
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
```

Expected: 现有测试全部 PASS；备份出现在 ignored 状态且不得暂存。

- [ ] **Step 2: 注册统一指令并保留兼容别名。**

在 `Load` 中新增：

```csharp
AddCommand("css_duel", "游戏内独立单挑管理。用法：/duel help", OnDuelAdminCommand);
```

把现有 `css_duel_map`、`css_duel_maps` 处理器改为调用与 `/duel map`、`/duel maps` 相同的本地方法；不再调用 `SetDuelMapAsync` 或 `/api/plugin/duel-map`。

- [ ] **Step 3: 实现权限、配置、状态与地图分发。**

`OnDuelAdminCommand` 必须先执行：

```csharp
if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
{
    ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以管理单挑。");
    return;
}
```

随后把 `CommandInfo` 的参数转换为字符串数组交给解析器，并完整分发 `help/status/rounds/time/utility/reset/start/start confirm/pause/resume/stop/stop confirm/maps/map`。配置成功后回显当前完整配置；非法操作回显状态机的中文错误。

`SwitchDuelMap` 只允许非 `GameManaged` 运行状态，解析现有 `DuelWorkshopMaps` 后执行：

```csharp
Server.ExecuteCommand($"host_workshop_map {map.WorkshopId}");
```

- [ ] **Step 4: 实现网页互斥和确认接管入口。**

`ConfigureDuelMode` 开头增加：

```csharp
if (_duelSession.ControlMode == DuelControlMode.GameManaged)
{
    Logger.LogWarning("Rejected CONFIGURE_DUEL_MODE because a game-managed duel is active.");
    return;
}
```

正常解析网页配置后调用 `_duelSession.EnterWebManaged(config)`。`/duel start` 遇到 `WebManaged` 返回确认提示；`/duel start confirm` 才允许状态机清除旧网页状态并开始。网页模式打满配置总回合时把控制模式清回 `None`；网页强制结束后残留的状态仍由确认接管安全处理。

- [ ] **Step 5: 构建并手动检查命令注册。**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj
rg -n "css_duel|OnDuelAdminCommand|SetDuelMapAsync|api/plugin/duel-map" web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs
```

Expected: 测试和构建 PASS；统一命令已注册；游戏内地图处理路径不再调用网页 API。

- [ ] **Step 6: 提交管理员指令接入。**

```powershell
git add web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs
git status --short --ignored
git diff --cached --stat
git commit -m "feat: add in-game duel administration"
```

## Task 4: 实现服务器配置作用域与比赛生命周期

**Files:**
- Create: `web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs`
- Create: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260725-game-duel-runtime`（本地备份，不提交）
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs`
- Modify: `web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs`

**Interfaces:**
- Produces `DuelServerCvarScope.Set(name, value, fallback)` 与 `RestoreAll()`。
- `StartGameManagedDuel` 捕获当前 T/CT 真人、应用规则、锁队并执行 `mp_restartgame 1`。
- `FinishGameManagedDuel` 和 `AbortGameManagedDuel` 统一走幂等清理路径。

- [ ] **Step 1: 备份主插件并增加回合门闩测试。**

```powershell
Copy-Item web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260725-game-duel-runtime
```

在状态机测试中补充：暂停期间的 `RecordRoundEnd` 不计分；结束后再次 `RecordRoundEnd` 不计分；`Clear()` 可重复调用且回到 `None/Idle`。

- [ ] **Step 2: 实现 cvar 捕获与恢复。**

`DuelServerCvarScope.cs` 采用现有 `ManagedCvarScope` 模式，但只服务独立单挑：

```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCupPlugin;

public sealed class DuelServerCvarScope
{
    private readonly Dictionary<string, string> _restore = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value, string fallback)
    {
        if (!_restore.ContainsKey(name))
            _restore[name] = ConVar.Find(name)?.GetPrimitiveValue<string>() ?? fallback;
        Server.ExecuteCommand($"{name} {value}");
    }

    public void RestoreAll()
    {
        foreach (var item in _restore) Server.ExecuteCommand($"{item.Key} {item.Value}");
        _restore.Clear();
    }
}
```

读取单个 cvar 失败时捕获异常并使用传入 fallback，确保其他参数仍可恢复。

- [ ] **Step 3: 完成开赛原子流程。**

`StartGameManagedDuel(bool confirmWebTakeover)`：

1. 从 `Utilities.GetPlayers()` 过滤真人，收集当前 `CsTeam.Terrorist`/`CounterTerrorist`。
2. 先调用状态机验证；失败时不改 cvar、不改队伍。
3. 把参赛者写入 `_teamAssignments`，启用锁队且有效期不限制回合。
4. 清空旧比分、枪械选择、AWP 请求和保护状态。
5. 用 `DuelServerCvarScope.Set` 设置 `mp_maxrounds`、`mp_winlimit 0`、`mp_match_can_clinch 0`、`mp_roundtime`、`mp_freezetime 0`、`mp_round_restart_delay 2`、`mp_free_armor 0`、`mp_halftime 0`、`mp_autoteambalance 0`、`mp_limitteams 0`。
6. 执行 `mp_warmup_end` 与 `mp_restartgame 1`。
7. 广播配置、T/CT 参赛名单和开赛成功消息。

若步骤 3～6 抛出异常，立即执行统一异常清理并广播失败原因。

- [ ] **Step 4: 把正式回合事件接入状态机。**

`OnRoundStart` 在 `GameManaged` 且非暂停时调用 `MarkRoundStarted()`，再复用现有出生保护、武器和阶段公告。

`OnRoundEnd` 仅在赢家为 T/CT 且回合门闩已打开时计分：

```csharp
var duelResult = _duelSession.RecordRoundEnd(
    winner == "T" ? DuelTeam.Terrorist : DuelTeam.CounterTerrorist);
if (duelResult.Finished) FinishGameManagedDuel(duelResult);
```

普通模式和 `WebManaged` 继续沿用现有 `_scoreT/_scoreCt` 行为；不要让重启产生的重复 `round_end` 被独立单挑计分。

- [ ] **Step 5: 实现幂等正常结束与异常清理。**

统一清理顺序：

1. 若暂停则执行 `mp_unpause_match`。
2. 解除 `_teamLockEnabled`，清空 `_teamAssignments` 与 bypass。
3. 清空武器选择、AWP、保护、正式回合和本地比分。
4. `DuelServerCvarScope.RestoreAll()`。
5. 执行 `mp_restartgame 1`，使恢复后的配置生效。
6. 最后 `DuelGameSession.Clear()`，确保重复清理无副作用。

正常结束在清理前广播最终比分和 T 胜/CT 胜/平局；强制或异常终止只广播终止原因。

- [ ] **Step 6: 运行测试和构建。**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj
```

Expected: PASS，无 nullable 警告新增。

- [ ] **Step 7: 提交比赛生命周期。**

```powershell
git add web-command-center/CaorenCupPlugin/DuelServerCvarScope.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs
git diff --cached --stat
git commit -m "feat: run standalone duel lifecycle"
```

## Task 5: 实现掉线暂停、重连锁队、观察者限制与网页隔离

**Files:**
- Create: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260725-game-duel-safety`（本地备份，不提交）
- Modify: `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs`
- Modify: `web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs`

**Interfaces:**
- Produces: `EvaluateGameManagedConnectivity()`、`PauseGameManagedDuel(reason)`、`ResumeGameManagedDuel()`、`ShouldPublishMatchTelemetry()`。
- `EnforceTeamAssignments` 在 `GameManaged` 下同时恢复参赛者原队和驱逐非参赛者到观察者席。

- [ ] **Step 1: 备份并补充在线状态测试。**

```powershell
Copy-Item web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260725-game-duel-safety
```

补充测试：任一参赛者首次缺席只触发一次自动暂停；重复轮询不重复触发；重连后仍暂停；一方完全无在线参赛者时 `TryResume` 返回明确中文错误。

- [ ] **Step 2: 每秒检测参赛者在线状态。**

在现有 1 秒队伍锁定 timer 中依次调用：

```csharp
EnforceTeamAssignments();
EvaluateGameManagedConnectivity();
```

`EvaluateGameManagedConnectivity` 用当前真人 SteamID 集合调用状态机；首次转为暂停时执行 `mp_pause_match` 并广播缺席玩家名单。玩家回来后只更新在线状态并提示管理员使用 `/duel resume`。

- [ ] **Step 3: 接入手动暂停与恢复。**

- `/duel pause`：仅 `GameManaged/Running` 可用，调用状态机 `Pause("管理员手动暂停")` 后执行 `mp_pause_match`。
- `/duel resume`：调用 `TryResume`；成功才执行 `mp_unpause_match`，失败回显具体原因。
- 暂停时关闭回合门闩，暂停期间到达的 `round_end` 不计分。

- [ ] **Step 4: 强化锁队和观察者规则。**

`EnforceTeamAssignments` 在 `GameManaged` 下：

- 参赛者不在记录队伍时，用现有 bypass + `ChangeTeam` 恢复。
- 非参赛真人处于 T/CT 时移到 `CsTeam.Spectator`。
- 观察者不加入 `_teamAssignments`。

`OnJoinTeamCommand` 在 `GameManaged` 下：

- 参赛者尝试换边时拦截并提示原队伍。
- 非参赛者尝试加入 T/CT 时拦截并提示本局只能观察。
- 插件自身队伍修正使用 bypass，避免递归拦截。

- [ ] **Step 5: 屏蔽独立单挑网页比赛遥测。**

新增：

```csharp
private bool ShouldPublishMatchTelemetry() => _duelSession.ControlMode != DuelControlMode.GameManaged;
```

并在以下入口阻断游戏内独立单挑数据：

- `QueueEvent` 直接返回；
- `QueueSnapshot` 直接返回；
- 周期 `SendSnapshotAsync` timer 在 `GameManaged` 时跳过；
- 手动 `/ccsnapshot` 在 `GameManaged` 时提示已禁用。

`SendHeartbeatAsync`、身份绑定和网页状态刷新保持不变。

- [ ] **Step 6: 处理换图与卸载异常终止。**

- `OnMapStart` 若进入回调前为 `GameManaged`，在下一帧执行异常清理并记录“比赛中发生换图”。
- `Unload` 在停止 timer 和释放 HTTP 之前调用无异步依赖的清理方法；不得在卸载阶段排队网页请求。
- 地图指令在 `GameManaged` 运行或暂停时拒绝。

- [ ] **Step 7: 运行完整测试与构建。**

```powershell
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj
```

Expected: PASS；构建产物仅位于 ignored 的 `bin/obj`。

- [ ] **Step 8: 提交安全与隔离行为。**

```powershell
git add web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs web-command-center/CaorenCupPlugin.Tests/DuelGameSessionTests.cs
git status --short --ignored
git diff --cached --stat
git commit -m "feat: isolate and protect in-game duels"
```

## Task 6: 回归、文档与本地交付检查

**Files:**
- Create: `docs/duel-mode-test-flow.md.bak-20260725-game-duel`（本地备份，不提交）
- Create: `scripts/test-duel-mode-local.ps1.bak-20260725-game-duel-tests`（本地备份，不提交）
- Modify: `docs/duel-mode-test-flow.md`
- Modify: `scripts/test-duel-mode-local.ps1`

**Interfaces:**
- Produces: 可重复执行的独立单挑本地测试门和真人服务器验收清单。

- [ ] **Step 1: 备份测试文档并检查现有本地脚本。**

```powershell
Copy-Item docs/duel-mode-test-flow.md docs/duel-mode-test-flow.md.bak-20260725-game-duel
Copy-Item scripts/test-duel-mode-local.ps1 scripts/test-duel-mode-local.ps1.bak-20260725-game-duel-tests
```

在路径变量区增加：

```powershell
$bridgeTestsRoot = Join-Path $webRoot 'CaorenCupPlugin.Tests'
```

在桥接插件构建步骤之前增加：

```powershell
Invoke-CheckedStep `
    -Name 'Web bridge plugin tests' `
    -WorkingDirectory $bridgeTestsRoot `
    -Command 'dotnet' `
    -Arguments @('test')
```

- [ ] **Step 2: 更新独立单挑验收文档。**

增加以下明确清单：

- `/duel help/status/reset`；
- 配置合法值和非法值；
- 当前 T/CT 取名单、观察者排除、新加入者观察；
- 网页状态残留时 `/duel start confirm`；
- 0 回合阶段边界和 8/16/12 默认边界；
- 掉线自动暂停、同 SteamID 回原队、管理员恢复；
- `/duel stop confirm`；
- 打满后 T 胜/CT 胜/平局；
- 游戏内单挑不在网页显示；
- 正常、强制和异常结束后 cvar、锁队、武器与暂停状态已清理。

- [ ] **Step 3: 执行本地验证门。**

```powershell
.\scripts\test-duel-mode-local.ps1
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj -c Release
```

Expected: 所有命令退出码 0。若项目脚本已经包含后两项，避免重复执行并在交付记录中说明。

- [ ] **Step 4: 做静态安全检查。**

```powershell
rg -n "GameManaged|WebManaged|ShouldPublishMatchTelemetry|css_duel|mp_pause_match|mp_unpause_match" web-command-center/CaorenCupPlugin
git diff --check
git status --short --ignored
```

Expected: 控制模式、遥测门和暂停指令均有对应实现；无空白错误；备份、`bin/obj` 均 ignored；私有插件未暂存。

- [ ] **Step 5: 提交文档和测试门改动。**

```powershell
git add docs/duel-mode-test-flow.md
if (git diff -- scripts/test-duel-mode-local.ps1) { git add scripts/test-duel-mode-local.ps1 }
git diff --cached --stat
git commit -m "docs: add standalone duel test flow"
```

- [ ] **Step 6: 请求代码审查并记录未完成的真实服务器验证。**

使用 `requesting-code-review` 技能审查设计符合性、状态清理、网页回归与误计分风险。审查问题修复后再次运行 Task 6 Step 3。

交付说明必须明确：本地测试通过不等于真实 CS2 多人验收通过；服务器上传、覆盖和重启尚未授权，不在本轮自动执行。

---

## 实施完成标准

- 全部新增单元测试通过，桥接插件 Debug/Release 构建通过。
- `/duel` 管理指令、兼容地图别名和中文错误提示完整。
- 当前 T/CT 真人名单、SteamID 锁队、观察者隔离正常。
- 阶段自动推进、打满结算、平局和回合门闩正确。
- 掉线自动暂停、重连回原队、管理员恢复正常。
- `WebManaged` 与 `GameManaged` 互斥，确认接管可处理旧网页状态。
- 游戏内独立单挑不发送比赛事件或快照到网页，普通心跳不受影响。
- 正常结束、强制终止、换图和卸载都能幂等清理并恢复 cvar。
- Git 暂存区不包含备份、构建产物、日志、密钥配置或私有 Bot 插件。
