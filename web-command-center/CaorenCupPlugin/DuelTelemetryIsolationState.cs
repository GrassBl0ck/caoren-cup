namespace CaorenCupPlugin;

internal sealed class DuelTelemetryIsolationState
{
    private readonly HashSet<string> _staleMatchIds = new(StringComparer.Ordinal);
    private bool _cleanupRestartPending;
    private bool _cleanupRoundStartCompleted;
    private bool _safeHeartbeatObserved;
    private bool _cvarRestoreReady = true;
    private bool _hasPendingHeartbeatState;
    private PluginHeartbeatResponse? _pendingHeartbeatState;

    public bool IsActive { get; private set; }

    public string? StaleMatchId { get; private set; }

    public bool CleanupRestartPending => _cleanupRestartPending;

    public bool HasReleasedHeartbeatState => !IsActive && _hasPendingHeartbeatState;

    public PluginHeartbeatResponse? ReleasedHeartbeatState =>
        HasReleasedHeartbeatState ? _pendingHeartbeatState : null;

    public void Begin(string? staleMatchId)
    {
        IsActive = true;
        var normalizedStaleMatchId = NormalizeMatchId(staleMatchId);
        if (normalizedStaleMatchId != null)
        {
            _staleMatchIds.Add(normalizedStaleMatchId);
            StaleMatchId = normalizedStaleMatchId;
        }
        _cleanupRestartPending = false;
        _cleanupRoundStartCompleted = false;
        _safeHeartbeatObserved = false;
        _cvarRestoreReady = true;
        ClearReleasedHeartbeatState();
    }

    public void BeginCleanupRestart()
    {
        if (!IsActive) return;
        _cleanupRestartPending = true;
        _cleanupRoundStartCompleted = false;
        _safeHeartbeatObserved = false;
        _cvarRestoreReady = true;
        ClearReleasedHeartbeatState();
    }

    public void CompleteCleanupRoundStart()
    {
        if (!IsActive || !_cleanupRestartPending) return;
        _cleanupRestartPending = false;
        _cleanupRoundStartCompleted = true;
        ReleaseIfSafe();
    }

    public void UpdateCvarRestoreReady(bool isReady)
    {
        _cvarRestoreReady = isReady;
        ReleaseIfSafe();
    }

    public void ObserveHeartbeat(string? matchId)
    {
        if (!IsActive) return;

        ObserveHeartbeatMatchId(matchId);
        ReleaseIfSafe();
    }

    public HeartbeatResponseDisposition ObserveHeartbeatState(PluginHeartbeatResponse? state)
    {
        if (!IsActive)
        {
            return IsStaleMatchId(state?.MatchId)
                ? HeartbeatResponseDisposition.Stale
                : HeartbeatResponseDisposition.Ready;
        }

        if (IsStaleMatchId(state?.MatchId))
        {
            return HeartbeatResponseDisposition.Stale;
        }

        _safeHeartbeatObserved = true;
        _pendingHeartbeatState = state;
        _hasPendingHeartbeatState = true;

        ReleaseIfSafe();
        return IsActive
            ? HeartbeatResponseDisposition.Deferred
            : HeartbeatResponseDisposition.Ready;
    }

    public void ClearReleasedHeartbeatState()
    {
        _pendingHeartbeatState = null;
        _hasPendingHeartbeatState = false;
    }

    private void ObserveHeartbeatMatchId(string? matchId)
    {
        if (!IsStaleMatchId(matchId)) _safeHeartbeatObserved = true;
    }

    public bool IsStaleMatchId(string? matchId)
    {
        var normalizedMatchId = NormalizeMatchId(matchId);
        return normalizedMatchId != null && _staleMatchIds.Contains(normalizedMatchId);
    }

    private void ReleaseIfSafe()
    {
        if (!_cleanupRoundStartCompleted || !_safeHeartbeatObserved || !_cvarRestoreReady) return;
        IsActive = false;
    }

    private static string? NormalizeMatchId(string? matchId) =>
        string.IsNullOrWhiteSpace(matchId) ? null : matchId.Trim();
}
