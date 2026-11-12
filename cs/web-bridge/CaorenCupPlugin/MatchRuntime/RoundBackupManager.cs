namespace CaorenCupPlugin.MatchRuntime;

public interface IMatchServerActions
{
    string ReadCvar(string name, string fallback);

    void Execute(string command);

    void Pause();

    void RestoreValveBackup(string fileName);

    void StartDemo(string fileName);

    void StopDemo();
}

public interface IMatchRestoreTarget
{
    void EnterRestoring();

    void RestoreMatchState(MatchRuntimeSnapshot snapshot);

    void RestoreAbility(string checkpointId, long runtimeEpoch);

    void ReapplyRosterAndCvars();

    void EnterTechnicalPause();
}

public sealed record RestorePlan(
    MatchRuntimeSnapshot Before,
    RoundRestorePoint Target,
    long NewRuntimeEpoch);

public sealed record RestorePlanResult(
    bool Ok,
    string Code,
    string Message = "",
    RestorePlan? Plan = null);

public sealed record RestoreCommitResult(bool Ok, string Code, string Message = "");

public sealed class RoundBackupManager
{
    private static readonly TimeSpan StopConfirmationWindow = TimeSpan.FromSeconds(60);
    private readonly IMatchServerActions _server;
    private readonly Func<MatchRuntimeSnapshot> _currentSnapshot;
    private readonly Dictionary<int, RoundRestorePoint> _restorePoints = [];
    private readonly HashSet<RosterSide> _stopConfirmations = [];
    private DateTimeOffset? _stopWindowStartedAt;
    private int? _stopRound;

    public RoundBackupManager(IMatchServerActions server, Func<MatchRuntimeSnapshot> currentSnapshot)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _currentSnapshot = currentSnapshot ?? throw new ArgumentNullException(nameof(currentSnapshot));
    }

    public IReadOnlyCollection<RoundRestorePoint> RestorePoints =>
        _restorePoints.Values.OrderBy(point => point.RoundNumber).ToArray();

    public void CreateRestorePoint(RoundRestorePoint restorePoint)
    {
        if (restorePoint.RoundNumber <= 0) throw new ArgumentOutOfRangeException(nameof(restorePoint));
        _restorePoints[restorePoint.RoundNumber] = restorePoint;
    }

    public void AttachBridgeStats(int roundNumber, IReadOnlyDictionary<string, BridgePlayerStats> stats)
    {
        if (!_restorePoints.TryGetValue(roundNumber, out var restorePoint))
            throw new InvalidOperationException("Cannot attach stats before the round restore point exists.");
        _restorePoints[roundNumber] = restorePoint with
        {
            BridgeStats = stats.ToDictionary(pair => pair.Key, pair => pair.Value with { }, StringComparer.Ordinal),
        };
    }

    public RestorePlanResult RequestStop(RosterSide roster, DateTimeOffset now, int currentRound)
    {
        var expired = _stopWindowStartedAt is null
            || now - _stopWindowStartedAt.Value > StopConfirmationWindow
            || _stopRound != currentRound;
        if (expired)
        {
            _stopConfirmations.Clear();
            _stopWindowStartedAt = now;
            _stopRound = currentRound;
        }

        if (!_stopConfirmations.Add(roster))
            return new(true, "ALREADY_CONFIRMED", "该队队长已经确认回滚。");
        if (_stopConfirmations.Count < 2)
            return new(true, "WAITING_FOR_OTHER_CAPTAIN", "等待另一队队长确认回滚。");

        var plan = PlanRestore(currentRound);
        ClearStopWindow();
        return plan.Ok
            ? plan with { Code = "RESTORE_READY", Message = "双方队长已确认回滚。" }
            : plan;
    }

    public RestorePlanResult PlanRestore(int roundNumber)
    {
        if (!_restorePoints.TryGetValue(roundNumber, out var target))
            return new(false, "RESTORE_POINT_NOT_FOUND", "没有找到指定回合的恢复点。");
        var before = _currentSnapshot();
        return new(true, "PREVIEW_READY", Plan: new RestorePlan(before, target, checked(before.RuntimeEpoch + 1)));
    }

    public RestoreCommitResult CommitRestore(RestorePlan plan, IMatchRestoreTarget target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            _server.Pause();
            target.EnterRestoring();
            _server.RestoreValveBackup(plan.Target.ValveBackupFile);
            var restoredSnapshot = plan.Target.MatchSnapshot with
            {
                RuntimeEpoch = plan.NewRuntimeEpoch,
                PauseState = MatchPauseState.Restoring,
                CurrentRoundKey = null,
            };
            target.RestoreMatchState(restoredSnapshot);
            target.RestoreAbility(plan.Target.AbilityCheckpointId, plan.NewRuntimeEpoch);
            target.ReapplyRosterAndCvars();
            target.EnterTechnicalPause();
            return new(true, "RESTORED");
        }
        catch (Exception ex)
        {
            return new(false, "RESTORE_FAILED", ex.Message);
        }
    }

    private void ClearStopWindow()
    {
        _stopConfirmations.Clear();
        _stopWindowStartedAt = null;
        _stopRound = null;
    }
}
