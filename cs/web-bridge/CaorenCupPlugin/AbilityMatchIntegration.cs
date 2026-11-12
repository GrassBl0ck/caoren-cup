using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using CaorenCup.Contracts;
using CaorenCupPlugin.MatchRuntime;
using CaorenCupPlugin.Practice;
namespace CaorenCupPlugin;
public sealed partial class CaorenCupPlugin
{
    internal static IReadOnlyList<string> MatchCompatibilityCommandNames { get; } =
    [
        "css_ready", "css_r", "css_unready", "css_ur", "css_notready", "css_start",
        "css_pause", "css_tech", "css_tac", "css_unpause", "css_forcepause", "css_fp",
        "css_forceunpause", "css_fup", "css_stop", "css_restore", "css_ccmatch",
        "css_prac", "css_exitprac", "css_rethrow", "css_last", "css_clear", "css_bot",
        "css_cbot", "css_nobots", "css_savenade", "css_loadnade", "css_deletenade",
        "css_ctspawn", "css_tspawn", "css_god", "css_impacts", "css_traj",
    ];
    private MatchRuntimeBridgeAdapter? _matchRuntimeBridge;
    private PracticeRuntime? _practiceRuntime;
    private PracticeLineupStore? _practiceLineups;
    private readonly Dictionary<int, BridgeStatsCheckpoint> _bridgeStatsCheckpoints = [];
    private readonly Dictionary<string, string> _pendingAbilitySyncDigests = new(StringComparer.Ordinal);
    private AbilityRuntimeProtocol.AbilityRuntimeEnvelope? _confirmedAbilityRuntimeIdentity;
    private bool _abilityModeRequired;
    private bool _abilitySyncConfirmed;
    private bool _practiceGodEnabled;
    private bool _practiceImpactsEnabled = true;
    private bool _practiceTrajectoryEnabled = true;
    private string? _lastAbilityRuntimeStartKey;
    private string _currentMatchController = "matchzy";
    private string? _currentWebPhase;
    private bool _webMatchRuntimeEnabled;
    private readonly object _matchAuthorityOwner = new();
    private string _runtimeModeId = "competitive";
    private bool RuntimeOwnsMatch => MatchRuntimeOwnership.IsRequested(_config.CaorenMatchRuntimeEnabled,_webMatchRuntimeEnabled,_runtimeModeId,_currentMatchController);

    private void ReleaseAuthorityIfIdle() => MatchRuntimeOwnership.TryReleaseIfIdle(
        _matchAuthorityOwner, _matchRuntimeBridge?.RequiresAuthority == true, _practiceRuntime?.RequiresAuthority == true);

    internal static JsonSerializerOptions CreatePluginJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private void RegisterMatchCompatibilityCommands()
    {
        foreach (var commandName in MatchCompatibilityCommandNames)
            AddCommand(commandName, "草人杯比赛/练习兼容命令", OnMatchCompatibilityCommand);
    }

    private void OnMatchCompatibilityCommand(CCSPlayerController? player, CommandInfo command)
    {
        var commandName = (command.GetArg(0) ?? string.Empty).Trim();
        if (commandName.StartsWith("css_", StringComparison.OrdinalIgnoreCase)) commandName = commandName[4..];
        var arguments = Enumerable.Range(1, Math.Max(0, command.ArgCount - 1))
            .Select(command.ArgByIndex)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        var text = "." + commandName + (command.ArgCount > 1 ? " " + string.Join(' ', arguments) : string.Empty);

        var practice = PracticeCommandParser.Parse(text);
        if (practice.Ok && practice.Command is not null)
        {
            HandlePracticeCommand(player, command, practice.Command);
            return;
        }

        var match = MatchCommandParser.Parse(text);
        if (!match.Ok || match.Command is null)
        {
            ReplyToPlayer(player, $"[草人杯] 命令格式无效：{match.Code}");
            return;
        }
        HandleMatchCommand(player, match.Command);
    }

    private void HandlePracticeCommand(
        CCSPlayerController? player,
        CommandInfo command,
        ParsedPracticeCommand parsed)
    {
        if (parsed.Kind != PracticeCommandKind.Stop && (!_config.CaorenMatchRuntimeEnabled || MatchRuntimeOwnership.IsMatchZyLoaded)) { ReplyToPlayer(player,"[草人杯] 自有练习模式未启用，或 MatchZy 仍加载；请先在独立实例停用 MatchZy 并完整重启。");return; }
        if (_practiceRuntime is null)
        {
            ReplyToPlayer(player, "[草人杯] 练习运行时尚未初始化。");
            return;
        }
        var isAdmin = player is null || AdminManager.PlayerHasPermissions(player, "@css/root");
        if (parsed.Kind is PracticeCommandKind.Start or PracticeCommandKind.Stop or PracticeCommandKind.Clear or PracticeCommandKind.AddBot or PracticeCommandKind.AddCrouchedBot or PracticeCommandKind.RemoveBots or PracticeCommandKind.God or PracticeCommandKind.Impacts or PracticeCommandKind.Trajectory)
        {
            if (!isAdmin) { ReplyToPlayer(player,"[草人杯] 该全服练习操作仅管理员可用。");return; }
        }
        MatchCommandResult result;
        switch (parsed.Kind)
        {
            case PracticeCommandKind.Start:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以开启练习模式。"); return; }
                if (CaorenCupCoreAccess.TryGet()?.GameModes.State.Lifecycle is GameModeLifecycleState.Live or GameModeLifecycleState.Warmup
                    || CaorenCupPracticeState.IsPracticeActive || !CaorenCupMatchAuthority.TryAcquire(_matchAuthorityOwner)) { ReplyToPlayer(player,"[草人杯] 其他模式正在控制比赛，不能开启练习。"); return; }
                result = _practiceRuntime.Start(
                    SafeMapName(),
                    _matchRuntimeBridge?.RequiresAuthority == true);
                break;
            case PracticeCommandKind.Stop:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以退出练习模式。"); return; }
                result = _practiceRuntime.Stop();
                break;
            case PracticeCommandKind.Rethrow:
                result = _practiceRuntime.RethrowLastGrenade();
                break;
            case PracticeCommandKind.Clear:
                result = _practiceRuntime.ClearGrenades();
                break;
            case PracticeCommandKind.AddBot:
            case PracticeCommandKind.AddCrouchedBot:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以添加练习机器人。"); return; }
                result = _practiceRuntime.AddBot(parsed.Kind == PracticeCommandKind.AddCrouchedBot);
                break;
            case PracticeCommandKind.RemoveBots:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以移除机器人。"); return; }
                result = _practiceRuntime.RemoveBots();
                break;
            case PracticeCommandKind.CounterTerroristSpawn:
                result = TeleportPracticePlayerToSpawn(player, PracticeSpawnSide.CounterTerrorist);
                break;
            case PracticeCommandKind.TerroristSpawn:
                result = TeleportPracticePlayerToSpawn(player, PracticeSpawnSide.Terrorist);
                break;
            case PracticeCommandKind.God:
                result = _practiceRuntime.SetGodMode(!_practiceGodEnabled);
                if (result.Ok) _practiceGodEnabled = !_practiceGodEnabled;
                break;
            case PracticeCommandKind.Impacts:
                result = _practiceRuntime.SetImpacts(!_practiceImpactsEnabled);
                if (result.Ok) _practiceImpactsEnabled = !_practiceImpactsEnabled;
                break;
            case PracticeCommandKind.Trajectory:
                result = _practiceRuntime.SetTrajectory(!_practiceTrajectoryEnabled);
                if (result.Ok) _practiceTrajectoryEnabled = !_practiceTrajectoryEnabled;
                break;
            case PracticeCommandKind.Last:
            case PracticeCommandKind.SaveLineup:
            case PracticeCommandKind.LoadLineup:
            case PracticeCommandKind.DeleteLineup:
                if (!IsRealPlayer(player)) { ReplyToPlayer(player, "[草人杯] 点位命令只能由游戏内玩家执行。"); return; }
                _ = HandlePracticeLineupCommandAsync(player!, parsed);
                return;
            default:
                result = new(false, "UNKNOWN_PRACTICE_COMMAND");
                break;
        }
        ReleaseAuthorityIfIdle();
        if (result.Ok && isAdmin && parsed.Kind is not (PracticeCommandKind.Last or PracticeCommandKind.SaveLineup or PracticeCommandKind.LoadLineup or PracticeCommandKind.DeleteLineup))
            PrintToAllPlayers($"[草人杯] 练习设置已生效：{parsed.Kind}（{result.Code}）。");
        else ReplyToPlayer(player, $"[草人杯] {result.Code}{(string.IsNullOrWhiteSpace(result.Message) ? string.Empty : "：" + result.Message)}");
    }

    private MatchCommandResult TeleportPracticePlayerToSpawn(
        CCSPlayerController? player,
        PracticeSpawnSide side)
    {
        if (!IsRealPlayer(player) || _practiceRuntime?.Stage != PracticeStage.Active)
            return new(false, "PRACTICE_INACTIVE", "练习模式尚未开启。");
        var designerName = side == PracticeSpawnSide.CounterTerrorist
            ? "info_player_counterterrorist"
            : "info_player_terrorist";
        var spawn = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(designerName)
            .FirstOrDefault(entity => entity.IsValid && entity.AbsOrigin is not null);
        var pawn = player!.PlayerPawn?.Value;
        if (spawn?.AbsOrigin is null || pawn is null)
            return new(false, "SPAWN_NOT_FOUND", "当前地图没有找到对应出生点。");
        pawn.Teleport(
            new Vector(spawn.AbsOrigin.X, spawn.AbsOrigin.Y, spawn.AbsOrigin.Z),
            spawn.AbsRotation,
            new Vector(0, 0, 0));
        return new(true, "TELEPORTED");
    }

    private async Task HandlePracticeLineupCommandAsync(
        CCSPlayerController player,
        ParsedPracticeCommand parsed)
    {
        if (_practiceLineups is null || _practiceRuntime?.Stage != PracticeStage.Active) return;
        var steamId = player.SteamID.ToString();
        var mapName = SafeMapName();
        try
        {
            if (parsed.Kind == PracticeCommandKind.Last)
            {
                var lineups = await _practiceLineups.ListAsync(steamId, mapName);
                Server.NextFrame(() => ReplyToPlayer(player, lineups.Count == 0
                    ? "[草人杯] 当前地图还没有保存点位。"
                    : "[草人杯] 已保存点位：" + string.Join("、", lineups.Select(item => $"{item.Id}({item.Name})"))));
                return;
            }
            if (parsed.Kind == PracticeCommandKind.SaveLineup)
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn?.AbsOrigin is null || pawn.EyeAngles is null) return;
                var name = parsed.Arguments[0];
                var id = "lineup-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var grenade = PlayerEquipment(player).ActiveWeapon.Replace("weapon_", string.Empty, StringComparison.OrdinalIgnoreCase);
                await _practiceLineups.SaveAsync(steamId, mapName, new PracticeLineup(
                    id,
                    name,
                    null,
                    string.IsNullOrWhiteSpace(grenade) ? "smokegrenade" : grenade,
                    pawn.AbsOrigin.X,
                    pawn.AbsOrigin.Y,
                    pawn.AbsOrigin.Z,
                    pawn.EyeAngles.X,
                    pawn.EyeAngles.Y));
                Server.NextFrame(() => ReplyToPlayer(player, $"[草人杯] 已保存点位 {id}：{name}"));
                return;
            }
            var lineupId = parsed.Arguments[0];
            if (parsed.Kind == PracticeCommandKind.DeleteLineup)
            {
                var removed = await _practiceLineups.DeleteAsync(steamId, mapName, lineupId);
                Server.NextFrame(() => ReplyToPlayer(player, removed ? "[草人杯] 点位已删除。" : "[草人杯] 未找到该点位。"));
                return;
            }
            var lineup = (await _practiceLineups.ListAsync(steamId, mapName)).FirstOrDefault(item => item.Id == lineupId);
            Server.NextFrame(() =>
            {
                var pawn = player.PlayerPawn?.Value;
                if (lineup is null || pawn is null) { ReplyToPlayer(player, "[草人杯] 未找到该点位。"); return; }
                pawn.Teleport(
                    new Vector(lineup.X, lineup.Y, lineup.Z),
                    new QAngle(lineup.Pitch, lineup.Yaw, 0),
                    new Vector(0, 0, 0));
                ReplyToPlayer(player, $"[草人杯] 已加载点位：{lineup.Name}");
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Practice lineup command failed.");
            Server.NextFrame(() => ReplyToPlayer(player, "[草人杯] 点位操作失败。"));
        }
    }

    private void HandleMatchCommand(CCSPlayerController? player, ParsedMatchCommand parsed)
    {
        if (_matchRuntimeBridge is null)
        {
            ReplyToPlayer(player, "[草人杯] 比赛运行时尚未初始化。");
            return;
        }
        var isAdmin = player is null || AdminManager.PlayerHasPermissions(player, "@css/root");
        var steamId = IsRealPlayer(player) ? player!.SteamID.ToString() : string.Empty;
        if (parsed.Kind is MatchCommandKind.Ready or MatchCommandKind.Unready or MatchCommandKind.Start)
            RefreshRuntimeRosterPlayers();
        var roster = ResolveRuntimeRoster(player);
        MatchCommandResult result;

        switch (parsed.Kind)
        {
            case MatchCommandKind.Ready:
            case MatchCommandKind.Unready:
                if (!IsRealPlayer(player)) { ReplyToPlayer(player, "[草人杯] Ready 只能由正式玩家执行。"); return; }
                ObserveRuntimePlayer(player!);
                result = ApplyLocalRuntimeAction("set_ready", new Dictionary<string, object?>
                {
                    ["steamId"] = steamId,
                    ["ready"] = parsed.Kind == MatchCommandKind.Ready,
                });
                if (result.Ok && parsed.Kind == MatchCommandKind.Ready && _matchRuntimeBridge.Snapshot.Stage == MatchStage.ReadyCheck)
                {
                    var automatic = ApplyLocalRuntimeAction("begin_going_live", new Dictionary<string, object?>
                    {
                        ["force"] = false,
                        ["abilitySyncConfirmed"] = !_abilityModeRequired || _abilitySyncConfirmed,
                    });
                    if (automatic.Ok) PrintToAllPlayers("[草人杯] 全员 Ready，正在正式开赛。");
                }
                break;
            case MatchCommandKind.Start:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以强制开赛。"); return; }
                result = ApplyLocalRuntimeAction("begin_going_live", new Dictionary<string, object?>
                {
                    ["force"] = parsed.Arguments.Contains("confirm", StringComparer.OrdinalIgnoreCase),
                    ["abilitySyncConfirmed"] = !_abilityModeRequired || _abilitySyncConfirmed,
                });
                break;
            case MatchCommandKind.Pause:
            case MatchCommandKind.TechnicalPause:
                result = ApplyLocalRuntimeAction("technical_pause", new Dictionary<string, object?>
                {
                    ["reason"] = "player technical pause",
                    ["requestedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                break;
            case MatchCommandKind.TacticalPause:
                if (roster is null) { ReplyToPlayer(player, "[草人杯] 无法识别你所属的 A/B 队。"); return; }
                result = ApplyLocalRuntimeAction("tactical_pause", new Dictionary<string, object?>
                {
                    ["roster"] = roster.Value.ToString(),
                    ["requestedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                break;
            case MatchCommandKind.Unpause:
                if (roster is null) { ReplyToPlayer(player, "[草人杯] 无法识别你所属的 A/B 队。"); return; }
                result = ApplyLocalRuntimeAction("confirm_unpause", new Dictionary<string, object?> { ["roster"] = roster.Value.ToString() });
                break;
            case MatchCommandKind.ForcePause:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以强制暂停。"); return; }
                result = ApplyLocalRuntimeAction("force_pause", new Dictionary<string, object?>
                {
                    ["reason"] = "administrator",
                    ["requestedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                break;
            case MatchCommandKind.ForceUnpause:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以强制恢复。"); return; }
                result = ApplyLocalRuntimeAction("force_unpause", new Dictionary<string, object?>());
                break;
            case MatchCommandKind.Stop:
                roster = _matchRuntimeBridge.CaptainRosterForSteamId(steamId);
                if (roster is null) { ReplyToPlayer(player, "[草人杯] 只有双方队长可以确认 .stop。"); return; }
                result = ApplyLocalRuntimeAction("stop", new Dictionary<string, object?>
                {
                    ["roster"] = roster.Value.ToString(),
                    ["requestedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                break;
            case MatchCommandKind.Restore:
                if (!isAdmin) { ReplyToPlayer(player, "[草人杯] 只有管理员可以恢复指定回合。"); return; }
                var round = int.Parse(parsed.Arguments[0]);
                if (!parsed.Arguments.Contains("confirm", StringComparer.OrdinalIgnoreCase))
                {
                    ReplyToPlayer(player, $"[草人杯] 请再次输入 .restore {round} confirm 确认恢复。");
                    return;
                }
                result = ApplyLocalRuntimeAction("restore", new Dictionary<string, object?> { ["targetRound"] = round });
                break;
            case MatchCommandKind.CcMatch:
                HandleCcMatchCommand(player, parsed);
                return;
            default:
                ReplyToPlayer(player, "[草人杯] 该命令属于练习模式或尚不适用于当前比赛。");
                return;
        }

        DispatchPendingAbilityRestore();
        if (result.Ok && parsed.Kind == MatchCommandKind.Stop && result.Code == "WAITING_FOR_OTHER_CAPTAIN")
            PrintToAllPlayers("[草人杯] 已收到一队 .stop，等待另一队队长确认。");
        ReplyToPlayer(player, $"[草人杯] {result.Code}{(string.IsNullOrWhiteSpace(result.Message) ? string.Empty : "：" + result.Message)}");
    }

    private void HandleCcMatchCommand(CCSPlayerController? player, ParsedMatchCommand parsed)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            ReplyToPlayer(player, "[草人杯] 只有管理员可以使用 /ccmatch。");
            return;
        }
        var subcommand = parsed.Arguments.FirstOrDefault() ?? "status";
        if (subcommand == "status")
        {
            var snapshot = _matchRuntimeBridge?.Snapshot ?? MatchRuntimeSnapshot.Empty;
            ReplyToPlayer(player, $"[草人杯] {snapshot.Stage} A {snapshot.ScoreA}:{snapshot.ScoreB} B，epoch {snapshot.RuntimeEpoch}，暂停 {snapshot.PauseState}，待清理 {_matchRuntimeBridge?.PendingCleanup == true}");
            return;
        }
        var action = subcommand switch
        {
            "start" => ".start" + (parsed.Arguments.Contains("confirm") ? " confirm" : string.Empty),
            "pause" => ".forcepause",
            "abort" => string.Empty,
            "restore" => ".restore " + string.Join(' ', parsed.Arguments.Skip(1)),
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(action))
        {
            var reparsed = MatchCommandParser.Parse(action);
            if (reparsed.Ok && reparsed.Command is not null) HandleMatchCommand(player, reparsed.Command);
            return;
        }
        if (subcommand == "abort")
        {
            var result = ApplyLocalRuntimeAction("abort", new Dictionary<string, object?> { ["reason"] = "administrator" });
            ReplyToPlayer(player, $"[草人杯] {result.Code}");
            return;
        }
        ReplyToPlayer(player, "[草人杯] 未知 /ccmatch 子命令。");
    }

    private MatchCommandResult ApplyLocalRuntimeAction(string action, IReadOnlyDictionary<string, object?> values)
    {
        if (action == "abort" && _matchRuntimeBridge?.PendingCleanup == true)
        {
            var cleanup = _matchRuntimeBridge.Cleanup("administrator cleanup retry");
            ReleaseAuthorityIfIdle();
            if (cleanup.Ok) PrintToAllPlayers("[草人杯] 比赛运行时待恢复状态已清理。");
            return cleanup;
        }
        if (!RuntimeOwnsMatch || _matchRuntimeBridge is null) return new(false, "FEATURE_DISABLED");
        if (action != "abort" && !MatchRuntimeOwnership.CanClaim(RuntimeOwnsMatch,
            CaorenCupCoreAccess.TryGet()?.GameModes.State.Lifecycle,CaorenCupPracticeState.IsPracticeActive,
            ConVar.Find("matchzy_autostart_mode")?.StringValue,MatchRuntimeOwnership.IsMatchZyLoaded))
            return new(false,"CONTROLLER_CONFLICT");
        var snapshot = _matchRuntimeBridge.Snapshot;
        var payload = new Dictionary<string, object?>(values)
        {
            ["action"] = action,
            ["matchId"] = snapshot.MatchId,
            ["revision"] = snapshot.Revision,
            ["runtimeEpoch"] = snapshot.RuntimeEpoch,
        };
        var result = _matchRuntimeBridge.ApplyWebCommand(
            JsonSerializer.SerializeToElement(payload, _jsonOptions),
            RuntimeOwnsMatch,
            _currentMatchController);
        if (result.Ok && action is "begin_going_live" or "force_pause" or "force_unpause" or "restore" or "abort" or "substitute") PrintToAllPlayers($"[草人杯] 比赛运行时操作已生效：{action}。");
        if (result.Ok && action == "abort") ClearTeamAssignments();
        if (result.Ok && action == "substitute")
        {
            SyncRuntimeRosterAssignmentsFromAdapter();
            DispatchAbilitySubstitution(values);
        }
        ReleaseAuthorityIfIdle();
        return result;
    }

    private void ApplyRuntimeRosterAssignments(JsonElement payload)
    {
        if (!payload.TryGetProperty("seats", out var seats) || seats.ValueKind != JsonValueKind.Array) return;
        var initialSide = payload.TryGetProperty("teamAInitialSide", out var sideElement)
            ? sideElement.GetString() ?? "CT"
            : "CT";
        _teamAssignments.Clear();
        foreach (var seat in seats.EnumerateArray())
        {
            if (!seat.TryGetProperty("steamId", out var steamElement)
                || !seat.TryGetProperty("roster", out var rosterElement)) continue;
            var steamId = steamElement.GetString()?.Trim();
            var roster = rosterElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(steamId)) continue;
            _teamAssignments[steamId] = ResolveRuntimeAssignmentSide(roster, initialSide) == "CT"
                ? CsTeam.CounterTerrorist
                : CsTeam.Terrorist;
        }
        _teamLockEnabled = _teamAssignments.Count > 0;
        _teamAssignmentsValidFromRound = 0;
        _teamAssignmentsValidUntilRound = 0;
        Server.NextFrame(() =>
        {
            if (!_isUnloading) EnforceTeamAssignments();
        });
    }

    private void SyncRuntimeRosterAssignmentsFromAdapter()
    {
        if (_matchRuntimeBridge is null) return;
        _teamAssignments.Clear();
        foreach (var assignment in _matchRuntimeBridge.CurrentRosterAssignments)
            _teamAssignments[assignment.SteamId] = assignment.Side == EngineSide.CounterTerrorist
                ? CsTeam.CounterTerrorist
                : CsTeam.Terrorist;
        _teamLockEnabled = _teamAssignments.Count > 0;
        _teamAssignmentBypass.Clear();
    }

    private void ObserveRuntimePlayer(CCSPlayerController player)
    {
        var side = TeamName(player.TeamNum);
        ApplyLocalRuntimeAction("observe_player", new Dictionary<string, object?>
        {
            ["steamId"] = player.SteamID.ToString(),
            ["side"] = side,
            ["connected"] = true,
        });
    }

    private void RefreshRuntimeRosterPlayers()
    {
        if (_matchRuntimeBridge is null) return;
        var players = Utilities.GetPlayers()
            .Where(IsRealPlayer)
            .Select(player =>
            {
                var side = TeamName(player.TeamNum);
                EngineSide? engineSide = side == "CT"
                    ? EngineSide.CounterTerrorist
                    : side == "T" ? EngineSide.Terrorist : null;
                return (Player: player, Side: engineSide);
            })
            .Where(item => item.Side is not null)
            .Select(item => (item.Player.SteamID.ToString(), item.Side!.Value))
            .ToArray();
        _matchRuntimeBridge.RefreshRosterPlayers(players);
    }

    private RosterSide? ResolveRuntimeRoster(CCSPlayerController? player)
    {
        if (!IsRealPlayer(player) || _matchRuntimeBridge is null) return null;
        var side = TeamName(player!.TeamNum);
        if (side is not ("CT" or "T")) return null;
        var playerSide = side == "CT" ? EngineSide.CounterTerrorist : EngineSide.Terrorist;
        return playerSide == _matchRuntimeBridge.Snapshot.TeamAEngineSide ? RosterSide.A : RosterSide.B;
    }

    private void DispatchAbilitySubstitution(IReadOnlyDictionary<string, object?> values)
    {
        var confirmed = _confirmedAbilityRuntimeIdentity;
        var snapshot = _matchRuntimeBridge?.Snapshot;
        if (confirmed is null || snapshot is null) return;
        var envelope = confirmed with
        {
            Action = "substitute",
            RuntimeEpoch = snapshot.RuntimeEpoch,
            RoundKey = snapshot.CurrentRoundKey,
            SeatId = values.GetValueOrDefault("seatId")?.ToString(),
            SteamId = values.GetValueOrDefault("steamId")?.ToString(),
        };
        var command = new PluginCommand
        {
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(envelope, _jsonOptions),
        };
        try
        {
            var internalCommand = AbilityRuntimeProtocol.BuildInternalCommand(command);
            Server.ExecuteCommand($"{internalCommand.Name} {internalCommand.Argument}");
        }
        catch (AbilityRuntimeProtocolException ex)
        {
            Logger.LogError(ex, "Failed to dispatch ability substitution.");
        }
    }

    private void DispatchPendingAbilityRestore()
    {
        var pending = _matchRuntimeBridge?.TakePendingAbilityRestore();
        var persistedStats = _matchRuntimeBridge?.TakePendingBridgeStatsRestore();
        var confirmed = _confirmedAbilityRuntimeIdentity;
        if (pending is null) return;
        SyncRuntimeRosterAssignmentsFromAdapter();
        if (persistedStats is not null) RestoreBridgeStatsCheckpoint(persistedStats, pending.CheckpointId);
        else RestoreBridgeStatsCheckpoint(pending.CheckpointId);
        if (confirmed is null) return;
        var envelope = confirmed with
        {
            Action = "restore",
            RuntimeEpoch = pending.RuntimeEpoch,
            RoundKey = pending.RoundKey,
            CheckpointId = pending.CheckpointId,
            SeatId = null,
            SteamId = null,
            Outcome = null,
        };
        var command = new PluginCommand
        {
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(envelope, _jsonOptions),
        };
        try
        {
            var internalCommand = AbilityRuntimeProtocol.BuildInternalCommand(command);
            Server.ExecuteCommand($"{internalCommand.Name} {internalCommand.Argument}");
        }
        catch (AbilityRuntimeProtocolException ex)
        {
            Logger.LogError(ex, "Failed to dispatch ability restore.");
        }
    }

    private void CaptureBridgeStatsCheckpoint()
    {
        var snapshot = _matchRuntimeBridge?.Snapshot;
        if (snapshot is null) return;
        var roundNumber = Math.Max(1, snapshot.CompletedRounds + 1);
        if (_bridgeStatsCheckpoints.ContainsKey(roundNumber)) return;
        var stats = _stats.ToDictionary(
            pair => pair.Key,
            pair => new BridgePlayerStats(
                pair.Value.Name,
                pair.Value.Kills,
                pair.Value.Deaths,
                pair.Value.Assists,
                pair.Value.Damage),
            StringComparer.Ordinal);
        _bridgeStatsCheckpoints[roundNumber] = BridgeStatsCheckpoint.Capture(
            roundNumber,
            snapshot.RuntimeEpoch,
            stats);
        _matchRuntimeBridge?.AttachBridgeStatsCheckpoint(roundNumber, stats);
    }

    private void RestoreBridgeStatsCheckpoint(
        IReadOnlyDictionary<string, BridgePlayerStats> persistedStats,
        string checkpointId)
    {
        var tail = checkpointId.Split('-').LastOrDefault();
        if (!int.TryParse(tail, out var roundNumber)) return;
        _stats.Clear();
        foreach (var pair in persistedStats)
        {
            _stats[pair.Key] = new LocalPlayerStats
            {
                Name = pair.Value.Name,
                Kills = pair.Value.Kills,
                Deaths = pair.Value.Deaths,
                Assists = pair.Value.Assists,
                Damage = pair.Value.Damage,
            };
        }
        _currentRound = Math.Max(0, roundNumber - 1);
    }
    private void RestoreBridgeStatsCheckpoint(string checkpointId)
    {
        var tail = checkpointId.Split('-').LastOrDefault();
        if (!int.TryParse(tail, out var roundNumber)
            || !_bridgeStatsCheckpoints.TryGetValue(roundNumber, out var checkpoint)) return;
        var restored = new Dictionary<string, BridgePlayerStats>(StringComparer.Ordinal);
        checkpoint.RestoreTo(restored);
        _stats.Clear();
        foreach (var pair in restored)
        {
            _stats[pair.Key] = new LocalPlayerStats
            {
                Name = pair.Value.Name,
                Kills = pair.Value.Kills,
                Deaths = pair.Value.Deaths,
                Assists = pair.Value.Assists,
                Damage = pair.Value.Damage,
            };
        }
        _currentRound = Math.Max(0, roundNumber - 1);
    }

    private void DispatchCaorenAbilityStop(string reason)
    {
        var confirmed = _confirmedAbilityRuntimeIdentity;
        var snapshot = _matchRuntimeBridge?.Snapshot;
        if (confirmed is null || snapshot is null) return;
        var envelope = confirmed with
        {
            Action = "stop",
            RuntimeEpoch = snapshot.RuntimeEpoch,
            RoundKey = snapshot.CurrentRoundKey,
            SeatId = null,
            SteamId = null,
            Outcome = null,
            CheckpointId = null,
        };
        var command = new PluginCommand
        {
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(envelope, _jsonOptions),
        };
        try
        {
            var internalCommand = AbilityRuntimeProtocol.BuildInternalCommand(command);
            Server.ExecuteCommand($"{internalCommand.Name} {internalCommand.Argument}");
            Logger.LogInformation("Stopped ability runtime after Caoren match completion: {Reason}", reason);
        }
        catch (AbilityRuntimeProtocolException ex)
        {
            Logger.LogError(ex, "Failed to stop ability runtime after Caoren match completion.");
        }
    }

    private void DispatchCaorenAbilityRoundStart(bool firstRound)
    {
        var confirmed = _confirmedAbilityRuntimeIdentity;
        var snapshot = _matchRuntimeBridge?.Snapshot;
        if (confirmed is null || snapshot is null) return;
        try
        {
            var command = AbilityRuntimeProtocol.BuildAuthoritativeRoundCommand(confirmed, snapshot, firstRound);
            Server.ExecuteCommand($"{command.Name} {command.Argument}");
        }
        catch (AbilityRuntimeProtocolException ex)
        {
            Logger.LogError(ex, "Failed to dispatch authoritative ability round command.");
        }
    }

    private static bool ShouldUseCaorenMatchRuntime(
        bool enabled,
        string? matchController,
        MatchStage stage) =>
        enabled
        && string.Equals(matchController, "caoren", StringComparison.OrdinalIgnoreCase)
        && stage is not MatchStage.Disabled and not MatchStage.Completed and not MatchStage.Aborted;

    internal static bool IsPracticeBlockingMatchAction(PracticeStage practiceStage, string? action) =>
        practiceStage is not PracticeStage.Inactive
        && action is "load" or "begin_warmup" or "begin_ready_check" or "begin_going_live";

    internal static string ResolveRuntimeAssignmentSide(string roster, string teamAInitialSide) =>
        string.Equals(roster, "A", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(teamAInitialSide, "T", StringComparison.OrdinalIgnoreCase) ? "T" : "CT"
            : string.Equals(teamAInitialSide, "T", StringComparison.OrdinalIgnoreCase) ? "CT" : "T";

    private async Task<bool> ApplyAbilitySyncCommandAsync(HeartbeatPluginCommand heartbeatCommand)
    {
        await Task.CompletedTask;
        if (CaorenCupAbilityAccess.TryGet() is null) return true;
        if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
        if (!IsHeartbeatCommandSourceCurrent(heartbeatCommand)) return true;

        IReadOnlyList<AbilitySyncInternalCommand> commands;
        AbilitySyncProtocol.AbilitySyncBeginEnvelope begin;
        try
        {
            commands = AbilitySyncProtocol.BuildInternalCommands(heartbeatCommand.Command);
            begin = AbilitySyncProtocol.Decode<AbilitySyncProtocol.AbilitySyncBeginEnvelope>(commands[0].Argument);
        }
        catch (AbilitySyncProtocolException ex)
        {
            Logger.LogWarning(ex, "Rejected malformed ability sync command from web queue.");
            return true;
        }

        _pendingAbilitySyncDigests[begin.SyncId] = begin.ContentDigest;
        try
        {
            _ = PostAbilitySyncProgressAsync(begin);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to report ability sync validation progress; continuing internal transaction.");
        }

        foreach (var internalCommand in commands)
        {
            if (!ShouldProcessPluginContinuation(_isUnloading)) return false;
            Server.ExecuteCommand($"{internalCommand.Name} {internalCommand.Argument}");
        }
        return true;
    }

    private void OnAbilitySyncAckCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) return;
        if (info.ArgCount < 2) return;
        try
        {
            var ack = AbilitySyncProtocol.Decode<AbilitySyncFinalAckEnvelope>(info.GetArg(1));
            if (!_pendingAbilitySyncDigests.TryGetValue(ack.SyncId, out var expectedDigest)
                || !string.Equals(expectedDigest, ack.ContentDigest, StringComparison.Ordinal))
            {
                Logger.LogWarning("Ignored ability sync ACK with no matching bridge transaction: {SyncId}", ack.SyncId);
                return;
            }
            if (string.Equals(ack.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                _confirmedAbilityRuntimeIdentity = new AbilityRuntimeProtocol.AbilityRuntimeEnvelope(
                    "start",
                    ack.MatchId,
                    ack.Revision,
                    ack.ContentDigest,
                    Math.Max(1, _matchRuntimeBridge?.Snapshot.RuntimeEpoch ?? 1),
                    $"round-{Math.Max(1, _currentRound)}");
                TryStartConfirmedAbilityRuntime();
            }
            _ = PostAbilitySyncFinalAckAsync(ack);
        }
        catch (AbilitySyncProtocolException ex)
        {
            Logger.LogWarning(ex, "Ignored malformed ability sync ACK from entertainment plugin.");
        }
    }

    private void OnAbilityRuntimeReadyCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) return;
        if (player is not null) return;
        if (AbilityRuntimeProtocol.ShouldDispatchAuthoritativeReady(
                _confirmedAbilityRuntimeIdentity,
                _matchRuntimeBridge?.Snapshot,
                _currentMatchController))
        {
            DispatchCaorenAbilityRoundStart(firstRound: true);
            return;
        }
        TryStartConfirmedAbilityRuntime(force: true);
    }

    private void TryStartConfirmedAbilityRuntime(bool force = false)
    {
        var identity = _confirmedAbilityRuntimeIdentity;
        if (identity is null
            || string.Equals(_currentMatchController, "caoren", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_currentWebPhase, "LiveGame", StringComparison.Ordinal)
            || !string.Equals(identity.MatchId, _currentMatchId, StringComparison.Ordinal)
            || IsServerWarmup()) return;
        var current = identity with { RoundKey = $"round-{Math.Max(1, _currentRound)}" };
        var key = $"{current.MatchId}|{current.Revision}|{current.ContentDigest}|{current.RoundKey}";
        if (!force && string.Equals(key, _lastAbilityRuntimeStartKey, StringComparison.Ordinal)) return;
        Server.ExecuteCommand($"{AbilityRuntimeProtocol.StartCommandName} {AbilitySyncProtocol.Encode(current)}");
        _lastAbilityRuntimeStartKey = key;
    }

    private void ResetEntertainmentAbilitySyncState(string? matchId)
    {
        _confirmedAbilityRuntimeIdentity = null;
        _lastAbilityRuntimeStartKey = null;
        try
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                Server.ExecuteCommand(AbilitySyncProtocol.ClearCommandName);
                return;
            }
            var reset = AbilitySyncProtocol.BuildResetCommand(matchId);
            Server.ExecuteCommand($"{reset.Name} {reset.Argument}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to reset entertainment ability sync state for MatchId={MatchId}", matchId);
        }
    }

    private async Task PostAbilitySyncProgressAsync(AbilitySyncProtocol.AbilitySyncBeginEnvelope begin)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        using var response = await _http.PostAsJsonAsync("api/plugin/ability-sync-progress", new
        {
            syncId = begin.SyncId,
            matchId = begin.MatchId,
            revision = begin.Revision,
            contentDigest = begin.ContentDigest,
        }, _jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("Ability sync progress rejected: {Status}", response.StatusCode);
        }
    }

    private async Task PostAbilitySyncFinalAckAsync(AbilitySyncFinalAckEnvelope ack)
    {
        if (!ShouldProcessPluginContinuation(_isUnloading)) return;
        using var response = await _http.PostAsJsonAsync("api/plugin/ability-sync-ack", new
        {
            status = ack.Status,
            syncId = ack.SyncId,
            matchId = ack.MatchId,
            revision = ack.Revision,
            contentDigest = ack.ContentDigest,
            appliedSeatCount = ack.AppliedSeatCount,
            errorCode = ack.ErrorCode,
            errorMessage = ack.ErrorMessage,
        }, _jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("Ability sync final ACK rejected: {Status}", response.StatusCode);
        }
    }

    private static double ReadPayloadDouble(JsonElement payload, string propertyName, double fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var element) ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            return fallback;
        }

        return Math.Clamp(value, 0.25, 5);
    }

    private static HashSet<string> GetConnectedRealPlayerSteamIds() => Utilities.GetPlayers()
        .Where(IsRealPlayer)
        .Select(candidate => candidate.SteamID.ToString())
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsServerWarmup()
    {
        try
        {
            var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules;
            return rules?.WarmupPeriod ?? true;
        }
        catch
        {
            return true;
        }
    }
    private void ApplyRuntimeWebCommand(PluginCommand command)
    {
        if (_matchRuntimeBridge?.PendingCleanup == true && command.Payload.ValueKind == JsonValueKind.Object
            && command.Payload.TryGetProperty("action", out var cleanupAction) && cleanupAction.GetString() == "abort")
        {
            // Cleanup stays reachable even if the web/local enable flag changed after settlement.
            var cleanup = _matchRuntimeBridge.ApplyWebCommand(command.Payload, true, "caoren");
            if (cleanup.Ok) PrintToAllPlayers("[草人杯] 比赛运行时待恢复状态已清理。");
            else Logger.LogWarning("Runtime cleanup retry rejected: {Code}", cleanup.Code);
            ReleaseAuthorityIfIdle();
            return;
        }
        if (!RuntimeOwnsMatch || _matchRuntimeBridge is null || command.Payload.ValueKind != JsonValueKind.Object) return;
        var matchZyAutoStart=ConVar.Find("matchzy_autostart_mode")?.StringValue;
        if (!MatchRuntimeOwnership.CanClaim(RuntimeOwnsMatch,CaorenCupCoreAccess.TryGet()?.GameModes.State.Lifecycle,CaorenCupPracticeState.IsPracticeActive,matchZyAutoStart,MatchRuntimeOwnership.IsMatchZyLoaded)) {
            Logger.LogWarning("Rejecting bridge runtime: MatchZy automatic match control is enabled."); return;
        }
        if (CaorenCupPracticeState.IsPracticeActive || !CaorenCupMatchAuthority.TryAcquire(_matchAuthorityOwner)) return;
        var core = CaorenCupCoreAccess.TryGet()?.GameModes.State;
        if (core?.Lifecycle is GameModeLifecycleState.Warmup or GameModeLifecycleState.Live) return;
        var action = command.Payload.TryGetProperty("action", out var a) ? a.GetString() : "";
        if (IsPracticeBlockingMatchAction(_practiceRuntime?.Stage ?? PracticeStage.Inactive,action)) return;
        var result=_matchRuntimeBridge.ApplyWebCommand(command.Payload,true,_currentMatchController);
        if (!result.Ok) { Logger.LogWarning("Rejected match action: {Code}",result.Code);
            ReleaseAuthorityIfIdle(); return; }
        if (action is "load" or "begin_warmup" or "begin_ready_check" or "begin_going_live" or "force_pause" or "force_unpause" or "restore" or "abort" or "substitute") PrintToAllPlayers($"[草人杯] 比赛运行时操作已生效：{action}。");
        if (action=="load") { StopDemoRecording();_bridgeStatsCheckpoints.Clear();ApplyRuntimeRosterAssignments(command.Payload); }
        if (action=="confirm_side_swap" || action=="substitute") SyncRuntimeRosterAssignmentsFromAdapter();
        if (action=="substitute") DispatchAbilitySubstitution(new Dictionary<string,object?> {
            ["seatId"]=command.Payload.TryGetProperty("seatId",out var seat)?seat.GetString():null,
            ["steamId"]=command.Payload.TryGetProperty("steamId",out var steam)?steam.GetString():null });
        if (action=="abort") { ClearTeamAssignments();DispatchCaorenAbilityStop("aborted"); }
        DispatchPendingAbilityRestore();
        ReleaseAuthorityIfIdle();
    }

    private void CleanupAuthority(string reason)
    {
        var practiceCleanup=_practiceRuntime?.Stop();
        var cleanup=_matchRuntimeBridge?.Cleanup(reason);
        if ((cleanup is null || cleanup.Ok) && (practiceCleanup is null || practiceCleanup.Ok)) ReleaseAuthorityIfIdle();
    }

    private bool ObserveAuthoritativeRoundStart()
    {
        if (_matchRuntimeBridge?.RequiresAuthority != true) return false;
        if (!RuntimeOwnsMatch || _matchRuntimeBridge.PendingCleanup) return true;
        if (MatchRuntimeOwnership.IsMatchZyLoaded) return true;
        var first=_matchRuntimeBridge.Snapshot.Stage==MatchStage.GoingLive;
        var warmup = IsServerWarmup();
        if (first && !warmup) ResetLiveMatchStats(0);
        var result=_matchRuntimeBridge.OnNativeRoundStart(warmup, JsonSerializer.SerializeToElement(BuildLivePlayers(), _jsonOptions));
        if (result.Ok) {
            _currentRound=_matchRuntimeBridge.Snapshot.CompletedRounds+1;
            CaptureBridgeStatsCheckpoint(); DispatchCaorenAbilityRoundStart(first);
            RefreshRoundHealthState(); EnforceTeamAssignments();
        }
        return true; // Staging/warmup cannot leak a native round as a formal round.
    }

    private bool ObserveAuthoritativeRoundEnd(string? winner)
    {
        if (_matchRuntimeBridge?.RequiresAuthority != true) return false;
        if (!RuntimeOwnsMatch || _matchRuntimeBridge.PendingCleanup) return true;
        if (MatchRuntimeOwnership.IsMatchZyLoaded || winner is not ("CT" or "T")) return true;
        var result=_matchRuntimeBridge.OnNativeRoundEnd(winner=="CT"?EngineSide.CounterTerrorist:EngineSide.Terrorist,
            JsonSerializer.SerializeToElement(BuildLivePlayers(), _jsonOptions));
        if (!result.Ok) return true;
        _scoreCt=_matchRuntimeBridge.Snapshot.TeamAEngineSide==EngineSide.CounterTerrorist?_matchRuntimeBridge.Snapshot.ScoreA:_matchRuntimeBridge.Snapshot.ScoreB;
        _scoreT=_matchRuntimeBridge.Snapshot.TeamAEngineSide==EngineSide.Terrorist?_matchRuntimeBridge.Snapshot.ScoreA:_matchRuntimeBridge.Snapshot.ScoreB;
        QueueSnapshot();
        if (result.MatchFinished) { DispatchCaorenAbilityStop("completed"); ReleaseAuthorityIfIdle(); }
        return true;
    }
    private void RecoverRuntime(PluginHeartbeatResponse? state)
    {
        if (!RuntimeOwnsMatch || _matchRuntimeBridge is null || _matchRuntimeBridge.RequiresAuthority
            || state?.MatchRuntime is not { Stage: not MatchStage.Disabled and not MatchStage.Completed and not MatchStage.Aborted } snapshot
            || state.MatchRuntimeDefinition.ValueKind != JsonValueKind.Object) return;
        if (!MatchRuntimeOwnership.CanClaim(RuntimeOwnsMatch,CaorenCupCoreAccess.TryGet()?.GameModes.State.Lifecycle,
            CaorenCupPracticeState.IsPracticeActive,ConVar.Find("matchzy_autostart_mode")?.StringValue,MatchRuntimeOwnership.IsMatchZyLoaded)) return;
        if (!CaorenCupMatchAuthority.TryAcquire(_matchAuthorityOwner)) return;
        var result=_matchRuntimeBridge.RecoverFromWeb(state.MatchRuntimeDefinition,snapshot,state.MatchRestorePoints);
        if (!result.Ok) { ReleaseAuthorityIfIdle(); return; }
        SyncRuntimeRosterAssignmentsFromAdapter();DispatchPendingAbilityRestore();
    }
}
