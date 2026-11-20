using CaorenCup.Contracts;
using CaorenCup.Contracts.MatchRuntime;
using CounterStrikeSharp.API;

namespace CaorenCupPlugin;

public sealed partial class CaorenCupPlugin
{
    bool ICaorenCupHostedMatchAdministration.ControlsMatch =>
        _matchRuntimeBridge?.RequiresAuthority == true;

    MatchAdminState ICaorenCupMatchAdministration.ReadMatchAdminState() => ReadHostedMatchAdminState();

    private MatchAdminState ReadHostedMatchAdminState()
    {
        var adapter = _matchRuntimeBridge!;
        var snapshot = adapter.Snapshot;
        var live = snapshot.Stage is MatchStage.FirstHalf or MatchStage.SecondHalf or MatchStage.Overtime;
        var round = live && snapshot.CurrentRoundKey is not null ? snapshot.CompletedRounds + 1 : (int?)null;
        var modeId = adapter.ModeId ?? _runtimeModeId;
        var mode = CaorenCupGameModeRegistry.Find(modeId)?.Descriptor;
        var phase = snapshot.Stage switch
        {
            MatchStage.Staging => "配置就绪",
            MatchStage.Warmup => "热身中",
            MatchStage.ReadyCheck => "等待准备",
            MatchStage.GoingLive => "开赛中",
            MatchStage.Halftime or MatchStage.OvertimeHalftime => "换边",
            MatchStage.Recovering => "恢复中",
            MatchStage.Completed => "已结束",
            MatchStage.Aborted => "已终止",
            _ => "比赛中",
        };
        var backups = adapter.RestorePoints
            .Where(point => round is { } current && point.RoundNumber <= current
                && File.Exists(Path.Combine(Server.GameDirectory, "csgo", point.ValveBackupFile)))
            .Select(point => point.RoundNumber).Order().ToArray();
        return new MatchAdminState(
            new(snapshot.MatchId, snapshot.Revision, snapshot.RuntimeEpoch, round),
            $"模式：{mode?.DisplayName ?? modeId}  ·  {phase}  ·  {snapshot.ScoreA}:{snapshot.ScoreB}",
            [],
            snapshot.Stage == MatchStage.Staging,
            live && snapshot.PauseState is not (MatchPauseState.RestorePending or MatchPauseState.Restoring),
            snapshot.PauseState != MatchPauseState.Running,
            adapter.RequiresAuthority,
            backups);
    }

    GameModeResult ICaorenCupMatchAdministration.ApplyMatchAdminAction(MatchAdminRequest request)
    {
        var current = ReadHostedMatchAdminState();
        if (request.Expected is { } expected && expected != current.Stamp)
            return GameModeResult.Fail("比赛或回合已经变化，请重新选择。");
        if (request.Action == MatchAdminAction.RestoreRound
            && (request.Round is not { } round || !current.RestorableRounds.Contains(round)))
            return GameModeResult.Fail("本场没有可用的目标回合备份。");

        const string reason = "administrator";
        var seconds = _matchRuntimeBridge!.ConfiguredWarmupSeconds;
        var result = request.Action switch
        {
            MatchAdminAction.SelectMode => new(false, "ACTIVE_MATCH", "请先结束当前正式比赛，再选择模式。"),
            MatchAdminAction.Start => ApplyLocalRuntimeAction("begin_warmup", new Dictionary<string, object?> { ["warmupSeconds"] = seconds }),
            MatchAdminAction.Pause => ApplyLocalRuntimeAction("force_pause", new Dictionary<string, object?> { ["reason"] = reason }),
            MatchAdminAction.Resume => ApplyLocalRuntimeAction("force_unpause", new Dictionary<string, object?>()),
            MatchAdminAction.RestoreRound => ApplyLocalRuntimeAction("restore", new Dictionary<string, object?> { ["targetRound"] = request.Round }),
            MatchAdminAction.Stop => ApplyLocalRuntimeAction("abort", new Dictionary<string, object?> { ["reason"] = reason }),
            _ => new(false, "UNKNOWN_ACTION", "无效的比赛操作。"),
        };
        if (!result.Ok) return GameModeResult.Fail(result.Message ?? result.Code);
        DispatchPendingAbilityRestore();
        if (request.Action == MatchAdminAction.Stop) DispatchCaorenAbilityStop("aborted");
        RequestMatchStateHeartbeat();
        return GameModeResult.Ok(result.Message ?? string.Empty);
    }
}
