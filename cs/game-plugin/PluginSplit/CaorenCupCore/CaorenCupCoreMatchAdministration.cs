using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Core;

public sealed partial class CaorenCupCorePlugin
{
    public MatchAdminState ReadMatchAdminState()
    {
        if (CaorenCupMatchAdministration.Hosted is { } hosted) return hosted.ReadMatchAdminState();

        var state = GameModes.State;
        var runtime = GameModes as GameModeRuntime;
        var busy = state.Lifecycle is GameModeLifecycleState.Warmup or GameModeLifecycleState.Live
            || runtime?.CleanupPending == true;
        var rules = GetLiveMatchRules();
        int? round = rules is null ? null : Math.Clamp(rules.TotalRoundsPlayed + 1, 1, 999);
        var restoring = runtime?.RoundRestorePending == true;
        IReadOnlyList<int> backups = !restoring && round is { } current
            ? Enumerable.Range(1, current).Where(number => FindRoundBackup(number) is not null).ToArray()
            : [];

        return new MatchAdminState(
            new(runtime?.MatchId, state.Revision, runtime?.RoundTimeline ?? 0, round),
            restoring ? $"{GameModes.GetStatusText()} | 回合恢复中" : GameModes.GetStatusText(),
            busy || CaorenCupMatchAuthority.IsOwnedOutsideCore ? [] : GameModes.AvailableModes.Where(mode => mode.Available).ToArray(),
            !busy && state.ModeId is not null && !CaorenCupMatchAuthority.IsOwnedOutsideCore,
            rules is not null && !restoring,
            rules is not null && (_gameModePause.Active || rules.MatchWaitingForResume),
            busy,
            backups);
    }

    public GameModeResult ApplyMatchAdminAction(MatchAdminRequest request)
    {
        if (CaorenCupMatchAdministration.Hosted is { } hosted) return hosted.ApplyMatchAdminAction(request);
        if (request.Expected is { } expected && expected != ReadMatchAdminState().Stamp)
            return GameModeResult.Fail("比赛或回合已经变化，请重新选择。");

        return request.Action switch
        {
            MatchAdminAction.SelectMode => GameModes.SelectMode(request.ModeId ?? string.Empty),
            MatchAdminAction.Start => GameModes.Start(),
            MatchAdminAction.Stop => StopCoreMatch(),
            MatchAdminAction.Pause => ApplyCoreAdminPause(resume: false),
            MatchAdminAction.Resume => ApplyCoreAdminPause(resume: true),
            MatchAdminAction.RestoreRound => RequestCoreRoundRestore(request.Round),
            _ => GameModeResult.Fail("无效的比赛操作。"),
        };
    }

    private GameModeResult StopCoreMatch()
    {
        if (OwnsPauseCommands) ClearCorePause(resumeMatch: true);
        return GameModes.Stop();
    }

    private CCSGameRules? GetLiveMatchRules()
    {
        if (!OwnsPauseCommands || _finishedMatchRevision == GameModes.State.Revision)
            return null;
        var rules = ReadGameRules();
        return rules is { WarmupPeriod: false, GameRestart: false } && rules.GamePhase != MatchEndedPhase
            ? rules : null;
    }

    private string? FindRoundBackup(int round)
    {
        var fileName = (GameModes as GameModeRuntime)?.GetRoundBackupFileName(round);
        return fileName is not null && File.Exists(Path.Combine(Server.GameDirectory, "csgo", fileName)) ? fileName : null;
    }

    private GameModeResult RequestCoreRoundRestore(int? round)
    {
        var rules = GetLiveMatchRules();
        if (rules is null) return GameModeResult.Fail("当前没有可恢复回合的正式对局。");
        if (round is null or < 1 or > 999 || round.Value > rules.TotalRoundsPlayed + 1)
            return GameModeResult.Fail("请选择本场已经开始的有效回合。");
        var fileName = FindRoundBackup(round.Value);
        if (fileName is null)
            return GameModeResult.Fail($"本场第 {round} 回合没有可用备份。请核对服务端启动参数 -allow_round_backup_files。");

        return GameModes is GameModeRuntime runtime
            ? runtime.RequestRoundRestore(round.Value)
            : GameModeResult.Fail("回合恢复暂不可用。");
    }

    private HookResult ConfirmEngineRoundRestore(EventRoundStart _, GameEventInfo __)
    {
        if (GameModes is GameModeRuntime { RoundRestorePending: true } runtime
            && ReadGameRules() is { WarmupPeriod: false } rules)
            runtime.ConfirmRoundRestore(rules.TotalRoundsPlayed + 1);
        return HookResult.Continue;
    }

    private GameModeResult ApplyCoreAdminPause(bool resume)
    {
        if (GameModes is GameModeRuntime { RoundRestorePending: true })
            return GameModeResult.Fail("正在等待 CS2 载入回合。");
        SynchronizePauseMatch();
        var rules = GetLiveMatchRules();
        if (rules is null) return GameModeResult.Fail("正式回合尚未就绪或比赛已经结束。");

        if (resume)
        {
            if (!_gameModePause.Active && !rules.MatchWaitingForResume)
                return GameModeResult.Fail("比赛当前没有暂停。");
            const string message = "管理员已请求恢复比赛。";
            ResumeCoreMatch(message);
            return GameModeResult.Ok(message);
        }

        if (rules.MatchWaitingForResume) return GameModeResult.Fail("比赛正在暂停。");
        var result = _gameModePause.Request(tactical: false, overtime: Math.Max(0, rules.OvertimePlaying), roster: false);
        if (!result.Success) return result;
        _awaitingTacticalConfirmation = false;
        _pauseRequestedAt = Server.EngineTime;
        Server.ExecuteCommand("mp_pause_match");
        const string paused = "管理员已请求暂停，CS2 将在安全时点处理。";
        CaorenCupChat.PrintToChatAll(paused);
        return GameModeResult.Ok(paused);
    }
}
