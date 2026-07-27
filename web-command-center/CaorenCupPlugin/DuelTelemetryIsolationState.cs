namespace CaorenCupPlugin;

internal sealed class DuelTelemetryIsolationState
{
    private bool _cleanupRestartPending;
    private bool _cleanupRoundStartCompleted;
    private bool _safeHeartbeatObserved;
    private bool _hasPendingHeartbeatState;
    private PluginHeartbeatResponse? _pendingHeartbeatState;

    public bool IsActive { get; private set; }

    public string? StaleMatchId { get; private set; }

    public bool HasReleasedHeartbeatState => !IsActive && _hasPendingHeartbeatState;

    public PluginHeartbeatResponse? ReleasedHeartbeatState =>
        HasReleasedHeartbeatState ? _pendingHeartbeatState : null;

    public void Begin(string? staleMatchId)
    {
        IsActive = true;
        StaleMatchId = NormalizeMatchId(staleMatchId);
        _cleanupRestartPending = false;
        _cleanupRoundStartCompleted = false;
        _safeHeartbeatObserved = false;
        ClearReleasedHeartbeatState();
    }

    public void BeginCleanupRestart()
    {
        if (!IsActive) return;
        _cleanupRestartPending = true;
        _cleanupRoundStartCompleted = false;
        _safeHeartbeatObserved = false;
        ClearReleasedHeartbeatState();
    }

    public void CompleteCleanupRoundStart()
    {
        if (!IsActive || !_cleanupRestartPending) return;
        _cleanupRestartPending = false;
        _cleanupRoundStartCompleted = true;
        ReleaseIfSafe();
    }

    public void ObserveHeartbeat(string? matchId)
    {
        if (!IsActive) return;

        ObserveHeartbeatMatchId(matchId);
        ReleaseIfSafe();
    }

    public void ObserveHeartbeatState(PluginHeartbeatResponse? state)
    {
        if (!IsActive) return;

        ObserveHeartbeatMatchId(state?.MatchId);
        if (_safeHeartbeatObserved)
        {
            _pendingHeartbeatState = state;
            _hasPendingHeartbeatState = true;
        }
        else
        {
            ClearReleasedHeartbeatState();
        }

        ReleaseIfSafe();
    }

    public void ClearReleasedHeartbeatState()
    {
        _pendingHeartbeatState = null;
        _hasPendingHeartbeatState = false;
    }

    private void ObserveHeartbeatMatchId(string? matchId)
    {
        var normalizedMatchId = NormalizeMatchId(matchId);
        _safeHeartbeatObserved = normalizedMatchId == null ||
            !string.Equals(normalizedMatchId, StaleMatchId, StringComparison.Ordinal);
    }

    private void ReleaseIfSafe()
    {
        if (!_cleanupRoundStartCompleted || !_safeHeartbeatObserved) return;
        IsActive = false;
        StaleMatchId = null;
    }

    private static string? NormalizeMatchId(string? matchId) =>
        string.IsNullOrWhiteSpace(matchId) ? null : matchId.Trim();
}
