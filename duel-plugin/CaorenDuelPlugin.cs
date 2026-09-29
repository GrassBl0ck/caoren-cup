using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using static CaorenDuel.DuelPluginUtils;

namespace CaorenDuel;

[MinimumApiVersion(374)]
public sealed class CaorenDuelPlugin : BasePlugin
{
    public override string ModuleName => "CaorenDuel";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "CaorenCup";
    public override string ModuleDescription => "Standalone game-managed duel mode for the CaorenCup community servers.";

    private const string BridgeBeginCommand = "css_duelbridge_begin";
    private const string BridgeCleanupRestartCommand = "css_duelbridge_cleanup_restart";
    private const string BridgeRoundStartCommand = "css_duelbridge_round_start";
    private const string BridgeCvarReadyCommand = "css_duelbridge_cvar_ready";

    private readonly DuelGameSession _duelSession = new();
    private readonly DuelServerCvarScope _duelServerCvars = new();
    private readonly DuelCleanupState _duelCleanupState = new();
    private readonly List<CounterStrikeSharp.API.Modules.Timers.Timer> _timers = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _duelSafetyTimer;
    private volatile bool _isUnloading;
    private readonly Dictionary<string, CsTeam> _teamAssignments = new(StringComparer.Ordinal);
    private readonly HashSet<string> _teamAssignmentBypass = new(StringComparer.Ordinal);
    private bool _teamLockEnabled;
    private bool _duelModeEnabled;
    private int _duelPistolRounds = 8;
    private int _duelRifleRounds = 16;
    private int _duelSniperRounds = 12;
    private string _duelUtilityMode = "none";
    private int _duelFormalRound;
    private DuelStage? _duelLastAnnouncedStage;
    private float _duelRoundProtectionEndsAt;
    private readonly Dictionary<string, float> _duelProtectionNoticeAt = new(StringComparer.Ordinal);
    private readonly Random _duelRandom = new();
    private readonly Dictionary<string, string> _duelPendingPrimary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _duelPendingSecondary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _duelCurrentPrimary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _duelCurrentSecondary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingAwpRequest> _duelAwpRequests = new(StringComparer.Ordinal);
    private int _duelLoadoutGeneration;
    private bool _gameManagedDuelRuntimeActive;
    private bool _duelCvarRestorePending;
    private static Action<Action> MapStartLifecycleSchedule => Server.NextWorldUpdate;
    private static readonly DuelWorkshopMap[] DuelWorkshopMaps =
    [
        new("5e_akm4_aim_duel", "3250543760"),
        new("AIM Map", "3084291314"),
        new("aim_awp [CS2 Port]", "3444237717"),
        new("The_Arena", "3529094738"),
        new("5e_awp_space", "3250550000"),
        new("AIM TRAINING DUEL", "3714852830"),
        new("AimDuel", "3581460570"),
    ];
    private static readonly Dictionary<string, string> DuelWeaponAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usp"] = "weapon_usp_silencer",
        ["glock"] = "weapon_glock",
        ["p2k"] = "weapon_hkp2000",
        ["elite"] = "weapon_elite",
        ["fn57"] = "weapon_fiveseven",
        ["cz75"] = "weapon_cz75a",
        ["r8"] = "weapon_revolver",
        ["tec9"] = "weapon_tec9",
        ["deagle"] = "weapon_deagle",
        ["p250"] = "weapon_p250",
        ["ak"] = "weapon_ak47",
        ["a1"] = "weapon_m4a1_silencer",
        ["a4"] = "weapon_m4a1",
        ["famas"] = "weapon_famas",
        ["galil"] = "weapon_galilar",
        ["aug"] = "weapon_aug",
        ["553"] = "weapon_sg556",
        ["ssg"] = "weapon_ssg08",
        ["awp"] = "weapon_awp",
        ["mac10"] = "weapon_mac10",
        ["mp9"] = "weapon_mp9",
        ["p90"] = "weapon_p90",
        ["mp7"] = "weapon_mp7",
        ["ump"] = "weapon_ump45",
        ["bizon"] = "weapon_bizon",
        ["negev"] = "weapon_negev",
        ["mp5"] = "weapon_mp5sd",
    };
    private static readonly HashSet<string> DuelPistolAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "usp", "glock", "p2k", "elite", "fn57", "cz75", "r8", "tec9", "deagle", "p250"
    };
    private static readonly HashSet<string> DuelRifleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "ak", "a1", "a4", "famas", "galil", "aug", "553", "ssg", "awp",
        "mac10", "mp9", "p90", "mp7", "ump", "bizon", "negev", "mp5"
    };

    public override void Load(bool hotReload)
    {
        _isUnloading = false;
        AddCommand("css_guns", "查看单挑模式可切换枪械。用法：/guns", OnDuelGunsCommand);
        AddCommand("css_agree_awp", "同意对方在步枪阶段使用 AWP。用法：/agree_awp", OnDuelAgreeAwpCommand);
        AddCommand("css_duel", "游戏内独立单挑管理。用法：/duel help", OnDuelAdminCommand);
        AddCommand("css_dp", "暂停单挑。用法：/dp", OnDuelPauseAliasCommand);
        AddCommand("css_dr", "恢复单挑。用法：/dr", OnDuelResumeAliasCommand);
        AddCommand("css_duel_map", "切换单挑创意工坊地图。用法：/duel_map <序号|地图名|创意工坊ID>", OnDuelMapCommand);
        AddCommand("css_duel_maps", "查看可切换的单挑地图。用法：/duel_maps", OnDuelMapsCommand);
        RegisterDuelWeaponCommands();
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        AddCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        AddCommandListener("drop", OnDuelDropCommand, HookMode.Pre);
        StartDuelSafetyTimer();
        Logger.LogInformation("{Name} loaded.", ModuleName);
    }

    public override void Unload(bool hotReload)
    {
        _isUnloading = true;
        CleanupDuelImmediately();
        StopTimers();
        RemoveCommandListener("jointeam", OnJoinTeamCommand, HookMode.Pre);
        RemoveCommandListener("drop", OnDuelDropCommand, HookMode.Pre);
    }

    private void StopTimers()
    {
        foreach (var timer in _timers.ToArray())
        {
            try
            {
                timer.Kill();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to stop CaorenDuel plugin timer");
            }
        }

        _timers.Clear();
        _duelSafetyTimer = null;
    }

    private void StartDuelSafetyTimer()
    {
        if (_duelSafetyTimer != null)
        {
            _timers.Remove(_duelSafetyTimer);
            try
            {
                _duelSafetyTimer.Kill();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to replace duel safety timer");
            }
        }

        _duelSafetyTimer = AddTimer(1.0f, () =>
        {
            if (_isUnloading) return;
            RetryPendingGameManagedDuelCvarsAtSafePoint();
            EvaluateGameManagedConnectivity();
            EnforceTeamAssignments();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        _timers.Add(_duelSafetyTimer);
    }

    // ---- 跨插件通知（发给桥接插件，仅服务器控制台来源会被桥接接受） ----

    private static void NotifyDuelBridgeBegin() => Server.ExecuteCommand(BridgeBeginCommand);

    private static void NotifyDuelBridgeCleanupRestart() => Server.ExecuteCommand(BridgeCleanupRestartCommand);

    private static void NotifyDuelBridgeRoundStart() => Server.ExecuteCommand(BridgeRoundStartCommand);

    private static void NotifyDuelBridgeCvarRestoreReady(bool ready) =>
        Server.ExecuteCommand($"{BridgeCvarReadyCommand} {(ready ? "1" : "0")}");

    private void RegisterDuelWeaponCommands()
    {
        foreach (var alias in DuelWeaponAliases.Keys)
        {
            AddCommand("css_" + alias, $"单挑模式切换枪械：/{alias}", OnDuelWeaponCommand);
        }
    }

    private void OnDuelGunsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player))
        {
            ReplyToPlayer(player, "[草人杯] 当前没有启用单挑枪械规则。");
            return;
        }

        var stage = GetDuelStage();
        if (stage == DuelStage.Pistol)
        {
            ReplyToPlayer(player, "[草人杯] 手枪阶段可切换：/usp /glock /p2k /elite /fn57 /cz75 /r8 /tec9 /deagle /p250");
        }
        else if (stage == DuelStage.Rifle)
        {
            ReplyToPlayer(player, "[草人杯] 步枪阶段可切换：/ak /a1 /a4 /famas /galil /aug /553 /ssg /awp");
            ReplyToPlayer(player, "[草人杯] 也可切换：/mac10 /mp9 /p90 /mp7 /ump /bizon /negev /mp5");
            ReplyToPlayer(player, "[草人杯] 副武器可切换：/usp /glock /p2k /elite /fn57 /cz75 /r8 /tec9 /deagle /p250");
        }
        else
        {
            ReplyToPlayer(player, "[草人杯] 狙击阶段可切换：/ssg /awp");
        }
    }

    private void OnDuelWeaponCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player)) return;
        var alias = command.GetCommandString.TrimStart('!', '/').Replace("css_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (!DuelWeaponAliases.TryGetValue(alias, out var weapon)) return;
        RequestDuelWeapon(player!, alias, weapon);
    }

    private void OnDuelAgreeAwpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player)) return;
        var steamId = player!.SteamID.ToString();
        var myTeam = player.Team;
        var request = _duelAwpRequests.Values.FirstOrDefault(r => r.RequesterTeam != myTeam && r.RequiredApprovals.Contains(steamId));
        if (request == null)
        {
            ReplyToPlayer(player, "[草人杯] 当前没有需要你确认的 AWP 申请。");
            return;
        }

        request.Approvals.Add(steamId);
        ReplyToPlayer(player, "[草人杯] 已同意对方下回合使用 AWP。");
        var missing = request.RequiredApprovals.Where(id => !request.Approvals.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            PrintToAllPlayers($"AWP 申请还需 {missing.Count} 名对方玩家同意。");
            return;
        }

        _duelPendingPrimary[request.RequesterSteamId] = "weapon_awp";
        _duelAwpRequests.Remove(request.RequesterSteamId);
        PrintToAllPlayers("AWP 申请已通过，下回合生效。");
    }

    private void OnDuelMapsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以管理单挑。");
            return;
        }

        ShowDuelMapHelp(player, command);
    }

    private void OnDuelMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以管理单挑。");
            return;
        }

        var inputParts = new List<string>();
        for (var i = 1; i < command.ArgCount; i++)
        {
            var part = command.ArgByIndex(i);
            if (!string.IsNullOrWhiteSpace(part)) inputParts.Add(part.Trim());
        }
        SwitchDuelMap(player, command, string.Join(' ', inputParts));
    }

    private void OnDuelAdminCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以管理单挑。");
            return;
        }

        var args = new List<string>();
        for (var i = 1; i < command.ArgCount; i++)
        {
            var arg = command.ArgByIndex(i);
            if (!string.IsNullOrWhiteSpace(arg)) args.Add(arg.Trim());
        }

        var parsed = DuelAdminCommandParser.Parse(args);
        switch (parsed.Kind)
        {
            case DuelAdminCommandKind.Help:
                ShowDuelAdminHelp(player, command);
                break;
            case DuelAdminCommandKind.Status:
                ShowDuelStatus(player, command);
                break;
            case DuelAdminCommandKind.Rounds:
                var rounds = parsed.Rounds!.Value;
                ApplyDuelAdminConfig(player, command, _duelSession.Config with
                {
                    PistolRounds = rounds.Pistol,
                    RifleRounds = rounds.Rifle,
                    SniperRounds = rounds.Sniper
                });
                break;
            case DuelAdminCommandKind.Time:
                ApplyDuelAdminConfig(player, command, _duelSession.Config with
                {
                    RoundTimeMinutes = parsed.RoundTimeMinutes!.Value
                });
                break;
            case DuelAdminCommandKind.Nades:
                ApplyDuelAdminConfig(player, command, _duelSession.Config with
                {
                    UtilityMode = parsed.Value ?? string.Empty
                });
                break;
            case DuelAdminCommandKind.Reset:
                ApplyDuelAdminConfig(player, command, new DuelGameConfig());
                break;
            case DuelAdminCommandKind.Start:
                StartGameManagedDuel(player, command);
                break;
            case DuelAdminCommandKind.Pause:
                PauseGameManagedDuel(player, command);
                break;
            case DuelAdminCommandKind.Resume:
                ResumeGameManagedDuel(player, command);
                break;
            case DuelAdminCommandKind.Stop:
                StopGameManagedDuel(player, command);
                break;
            case DuelAdminCommandKind.Maps:
                ShowDuelMapHelp(player, command);
                break;
            case DuelAdminCommandKind.Map:
                SwitchDuelMap(player, command, parsed.Value ?? string.Empty);
                break;
            default:
                ReplyToDuelCaller(player, command, $"[草人杯] {parsed.Error ?? "未知子命令，请使用 /duel help。"}");
                break;
        }
    }

    private void OnDuelPauseAliasCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以管理单挑。");
            return;
        }

        PauseGameManagedDuel(player, command);
    }

    private void OnDuelResumeAliasCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有服务器管理员可以管理单挑。");
            return;
        }

        ResumeGameManagedDuel(player, command);
    }

    private void ShowDuelAdminHelp(CCSPlayerController? player, CommandInfo command)
    {
        foreach (var line in BuildDuelAdminHelpLines())
        {
            ReplyToDuelCaller(player, command, line);
        }
    }

    private static IReadOnlyCollection<string> BuildDuelAdminHelpLines() =>
    [
        "[草人杯] 推荐顺序：先切换地图，等待玩家重连并选择 T/CT，再配置回合数、时间和道具，最后 /duel start 开赛。",
        "[草人杯] 单挑仅由游戏内指令管理。",
        "[草人杯] /duel status：查看单挑状态和完整配置",
        "[草人杯] /duel rounds <手枪> <步枪> <狙击>：设置阶段回合数，总和至少 1",
        "[草人杯] /duel time <分钟>：设置每回合 0.25～5 分钟",
        "[草人杯] /duel nades <none|random1|random2|random3|full>：设置道具",
        "[草人杯] /duel reset：恢复默认配置；/duel start：按当前 T/CT 真人开赛",
        "[草人杯] /duel pause（/dp）、/duel resume（/dr）、/duel stop：控制比赛",
        "[草人杯] /duel maps；/duel map <序号|地图名|创意工坊ID>：查看或切换地图"
    ];

    private void StartGameManagedDuel(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelCleanupState.RestartPending &&
            _duelCvarRestorePending)
        {
            RetryPendingGameManagedDuelCvarsAtSafePoint();
        }

        if (IsDuelStartBlocked(
            _duelCleanupState.RestartPending,
            !_duelCvarRestorePending))
        {
            var reason = _duelCleanupState.RestartPending
                ? "上一局清理重启尚未完成"
                : "上一次比赛的服务器参数仍在恢复中";
            ReplyToDuelCaller(player, command, $"[草人杯] 无法开始单挑：{reason}，请稍后重试。");
            return;
        }

        var participants = Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .Where(candidate => candidate.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            .Select(candidate => new DuelParticipant(
                candidate.SteamID.ToString(),
                SafePlayerName(candidate),
                candidate.Team == CsTeam.Terrorist ? DuelTeam.Terrorist : DuelTeam.CounterTerrorist))
            .ToArray();

        if (!_duelSession.TryStart(participants, out var error))
        {
            ReplyToDuelCaller(player, command, $"[草人杯] 无法开始单挑：{error}");
            return;
        }

        NotifyDuelBridgeBegin();
        _gameManagedDuelRuntimeActive = true;
        _duelCvarRestorePending = false;
        try
        {
            var config = _duelSession.Config;
            _teamAssignments.Clear();
            foreach (var participant in participants)
            {
                _teamAssignments[participant.SteamId] = participant.Team == DuelTeam.Terrorist
                    ? CsTeam.Terrorist
                    : CsTeam.CounterTerrorist;
            }

            _teamAssignmentBypass.Clear();
            _teamLockEnabled = true;

            ActivateDuelRuntime(config);

            var tNames = string.Join("、", participants
                .Where(item => item.Team == DuelTeam.Terrorist)
                .Select(item => item.Name));
            var ctNames = string.Join("、", participants
                .Where(item => item.Team == DuelTeam.CounterTerrorist)
                .Select(item => item.Name));
            PrintToAllPlayers(FormatDuelConfig(config));
            PrintToAllPlayers($"T 参赛者：{tNames}");
            PrintToAllPlayers($"CT 参赛者：{ctNames}");
            PrintToAllPlayers("独立单挑已正式开始，参赛队伍已锁定。");
            if (player == null)
            {
                ReplyToDuelCaller(player, command, $"[草人杯] 独立单挑已正式开始：T {tNames}；CT {ctNames}。");
                ReplyToDuelCaller(player, command, $"[草人杯] {FormatDuelConfig(config)}");
            }
            Logger.LogInformation(
                "Started game-managed duel session with {TCount} T and {CtCount} CT participants.",
                participants.Count(item => item.Team == DuelTeam.Terrorist),
                participants.Count(item => item.Team == DuelTeam.CounterTerrorist));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start game-managed duel; restoring server state.");
            AbortGameManagedDuel($"开赛失败：{ex.Message}");
            ReplyToDuelCaller(player, command, $"[草人杯] 独立单挑开赛失败，已清理比赛状态：{ex.Message}");
        }
    }

    private void ShowDuelStatus(CCSPlayerController? player, CommandInfo command)
    {
        var status = _duelSession.ControlMode == DuelControlMode.GameManaged
            ? $"游戏内管理 / {DuelLifecycleLabel(_duelSession.Lifecycle)}"
            : "未启用 / 未开始";
        var completedRounds = _duelSession.CompletedRounds;
        var scoreT = _duelSession.ScoreT;
        var scoreCt = _duelSession.ScoreCt;

        ReplyToDuelCaller(player, command, $"[草人杯] 单挑状态：{status}；已完成 {completedRounds} 回合；比分 T {scoreT} : {scoreCt} CT。");
        ReplyToDuelCaller(player, command, $"[草人杯] {FormatDuelConfig(_duelSession.Config)}");
        if (_duelSession.ControlMode == DuelControlMode.GameManaged)
        {
            foreach (var line in BuildGameManagedDuelStatusLines(_duelSession))
            {
                ReplyToDuelCaller(player, command, line);
            }
        }
        if (_duelSession.ControlMode == DuelControlMode.GameManaged && !string.IsNullOrWhiteSpace(_duelSession.PauseReason))
        {
            ReplyToDuelCaller(player, command, $"[草人杯] 暂停原因：{_duelSession.PauseReason}");
        }
    }

    private static IReadOnlyCollection<string> BuildGameManagedDuelStatusLines(DuelGameSession session)
    {
        var tNames = session.Participants.Values
            .Where(participant => participant.Team == DuelTeam.Terrorist)
            .Select(participant => participant.Name)
            .ToArray();
        var ctNames = session.Participants.Values
            .Where(participant => participant.Team == DuelTeam.CounterTerrorist)
            .Select(participant => participant.Name)
            .ToArray();
        var stage = session.CurrentStage switch
        {
            DuelGameStage.Pistol => "手枪",
            DuelGameStage.Rifle => "步枪",
            DuelGameStage.Sniper => "狙击枪",
            _ => session.CurrentStage.ToString()
        };
        var remainingRounds = Math.Max(0, session.Config.TotalRounds - session.CompletedRounds);
        return
        [
            $"[草人杯] 当前阶段：{stage}；剩余 {remainingRounds} 回合。",
            $"[草人杯] T 参赛者（{tNames.Length}）：{string.Join("、", tNames)}",
            $"[草人杯] CT 参赛者（{ctNames.Length}）：{string.Join("、", ctNames)}"
        ];
    }

    private static string DuelLifecycleLabel(DuelLifecycle lifecycle)
    {
        return lifecycle switch
        {
            DuelLifecycle.Running => "进行中",
            DuelLifecycle.Paused => "已暂停",
            DuelLifecycle.Finished => "已结束",
            _ => "未开始"
        };
    }

    private void ApplyDuelAdminConfig(CCSPlayerController? player, CommandInfo command, DuelGameConfig config)
    {
        if (!_duelSession.TryUpdateConfig(config, out var error))
        {
            ReplyToDuelCaller(player, command, $"[草人杯] 配置未修改：{error}");
            return;
        }

        ReplyToDuelCaller(player, command, $"[草人杯] 配置已更新：{FormatDuelConfig(_duelSession.Config)}");
    }

    private void PauseGameManagedDuel(CCSPlayerController? player, CommandInfo command)
    {
        if (_duelSession.ControlMode != DuelControlMode.GameManaged || _duelSession.Lifecycle != DuelLifecycle.Running)
        {
            ReplyToDuelCaller(player, command, "[草人杯] 当前没有正在进行的游戏内单挑可暂停。");
            return;
        }

        PauseGameManagedDuel("管理员手动暂停");
        Logger.LogInformation("Game-managed duel paused by an administrator.");
        ReplyToDuelCaller(player, command, "[草人杯] 游戏内单挑状态已暂停。");
    }

    private void PauseGameManagedDuel(string reason)
    {
        _duelSession.Pause(reason);
        Server.ExecuteCommand("mp_pause_match");
        PrintToAllPlayers($"游戏内单挑已暂停：{reason}。");
    }

    private void ResumeGameManagedDuel(CCSPlayerController? player, CommandInfo command)
    {
        EvaluateGameManagedConnectivity();
        if (!_duelSession.TryResume(out var error))
        {
            ReplyToDuelCaller(player, command, $"[草人杯] 无法恢复单挑：{error}");
            return;
        }

        Server.ExecuteCommand("mp_unpause_match");
        Logger.LogInformation("Game-managed duel resumed by an administrator.");
        ReplyToDuelCaller(player, command, "[草人杯] 游戏内单挑状态已恢复。");
    }

    private void EvaluateGameManagedConnectivity()
    {
        if (_duelSession.ControlMode != DuelControlMode.GameManaged) return;
        _duelSession.UpdateConnectedPlayers(GetConnectedRealPlayerSteamIds());
    }

    private static HashSet<string> GetConnectedRealPlayerSteamIds() => Utilities.GetPlayers()
        .Where(IsRealPlayer)
        .Select(candidate => candidate.SteamID.ToString())
        .ToHashSet(StringComparer.Ordinal);

    private void StopGameManagedDuel(CCSPlayerController? player, CommandInfo command)
    {
        if (_duelSession.ControlMode != DuelControlMode.GameManaged)
        {
            ReplyToDuelCaller(player, command, "[草人杯] 当前没有游戏内单挑可终止。");
            return;
        }

        Logger.LogInformation("Game-managed duel state was stopped by an administrator.");
        if (player == null)
        {
            ReplyToDuelCaller(player, command, "[草人杯] 游戏内单挑已强制终止，本次不计算胜负。");
        }
        AbortGameManagedDuel("管理员强制终止，本次不计算胜负");
    }

    private void SwitchDuelMap(CCSPlayerController? player, CommandInfo command, string input)
    {
        if (!_duelCleanupState.RestartPending &&
            _duelCvarRestorePending)
        {
            RetryPendingGameManagedDuelCvarsAtSafePoint();
        }

        var hasPendingCvarRestore = _duelCvarRestorePending;
        if (IsDuelMapChangeBlocked(
            _duelSession.ControlMode,
            _duelSession.Lifecycle,
            hasPendingCvarRestore,
            _duelCleanupState.RestartPending))
        {
            var message = _duelCleanupState.RestartPending
                ? "[草人杯] 上一局清理重启尚未完成，暂时不能切换地图，请稍后重试。"
                : hasPendingCvarRestore
                    ? "[草人杯] 上一次比赛的服务器参数仍在恢复中，暂时不能切换地图，请稍后重试。"
                    : "[草人杯] 游戏内单挑期间不能切换地图，请先终止本局。";
            ReplyToDuelCaller(player, command, message);
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            ShowDuelMapHelp(player, command);
            return;
        }

        var map = ResolveDuelWorkshopMap(input);
        if (map == null)
        {
            ReplyToDuelCaller(player, command, $"[草人杯] 没找到单挑地图：{input}");
            ShowDuelMapHelp(player, command);
            return;
        }

        Server.ExecuteCommand($"host_workshop_map {map.WorkshopId}");
        Logger.LogInformation("Switching directly to duel workshop map {Map} ({WorkshopId}).", map.Name, map.WorkshopId);
        ReplyToDuelCaller(player, command, $"[草人杯] 正在切换单挑地图：{map.Name} ({map.WorkshopId})。");
    }

    private static bool IsDuelStartBlocked(
        bool cleanupRestartPending,
        bool cvarRestoreReady) =>
        cleanupRestartPending || !cvarRestoreReady;

    private static bool ShouldRetryPendingDuelCvars(
        bool hasActiveRuntime,
        bool restorePending) =>
        !hasActiveRuntime && restorePending;

    private static bool IsDuelMapChangeBlocked(
        DuelControlMode controlMode,
        DuelLifecycle lifecycle,
        bool hasPendingCvarRestore,
        bool cleanupRestartPending) =>
        cleanupRestartPending ||
        hasPendingCvarRestore ||
        (controlMode == DuelControlMode.GameManaged &&
            lifecycle is DuelLifecycle.Running or DuelLifecycle.Paused);

    private void ReplyToDuelCaller(CCSPlayerController? player, CommandInfo command, string message)
    {
        if (player == null)
        {
            command.ReplyToCommand($"[草人杯] {StripChatTag(message)}");
            return;
        }

        ReplyToPlayer(player, message);
    }

    private static string FormatDuelConfig(DuelGameConfig config)
    {
        var utility = config.UtilityMode switch
        {
            "none" => "无道具",
            "random1" => "随机 1 个道具",
            "random2" => "随机 2 个道具",
            "random3" => "随机 3 个道具",
            "full" => "全道具",
            _ => config.UtilityMode
        };
        return $"配置：手枪 {config.PistolRounds} 回合，步枪 {config.RifleRounds} 回合，狙击枪 {config.SniperRounds} 回合，每回合 {config.RoundTimeMinutes:0.##} 分钟，道具 {utility}。";
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_duelCleanupState.TryConsumeRestartRound(out var cleanupMode))
        {
            CompleteDuelCleanupAfterRestart(cleanupMode);
            return HookResult.Continue;
        }

        if (_duelSession.ControlMode == DuelControlMode.GameManaged &&
            _duelSession.Lifecycle == DuelLifecycle.Running)
        {
            _duelSession.MarkRoundStarted();
        }
        if (_duelModeEnabled) _duelFormalRound = Math.Max(1, _duelSession.ScoreCt + _duelSession.ScoreT + 1);
        StartDuelRoundProtection();
        EnforceTeamAssignments();
        ApplyDuelLoadoutsForRound();
        AnnounceDuelRound();
        NotifyDuelBridgeRoundStart();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (_duelSession.ControlMode != DuelControlMode.GameManaged) return HookResult.Continue;

        var winner = TeamName(@event.Winner);
        if (winner is not ("T" or "CT")) return HookResult.Continue;

        var duelResult = _duelSession.RecordRoundEnd(
            winner == "T" ? DuelTeam.Terrorist : DuelTeam.CounterTerrorist);
        if (duelResult.Finished)
        {
            FinishGameManagedDuel(duelResult);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerTakeDamagePre(CCSPlayerPawn victimPawn, CTakeDamageInfo damageInfo)
    {
        if (!_duelModeEnabled || !IsDuelRoundProtectionActive()) return HookResult.Continue;
        if (victimPawn == null || !victimPawn.IsValid) return HookResult.Continue;

        var victim = victimPawn.Controller.Value as CCSPlayerController;
        var attackerPawn = damageInfo.Attacker.Value?.As<CCSPlayerPawn>();
        var attacker = attackerPawn?.Controller.Value as CCSPlayerController;
        if (!IsRealPlayer(victim) || !IsRealPlayer(attacker) || victim == attacker) return HookResult.Continue;
        if (!IsEnemySideKill(TeamName(attacker!.TeamNum), TeamName(victim!.TeamNum))) return HookResult.Continue;

        damageInfo.Damage = 0;
        ShowDuelProtectionHitNotice(attacker);
        return HookResult.Continue;
    }

    private void FinishGameManagedDuel(DuelRoundResult result)
    {
        var outcome = result.ScoreT > result.ScoreCt
            ? "T 获胜"
            : result.ScoreCt > result.ScoreT
                ? "CT 获胜"
                : "双方战平";
        try
        {
            PrintToAllPlayers($"独立单挑结束：T {result.ScoreT} : {result.ScoreCt} CT，{outcome}。");
            Logger.LogInformation(
                "Game-managed duel finished normally. ScoreT={ScoreT} ScoreCt={ScoreCt}",
                result.ScoreT,
                result.ScoreCt);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to broadcast the game-managed duel result.");
        }
        finally
        {
            BeginDuelCleanup(DuelControlMode.GameManaged, waitForEngineRestart: true);
        }
    }

    private void AbortGameManagedDuel(string reason, bool immediateRestore = false)
    {
        try
        {
            PrintToAllPlayers($"独立单挑已终止：{reason}。");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to broadcast game-managed duel termination reason: {Reason}", reason);
        }
        finally
        {
            if (immediateRestore) CleanupDuelImmediately(DuelControlMode.GameManaged);
            else BeginDuelCleanup(DuelControlMode.GameManaged);
        }
    }

    private void BeginDuelCleanup(DuelControlMode mode, bool waitForEngineRestart = false)
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
            NotifyDuelBridgeCleanupRestart();
            TryUnpauseDuelDuringCleanup();
        }

        if (waitForEngineRestart) return;

        try
        {
            Server.ExecuteCommand("mp_restartgame 1");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to restart the game during duel cleanup; restoring immediately.");
            CleanupDuelImmediately(mode);
        }
    }

    private void CompleteDuelCleanupAfterRestart(DuelControlMode mode)
    {
        ClearDuelEquipmentState();
        _duelSession.Clear();
        RestoreGameManagedDuelCvarsWithRetry();

        if (mode == DuelControlMode.GameManaged)
        {
            NotifyDuelBridgeRoundStart();
        }

        _duelCleanupState.Reset();
    }

    private void CleanupDuelImmediately(DuelControlMode requestedMode = DuelControlMode.None)
    {
        var mode = requestedMode != DuelControlMode.None
            ? requestedMode
            : _duelSession.ControlMode != DuelControlMode.None
                ? _duelSession.ControlMode
                : _duelCleanupState.Mode;
        var hasRuntime = _duelModeEnabled ||
            _gameManagedDuelRuntimeActive ||
            mode != DuelControlMode.None ||
            _duelServerCvars.PendingRestoreCount > 0;
        if (!hasRuntime) return;

        _gameManagedDuelRuntimeActive = false;
        _duelModeEnabled = false;
        _teamLockEnabled = false;
        _teamAssignments.Clear();
        _teamAssignmentBypass.Clear();
        TryUnpauseDuelDuringCleanup();
        ClearDuelEquipmentState();
        _duelSession.Clear();

        if (mode == DuelControlMode.GameManaged)
        {
            NotifyDuelBridgeCleanupRestart();
        }

        RestoreGameManagedDuelCvarsWithRetry();
        if (mode == DuelControlMode.GameManaged)
        {
            NotifyDuelBridgeRoundStart();
        }
        _duelCleanupState.Reset();
    }

    private void TryUnpauseDuelDuringCleanup()
    {
        if (_duelSession.Lifecycle != DuelLifecycle.Paused) return;
        try
        {
            Server.ExecuteCommand("mp_unpause_match");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to unpause game-managed duel during cleanup.");
        }
    }

    private void RestoreGameManagedDuelCvarsWithRetry()
    {
        _duelCvarRestorePending = _duelServerCvars.PendingRestoreCount > 0;
        NotifyDuelBridgeCvarRestoreReady(!_duelCvarRestorePending);
        if (!_duelCvarRestorePending)
        {
            return;
        }

        var failedAttempt = 0;
        var restored = _duelServerCvars.TryRestoreAll(3, failure =>
        {
            failedAttempt++;
            Logger.LogWarning(
                failure,
                "Game-managed duel cvar restore attempt {Attempt} failed; pending entries will be retried.",
                failedAttempt);
        });
        if (!restored)
        {
            Logger.LogError(
                "Game-managed duel cvars remain unresolved after bounded retries; " +
                "the periodic safety check and heartbeat safe point will keep retrying: {PendingCvars}",
                string.Join(",", _duelServerCvars.PendingRestoreNames));
        }

        _duelCvarRestorePending = !restored;
        NotifyDuelBridgeCvarRestoreReady(restored);
    }

    private void RetryPendingGameManagedDuelCvarsAtSafePoint()
    {
        var hasActiveRuntime = _gameManagedDuelRuntimeActive ||
            _duelSession.ControlMode == DuelControlMode.GameManaged;
        if (!ShouldRetryPendingDuelCvars(hasActiveRuntime, _duelCvarRestorePending)) return;

        var restored = _duelServerCvars.RetryPendingAtSafePoint(failure =>
        {
            Logger.LogWarning(
                failure,
                "Deferred game-managed duel cvar restore failed; pending entries remain queued.");
        });
        if (restored)
        {
            Logger.LogInformation("All deferred game-managed duel cvars were restored.");
        }

        _duelCvarRestorePending = !restored;
        NotifyDuelBridgeCvarRestoreReady(restored);
    }

    private void ClearDuelEquipmentState()
    {
        _duelLoadoutGeneration++;
        _duelPendingPrimary.Clear();
        _duelPendingSecondary.Clear();
        _duelCurrentPrimary.Clear();
        _duelCurrentSecondary.Clear();
        _duelAwpRequests.Clear();
        _duelRoundProtectionEndsAt = 0;
        _duelProtectionNoticeAt.Clear();
    }

    private void ActivateDuelRuntime(DuelGameConfig config)
    {
        if (!DuelRuntimePolicy.CanActivateRuntime(
                _duelServerCvars.IsReadyForNewDuel,
                _duelModeEnabled))
        {
            throw new InvalidOperationException("Previous duel CVar restore is incomplete.");
        }

        _duelPistolRounds = config.PistolRounds;
        _duelRifleRounds = config.RifleRounds;
        _duelSniperRounds = config.SniperRounds;
        _duelUtilityMode = config.UtilityMode;
        _duelFormalRound = 0;
        _duelLastAnnouncedStage = null;
        ClearDuelEquipmentState();
        var cvarPlan = DuelRuntimePolicy.BuildCvarPlan(config);
        _duelServerCvars.Apply(cvarPlan);
        _duelModeEnabled = true;
        Server.ExecuteCommand("mp_warmup_end");
        Server.ExecuteCommand("mp_restartgame 1");
    }

    private void EnforceTeamAssignments()
    {
        if (!_teamLockEnabled || _teamAssignments.Count == 0) return;
        if (_duelSession.ControlMode != DuelControlMode.GameManaged) return;

        foreach (var player in Utilities.GetPlayers().Where(IsRealPlayer))
        {
            var steamId = player.SteamID.ToString();
            var isParticipant = _teamAssignments.TryGetValue(steamId, out var targetTeam);
            if (!isParticipant)
            {
                if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;
                targetTeam = CsTeam.Spectator;
            }

            if (player.Team == targetTeam || player.TeamNum == (int)targetTeam) continue;

            try
            {
                _teamAssignmentBypass.Add(steamId);
                player.ChangeTeam(targetTeam);
                var message = isParticipant
                    ? $"已恢复到本局固定队伍 {TeamLabel(targetTeam)}。"
                    : "本局参赛名单已经锁定，你只能在观察者席观看。";
                player.PrintToChat(FormatPrivateChatMessage(message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to enforce team assignment for {SteamId} to {Team}", steamId, targetTeam);
            }
            finally
            {
                _teamAssignmentBypass.Remove(steamId);
            }
        }
    }

    private HookResult OnJoinTeamCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_teamLockEnabled || !IsRealPlayer(player)) return HookResult.Continue;
        if (_duelSession.ControlMode != DuelControlMode.GameManaged) return HookResult.Continue;

        var steamId = player!.SteamID.ToString();
        if (_teamAssignmentBypass.Contains(steamId)) return HookResult.Continue;

        var requestedTeam = command.ArgByIndex(1)?.Trim();
        if (_teamAssignments.TryGetValue(steamId, out var gameManagedTeam))
        {
            var requestedOriginalTeam = requestedTeam == ((int)gameManagedTeam).ToString();
            if (requestedOriginalTeam) return HookResult.Continue;

            player.PrintToChat(FormatPrivateChatMessage(
                $"本局参赛队伍已锁定，不能自行换队。你的固定队伍是 {TeamLabel(gameManagedTeam)}。"));
            Server.NextFrame(EnforceTeamAssignments);
            return HookResult.Handled;
        }

        if (requestedTeam is not ("0" or "2" or "3")) return HookResult.Continue;
        player.PrintToChat(FormatPrivateChatMessage("本局参赛名单已经锁定，非参赛者只能在观察者席观看。"));
        return HookResult.Handled;
    }

    private HookResult OnDuelDropCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_duelModeEnabled || !IsRealPlayer(player)) return HookResult.Continue;
        if (!DuelRuntimePolicy.ShouldBlockDrop(PlayerEquipment(player).ActiveWeapon))
        {
            return HookResult.Continue;
        }

        player!.PrintToChat(FormatPrivateChatMessage("单挑期间不能丢弃枪械。"));
        return HookResult.Handled;
    }

    private DuelStage GetDuelStage()
    {
        var round = Math.Max(1, _duelFormalRound > 0 ? _duelFormalRound : _duelSession.ScoreCt + _duelSession.ScoreT + 1);
        if (round <= _duelPistolRounds) return DuelStage.Pistol;
        if (round <= _duelPistolRounds + _duelRifleRounds) return DuelStage.Rifle;
        return DuelStage.Sniper;
    }

    private int GetDuelStageRound(DuelStage stage)
    {
        var round = Math.Max(1, _duelFormalRound > 0 ? _duelFormalRound : _duelSession.ScoreCt + _duelSession.ScoreT + 1);
        return stage switch
        {
            DuelStage.Pistol => Math.Min(round, Math.Max(1, _duelPistolRounds)),
            DuelStage.Rifle => Math.Min(Math.Max(1, round - _duelPistolRounds), Math.Max(1, _duelRifleRounds)),
            DuelStage.Sniper => Math.Min(Math.Max(1, round - _duelPistolRounds - _duelRifleRounds), Math.Max(1, _duelSniperRounds)),
            _ => round
        };
    }

    private int GetDuelStageTotal(DuelStage stage)
    {
        return stage switch
        {
            DuelStage.Pistol => Math.Max(1, _duelPistolRounds),
            DuelStage.Rifle => Math.Max(1, _duelRifleRounds),
            DuelStage.Sniper => Math.Max(1, _duelSniperRounds),
            _ => 1
        };
    }

    private static string DuelStageLabel(DuelStage stage)
    {
        return stage switch
        {
            DuelStage.Pistol => "手枪",
            DuelStage.Rifle => "步枪",
            DuelStage.Sniper => "狙击枪",
            _ => "单挑"
        };
    }

    private void AnnounceDuelRound()
    {
        if (!_duelModeEnabled) return;
        var stage = GetDuelStage();
        var stageRound = GetDuelStageRound(stage);
        var stageTotal = GetDuelStageTotal(stage);
        Server.NextFrame(() =>
        {
            Server.ExecuteCommand($"css_csay {DuelStageLabel(stage)}第{stageRound}/{stageTotal}回合");
            if (_duelFormalRound == 1 || (_duelFormalRound > 1 && (_duelFormalRound - 1) % 8 == 0) || _duelLastAnnouncedStage != stage)
            {
                PrintToAllPlayers($"当前是{DuelStageLabel(stage)}阶段，请用 /guns 查看可选枪械。");
            }
            _duelLastAnnouncedStage = stage;
        });
    }

    private void StartDuelRoundProtection()
    {
        if (!_duelModeEnabled)
        {
            _duelRoundProtectionEndsAt = 0;
            _duelProtectionNoticeAt.Clear();
            return;
        }

        _duelRoundProtectionEndsAt = Server.CurrentTime + 0.6f;
        _duelProtectionNoticeAt.Clear();
    }

    private bool IsDuelRoundProtectionActive()
    {
        return _duelModeEnabled && _duelRoundProtectionEndsAt > 0 && Server.CurrentTime < _duelRoundProtectionEndsAt;
    }

    private void ShowDuelProtectionHitNotice(CCSPlayerController attacker)
    {
        var steamId = attacker.SteamID.ToString();
        var now = Server.CurrentTime;
        if (_duelProtectionNoticeAt.TryGetValue(steamId, out var lastAt) && now - lastAt < 0.35f) return;
        _duelProtectionNoticeAt[steamId] = now;

        var message = "开局保护中，本次命中不造成伤害";
        try { attacker.PrintToCenter($"[草人杯] {message}"); } catch { }
        try { attacker.PrintToChat(FormatPrivateChatMessage(message)); } catch { }
    }

    private void RequestDuelWeapon(CCSPlayerController player, string alias, string weapon)
    {
        var stage = GetDuelStage();
        var steamId = player.SteamID.ToString();
        if (stage == DuelStage.Pistol)
        {
            if (!DuelPistolAliases.Contains(alias))
            {
                ReplyToPlayer(player, "[草人杯] 当前是手枪阶段，请用 /guns 查看可选枪械。");
                return;
            }
            _duelPendingSecondary[steamId] = weapon;
            ReplyToPlayer(player, $"[草人杯] 已选择 {alias}，下回合生效。");
            return;
        }

        if (stage == DuelStage.Sniper)
        {
            if (alias != "ssg" && alias != "awp")
            {
                ReplyToPlayer(player, "[草人杯] 当前是狙击阶段，只能选择 /ssg 或 /awp。");
                return;
            }
            _duelPendingPrimary[steamId] = weapon;
            ReplyToPlayer(player, $"[草人杯] 已选择 {alias}，下回合生效。");
            return;
        }

        if (!DuelRifleAliases.Contains(alias))
        {
            if (DuelPistolAliases.Contains(alias))
            {
                _duelPendingSecondary[steamId] = weapon;
                ReplyToPlayer(player, $"[草人杯] 已选择副武器 {alias}，下回合生效。");
                return;
            }
            ReplyToPlayer(player, "[草人杯] 当前是步枪阶段，请用 /guns 查看可选枪械。");
            return;
        }
        if (alias == "awp")
        {
            RequestDuelAwp(player);
            return;
        }

        _duelPendingPrimary[steamId] = weapon;
        ReplyToPlayer(player, $"[草人杯] 已选择 {alias}，下回合生效。");
    }

    private void RequestDuelAwp(CCSPlayerController player)
    {
        var steamId = player.SteamID.ToString();
        var opponents = Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .Where(p => p.Team != player.Team && (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist))
            .Select(p => p.SteamID.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (opponents.Count == 0)
        {
            ReplyToPlayer(player, "[草人杯] 当前没有对方参赛玩家，不能申请 AWP。");
            return;
        }

        _duelAwpRequests[steamId] = new PendingAwpRequest
        {
            RequesterSteamId = steamId,
            RequesterTeam = player.Team,
            RequiredApprovals = opponents,
        };
        PrintToAllPlayers($"{SafePlayerName(player)} 申请下回合使用 AWP，对方所有玩家输入 /agree_awp 同意。");
    }

    private void ApplyDuelLoadoutsForRound()
    {
        if (!_duelModeEnabled) return;
        var stage = GetDuelStage();
        var inputs = Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .Where(player => player.Team == CsTeam.Terrorist || player.Team == CsTeam.CounterTerrorist)
            .Select(player =>
            {
                var steamId = player.SteamID.ToString();
                var primary = stage switch
                {
                    DuelStage.Pistol => string.Empty,
                    DuelStage.Rifle => GetPendingOrCurrent(_duelPendingPrimary, _duelCurrentPrimary, steamId, "weapon_ak47"),
                    DuelStage.Sniper => GetPendingOrCurrentSniper(steamId),
                    _ => string.Empty
                };
                var secondary = stage switch
                {
                    DuelStage.Sniper => string.Empty,
                    _ => GetPendingOrCurrent(_duelPendingSecondary, _duelCurrentSecondary, steamId, "weapon_usp_silencer")
                };
                return new DuelPlayerLoadoutInput(steamId, primary, secondary);
            })
            .ToList();
        var plans = DuelRuntimePolicy.BuildSteamBoundLoadoutPlans(stage, inputs);
        var generation = ++_duelLoadoutGeneration;

        AddTimer(0.2f, () =>
        {
            if (_isUnloading || !_duelModeEnabled || generation != _duelLoadoutGeneration) return;
            foreach (var plan in plans.Values)
            {
                ApplyDuelLoadout(plan, stage, generation);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ApplyDuelLoadout(DuelSteamBoundLoadoutPlan plan, DuelStage stage, int generation)
    {
        var player = FindDuelPlayer(plan.SteamId);
        if (player == null || !player.PawnIsAlive) return;

        try
        {
            player.RemoveWeapons();
            player.GiveNamedItem("weapon_knife");
            if (!string.IsNullOrWhiteSpace(plan.Primary)) player.GiveNamedItem(plan.Primary);
            if (!string.IsNullOrWhiteSpace(plan.Secondary)) player.GiveNamedItem(plan.Secondary);
            if (stage == DuelStage.Pistol) GivePlayerKevlar(player);
            else player.GiveNamedItem("item_assaultsuit");
            GiveDuelUtilities(player);
            QueuePreferredDuelWeapon(player, plan.Rule);
            AddTimer(0.05f, () => LogAppliedDuelLoadout(plan, generation), TimerFlags.STOP_ON_MAPCHANGE);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply duel loadout for {SteamId}", plan.SteamId);
        }
    }

    private CCSPlayerController? FindDuelPlayer(string steamId) => Utilities.GetPlayers()
        .Where(IsRealPlayer)
        .FirstOrDefault(player =>
            player.SteamID.ToString() == steamId
            && (player.Team == CsTeam.Terrorist || player.Team == CsTeam.CounterTerrorist));

    private void LogAppliedDuelLoadout(DuelSteamBoundLoadoutPlan plan, int generation)
    {
        if (_isUnloading || !_duelModeEnabled || generation != _duelLoadoutGeneration) return;
        var player = FindDuelPlayer(plan.SteamId);
        if (player == null) return;
        Logger.LogDebug(
            "Duel loadout bound by SteamID {SteamId}: expected primary={Primary} secondary={Secondary}; actual={Actual}",
            plan.SteamId,
            plan.Primary,
            plan.Secondary,
            string.Join(",", PlayerEquipment(player).Weapons));
    }

    private void QueuePreferredDuelWeapon(CCSPlayerController player, DuelLoadoutRule rule)
    {
        var steamId = player.SteamID;
        Server.NextFrame(() =>
        {
            if (!CanApplyDuelLoadoutContinuation(player, steamId)) return;
            TrySelectPreferredDuelWeapon(player, rule, allowRetry: true);
        });
    }

    private void TrySelectPreferredDuelWeapon(
        CCSPlayerController player,
        DuelLoadoutRule rule,
        bool allowRetry)
    {
        if (string.Equals(
                PlayerEquipment(player).ActiveWeapon,
                rule.PreferredWeapon,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var slotCommand = rule.PreferredSlot == DuelPreferredWeaponSlot.Primary ? "slot1" : "slot2";
        player.ExecuteClientCommand(slotCommand);
        if (!allowRetry) return;

        var steamId = player.SteamID;
        AddTimer(0.1f, () =>
        {
            if (!CanApplyDuelLoadoutContinuation(player, steamId)) return;
            TrySelectPreferredDuelWeapon(player, rule, allowRetry: false);
            if (!string.Equals(
                    PlayerEquipment(player).ActiveWeapon,
                    rule.PreferredWeapon,
                    StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogDebug(
                    "Duel preferred weapon selection did not settle for {SteamId}: {Weapon}",
                    steamId,
                    rule.PreferredWeapon);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private bool CanApplyDuelLoadoutContinuation(CCSPlayerController player, ulong steamId)
    {
        return !_isUnloading
            && _duelModeEnabled
            && IsRealPlayer(player)
            && player.SteamID == steamId;
    }

    private static string GetPendingOrCurrent(
        Dictionary<string, string> pending,
        Dictionary<string, string> current,
        string steamId,
        string fallback)
    {
        if (pending.TryGetValue(steamId, out var pendingWeapon))
        {
            current[steamId] = pendingWeapon;
            pending.Remove(steamId);
            return pendingWeapon;
        }
        if (current.TryGetValue(steamId, out var currentWeapon)) return currentWeapon;
        current[steamId] = fallback;
        return fallback;
    }

    private string GetPendingOrCurrentSniper(string steamId)
    {
        if (_duelPendingPrimary.TryGetValue(steamId, out var pendingWeapon))
        {
            _duelPendingPrimary.Remove(steamId);
            if (IsDuelSniperWeapon(pendingWeapon))
            {
                _duelCurrentPrimary[steamId] = pendingWeapon;
                return pendingWeapon;
            }
        }
        if (_duelCurrentPrimary.TryGetValue(steamId, out var currentWeapon) && IsDuelSniperWeapon(currentWeapon)) return currentWeapon;
        _duelCurrentPrimary[steamId] = "weapon_awp";
        return "weapon_awp";
    }

    private void GiveDuelUtilities(CCSPlayerController player)
    {
        var utility = BuildDuelUtilities(player);
        foreach (var item in utility)
        {
            try { player.GiveNamedItem(item); }
            catch (Exception ex) { Logger.LogDebug(ex, "Failed to give duel utility {Utility}", item); }
        }
    }

    private List<string> BuildDuelUtilities(CCSPlayerController player)
    {
        var pool = new List<string> { "weapon_hegrenade", "weapon_smokegrenade", "weapon_incgrenade", "weapon_flashbang" };
        var count = _duelUtilityMode switch
        {
            "random1" => 1,
            "random2" => 2,
            "random3" => 3,
            "full" => 4,
            _ => 0
        };
        if (count <= 0) return new List<string>();
        if (_duelUtilityMode == "full") return pool;
        var result = new List<string>();
        while (result.Count < count && pool.Count > 0)
        {
            var index = _duelRandom.Next(pool.Count);
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return result;
    }

    private static void GivePlayerKevlar(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        pawn.ArmorValue = 100;
        var itemServices = pawn.ItemServices?.As<CCSPlayer_ItemServices>();
        if (itemServices is not null)
        {
            itemServices.HasHelmet = false;
        }
        player.PawnHasHelmet = false;
        try
        {
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
        }
        catch
        {
            try
            {
                Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_ArmorValue");
            }
            catch { }
        }
        try
        {
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_bPawnHasHelmet");
        }
        catch { }
    }

    private static bool IsDuelSniperWeapon(string weapon)
    {
        return string.Equals(weapon, "weapon_awp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(weapon, "weapon_ssg08", StringComparison.OrdinalIgnoreCase);
    }

    private void OnMapStart(string _mapName)
    {
        var activeDuelMode = _duelSession.ControlMode;
        if (_isUnloading) return;
        MapStartLifecycleSchedule(() =>
        {
            if (_isUnloading) return;
            if (activeDuelMode == DuelControlMode.GameManaged &&
                _duelSession.ControlMode == DuelControlMode.GameManaged)
            {
                Logger.LogWarning("Aborting game-managed duel because the map changed during the match.");
                AbortGameManagedDuel("比赛中发生换图，本次不计算胜负", immediateRestore: true);
            }
            StartDuelSafetyTimer();
        });
    }

    private void ShowDuelMapHelp(CCSPlayerController? player, CommandInfo command)
    {
        ReplyToDuelCaller(player, command, "[草人杯] 用法：/duel map <序号|地图名|创意工坊ID>（兼容 /duel_map）");
        ReplyToDuelCaller(player, command, "[草人杯] 示例：/duel map 1 或 /duel map AIM Map 或 /duel map 3084291314");
        for (var i = 0; i < DuelWorkshopMaps.Length; i++)
        {
            var map = DuelWorkshopMaps[i];
            ReplyToDuelCaller(player, command, $"[草人杯] {i + 1}. {map.Name} ({map.WorkshopId})");
        }
    }

    private static DuelWorkshopMap? ResolveDuelWorkshopMap(string input)
    {
        var value = input.Trim();
        if (int.TryParse(value, out var index) && index >= 1 && index <= DuelWorkshopMaps.Length)
        {
            return DuelWorkshopMaps[index - 1];
        }

        return DuelWorkshopMaps.FirstOrDefault(map =>
            string.Equals(map.WorkshopId, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(map.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SlugifyDuelMapName(map.Name), SlugifyDuelMapName(value), StringComparison.OrdinalIgnoreCase));
    }

    private static string SlugifyDuelMapName(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private void ReplyToPlayer(CCSPlayerController? player, string message)
    {
        if (player == null) return;
        Server.NextFrame(() =>
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return;
            try
            {
                if (player.IsValid) player.PrintToChat(FormatPrivateChatMessage(message));
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to print chat message to player");
            }
        });
    }
}

internal sealed record DuelWorkshopMap(string Name, string WorkshopId);

internal sealed class PendingAwpRequest
{
    public string RequesterSteamId { get; set; } = string.Empty;
    public CsTeam RequesterTeam { get; set; }
    public List<string> RequiredApprovals { get; set; } = new();
    public HashSet<string> Approvals { get; set; } = new(StringComparer.Ordinal);
}
