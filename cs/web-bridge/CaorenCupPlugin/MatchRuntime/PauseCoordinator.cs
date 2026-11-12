namespace CaorenCupPlugin.MatchRuntime;

public sealed record PauseSnapshot(
    MatchPauseState State,
    RosterSide? RequestingRoster,
    string? Reason,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndsAt,
    bool AdminForced,
    IReadOnlyCollection<RosterSide> UnpauseConfirmations);

public sealed class PauseCoordinator
{
    private static readonly TimeSpan TacticalPauseDuration = TimeSpan.FromSeconds(30);
    private readonly Dictionary<RosterSide, int> _regulationTacticalPauses = new()
    {
        [RosterSide.A] = 0,
        [RosterSide.B] = 0,
    };
    private readonly Dictionary<(int OvertimeIndex, RosterSide Roster), int> _overtimeTacticalPauses = new();
    private readonly HashSet<RosterSide> _unpauseConfirmations = [];
    private MatchPauseState _state = MatchPauseState.Running;
    private RosterSide? _requestingRoster;
    private string? _reason;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _endsAt;
    private bool _adminForced;

    public PauseSnapshot Snapshot => new(
        _state,
        _requestingRoster,
        _reason,
        _startedAt,
        _endsAt,
        _adminForced,
        _unpauseConfirmations.OrderBy(value => value).ToArray());

    public int GetTacticalPausesUsed(RosterSide roster, int overtimeIndex) => overtimeIndex <= 0
        ? _regulationTacticalPauses[roster]
        : _overtimeTacticalPauses.GetValueOrDefault((overtimeIndex, roster));

    public MatchCommandResult RequestTacticalPause(
        RosterSide roster,
        int overtimeIndex,
        DateTimeOffset now,
        bool atSafeBoundary)
    {
        var boundary = ValidatePlayerPauseBoundary(atSafeBoundary);
        if (!boundary.Ok) return boundary;
        var used = GetTacticalPausesUsed(roster, overtimeIndex);
        var limit = overtimeIndex <= 0 ? 4 : 1;
        if (used >= limit)
            return new(false, "TACTICAL_PAUSES_EXHAUSTED", "该队本阶段的战术暂停次数已经用完。");

        if (overtimeIndex <= 0) _regulationTacticalPauses[roster] = used + 1;
        else _overtimeTacticalPauses[(overtimeIndex, roster)] = used + 1;
        Activate(MatchPauseState.TacticalPaused, roster, "tactical", now, now + TacticalPauseDuration, adminForced: false);
        return new(true, "PAUSED");
    }

    public MatchCommandResult RequestTechnicalPause(string reason, DateTimeOffset now, bool atSafeBoundary)
    {
        var boundary = ValidatePlayerPauseBoundary(atSafeBoundary);
        if (!boundary.Ok) return boundary;
        Activate(
            MatchPauseState.TechnicalPaused,
            null,
            string.IsNullOrWhiteSpace(reason) ? "technical" : reason.Trim(),
            now,
            null,
            adminForced: false);
        return new(true, "PAUSED");
    }

    public MatchCommandResult ForcePause(string reason, DateTimeOffset now)
    {
        if (_state != MatchPauseState.Running)
            return new(false, "ALREADY_PAUSED", "比赛已经暂停。");
        Activate(
            MatchPauseState.TechnicalPaused,
            null,
            string.IsNullOrWhiteSpace(reason) ? "administrator" : reason.Trim(),
            now,
            null,
            adminForced: true);
        return new(true, "PAUSED");
    }

    public MatchCommandResult ConfirmUnpause(RosterSide roster)
    {
        if (_state == MatchPauseState.Running)
            return new(false, "NOT_PAUSED", "比赛当前没有暂停。");
        if (_adminForced)
            return new(false, "ADMIN_FORCE_REQUIRED", "管理员强制暂停只能由管理员恢复。");

        _unpauseConfirmations.Add(roster);
        if (_unpauseConfirmations.Count < 2)
            return new(true, "WAITING_FOR_OTHER_ROSTER", "等待另一队确认恢复。");
        ResetActivePause();
        return new(true, "UNPAUSED");
    }

    public MatchCommandResult ForceUnpause()
    {
        if (_state == MatchPauseState.Running)
            return new(true, "ALREADY_RUNNING");
        ResetActivePause();
        return new(true, "UNPAUSED");
    }

    public MatchCommandResult TryExpire(DateTimeOffset now)
    {
        if (_state != MatchPauseState.TacticalPaused || _endsAt is null)
            return new(false, "NOT_EXPIRABLE", "当前暂停没有自动截止时间。");
        if (now < _endsAt.Value)
            return new(false, "NOT_EXPIRED", "战术暂停尚未到期。");
        ResetActivePause();
        return new(true, "UNPAUSED");
    }

    private MatchCommandResult ValidatePlayerPauseBoundary(bool atSafeBoundary)
    {
        if (_state != MatchPauseState.Running)
            return new(false, "ALREADY_PAUSED", "比赛已经暂停。");
        return atSafeBoundary
            ? new(true, "OK")
            : new(false, "UNSAFE_BOUNDARY", "暂停只能在冻结时间等安全节点生效。");
    }

    private void Activate(
        MatchPauseState state,
        RosterSide? requestingRoster,
        string reason,
        DateTimeOffset startedAt,
        DateTimeOffset? endsAt,
        bool adminForced)
    {
        _state = state;
        _requestingRoster = requestingRoster;
        _reason = reason;
        _startedAt = startedAt;
        _endsAt = endsAt;
        _adminForced = adminForced;
        _unpauseConfirmations.Clear();
    }

    private void ResetActivePause()
    {
        _state = MatchPauseState.Running;
        _requestingRoster = null;
        _reason = null;
        _startedAt = null;
        _endsAt = null;
        _adminForced = false;
        _unpauseConfirmations.Clear();
    }
}
