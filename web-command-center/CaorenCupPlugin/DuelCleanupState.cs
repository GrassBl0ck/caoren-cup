namespace CaorenCupPlugin;

internal sealed class DuelCleanupState
{
    public bool RestartPending { get; private set; }

    public DuelControlMode Mode { get; private set; }

    public void Begin(DuelControlMode mode)
    {
        if (mode == DuelControlMode.None)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (RestartPending && Mode != mode)
        {
            throw new InvalidOperationException("Conflicting duel cleanup mode.");
        }

        Mode = mode;
        RestartPending = true;
    }

    public bool TryConsumeRestartRound(out DuelControlMode mode)
    {
        mode = Mode;
        if (!RestartPending) return false;
        RestartPending = false;
        return true;
    }

    public void Reset()
    {
        RestartPending = false;
        Mode = DuelControlMode.None;
    }
}
