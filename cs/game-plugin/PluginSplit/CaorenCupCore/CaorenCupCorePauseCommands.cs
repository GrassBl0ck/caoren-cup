using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Core;

public sealed partial class CaorenCupCorePlugin
{
    private static readonly string[] PauseCommands = ["css_pause", "css_p", "css_unpause", "css_up", "css_tac", "css_tech"];
    private const int HalftimePhase = 4;
    private const int MatchEndedPhase = 5;
    private readonly GameModePause _gameModePause = new();
    private long? _pauseMatchRevision;
    private long? _finishedMatchRevision;
    private double _pauseRequestedAt;
    private bool _awaitingTacticalConfirmation;

    private bool OwnsPauseCommands => GameModes.State.Lifecycle == GameModeLifecycleState.Live
        && !CaorenCupMatchAuthority.IsOwnedOutsideCore && !CaorenCupPracticeState.IsPracticeActive;

    private void RegisterGameModePauseCommands()
    {
        foreach (var name in PauseCommands)
            AddCommand(name, "草人杯正式比赛暂停与恢复", (_, _) => { });
        // The global pre-listener runs before command-specific plugin callbacks, independent of load order.
        AddCommandListener(null, OnGameModePauseCommand, HookMode.Pre);
        RegisterListener<Listeners.OnTick>(TickGameModePause);
        RegisterListener<Listeners.OnMapEnd>(FinishCorePauseMatch);
        RegisterEventHandler<EventCsWinPanelMatch>((_, _) =>
        {
            FinishCorePauseMatch();
            return HookResult.Continue;
        });
    }

    private HookResult OnGameModePauseCommand(CCSPlayerController? player, CommandInfo command)
    {
        // Let the active controller's normal command handlers run when Core does not own the match.
        if (!OwnsPauseCommands) return HookResult.Continue;
        var name = command.GetArg(0).ToLowerInvariant();
        if (!PauseCommands.Contains(name)) return HookResult.Continue;
        SynchronizePauseMatch();
        var resume = name is "css_unpause" or "css_up";
        var playerPause = name is "css_tac" or "css_tech";
        if (command.ArgCount != 1) return RejectPause(player, "暂停指令不接受额外参数。");
        if (!playerPause && !HasRoot(player)) return RejectPause(player, "只有管理员可以使用 /pause 或 /unpause。玩家请使用 /tac 或 /tech。");
        if (_finishedMatchRevision == _pauseMatchRevision) return RejectPause(player, "本场比赛已结束。");
        if (GameModes is GameModeRuntime { RoundRestorePending: true })
            return RejectPause(player, "正在等待 CS2 载入回合。");

        if (!playerPause)
        {
            var adminResult = ApplyMatchAdminAction(new(resume ? MatchAdminAction.Resume : MatchAdminAction.Pause));
            return adminResult.Success ? HookResult.Stop : RejectPause(player, adminResult.Message);
        }

        var rules = ReadGameRules();
        if (rules is null || rules.WarmupPeriod || rules.GameRestart)
            return RejectPause(player, "正式回合尚未就绪，请稍后重试。");
        if (rules.GamePhase == MatchEndedPhase) return RejectPause(player, "本场比赛已结束。");
        if (rules.MatchWaitingForResume) return RejectPause(player, "比赛正在暂停。");
        if (player is not { IsValid: true, IsBot: false, IsHLTV: false }
            || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            return RejectPause(player, "只有已加入 CT 或 T 的参赛玩家可以申请暂停。");
        if (!rules.FreezePeriod || rules.GamePhase == HalftimePhase || rules.SwitchingTeamsAtRoundReset)
            return RejectPause(player, "请在回合开始的冻结时间内申请暂停。");

        var tactical = name == "css_tac";
        var overtime = Math.Max(0, rules.OvertimePlaying);
        var roster = tactical && GameModePause.RosterForSide(player!.Team == CsTeam.CounterTerrorist,
            ConVar.Find("mp_halftime")?.GetPrimitiveValue<bool>() ?? true, rules.TotalRoundsPlayed,
            ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 24, overtime,
            ConVar.Find("mp_overtime_maxrounds")?.GetPrimitiveValue<int>() ?? 6);
        var result = _gameModePause.Request(tactical, overtime, roster);
        if (!result.Success) return RejectPause(player, result.Message);

        _awaitingTacticalConfirmation = tactical;
        _pauseRequestedAt = Server.EngineTime;
        Server.ExecuteCommand("mp_pause_match");
        if (!tactical)
            CaorenCupChat.PrintToChatAll("已请求技术暂停，请管理员确认后恢复。");
        return HookResult.Stop;
    }

    private void TickGameModePause()
    {
        SynchronizePauseMatch();
        if (!_gameModePause.Active) return;
        var now = Server.EngineTime;
        if (_awaitingTacticalConfirmation)
        {
            var rules = ReadGameRules();
            if (rules?.MatchWaitingForResume == true && rules.FreezePeriod)
            {
                _gameModePause.ConfirmTacticalPause(now);
                _awaitingTacticalConfirmation = false;
                CaorenCupChat.PrintToChatAll("战术暂停已开始，30 秒后自动恢复。");
            }
            else if (now - _pauseRequestedAt >= 2)
            {
                ResumeCoreMatch("战术暂停未在本次冻结时间生效，已取消请求，未扣除次数。");
            }
        }
        else if (_gameModePause.IsDue(now))
        {
            ResumeCoreMatch("战术暂停时间已到，已请求恢复比赛。");
        }
    }

    private void SynchronizePauseMatch()
    {
        long? revision = OwnsPauseCommands ? GameModes.State.Revision : null;
        if (_pauseMatchRevision == revision) return;
        ClearCorePause(resumeMatch: !CaorenCupMatchAuthority.IsOwnedOutsideCore && !CaorenCupPracticeState.IsPracticeActive);
        _pauseMatchRevision = revision;
    }

    private void ClearCorePause(bool resumeMatch)
    {
        if (_gameModePause.Active && resumeMatch) Server.ExecuteCommand("mp_unpause_match");
        _gameModePause.Reset();
        _awaitingTacticalConfirmation = false;
    }

    private void FinishCorePauseMatch()
    {
        if (OwnsPauseCommands) _finishedMatchRevision = GameModes.State.Revision;
        ClearCorePause(resumeMatch: false);
        if ((GameModes as GameModeRuntime)?.FinishLiveMatch() is { Success: false } result)
            Console.Error.WriteLine($"[草人杯] {result.Message}");
    }

    private void ResumeCoreMatch(string message)
    {
        _gameModePause.Clear();
        _awaitingTacticalConfirmation = false;
        Server.ExecuteCommand("mp_unpause_match");
        CaorenCupChat.PrintToChatAll(message);
    }

    private static HookResult RejectPause(CCSPlayerController? player, string message)
    {
        Reply(player, message);
        return HookResult.Stop;
    }

    private static CCSGameRules? ReadGameRules() => Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
        .FirstOrDefault()?.GameRules;
}
