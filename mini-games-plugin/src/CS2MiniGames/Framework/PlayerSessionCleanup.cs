namespace CS2MiniGames.Framework;

internal static class PlayerSessionCleanup
{
    internal static void RunOwned<TPlayer>(
        Func<TPlayer?> detachOwner,
        Action<TPlayer> clearHud,
        Action<TPlayer> restoreMovement,
        Action<Exception> reportWarning)
        where TPlayer : class
    {
        ArgumentNullException.ThrowIfNull(detachOwner);
        ArgumentNullException.ThrowIfNull(clearHud);
        ArgumentNullException.ThrowIfNull(restoreMovement);
        ArgumentNullException.ThrowIfNull(reportWarning);

        TPlayer? owner;
        try
        {
            owner = detachOwner();
        }
        catch (Exception error)
        {
            TryReport(error, reportWarning);
            return;
        }

        if (owner is null)
        {
            return;
        }

        TryRun(() => clearHud(owner), reportWarning);
        TryRun(() => restoreMovement(owner), reportWarning);
    }

    internal static void CloseEach(
        IEnumerable<int> slots,
        Action<int> beforeClose,
        Action<int> close,
        Action<int, Exception> reportWarning)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(beforeClose);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(reportWarning);

        foreach (var slot in slots)
        {
            TryRun(
                () => beforeClose(slot),
                error => reportWarning(slot, error));
            TryRun(
                () => close(slot),
                error => reportWarning(slot, error));
        }
    }

    private static void TryRun(Action action, Action<Exception> reportWarning)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            TryReport(error, reportWarning);
        }
    }

    private static void TryReport(
        Exception error,
        Action<Exception> reportWarning)
    {
        try
        {
            reportWarning(error);
        }
        catch
        {
            // Cleanup must remain best-effort even when logging is unavailable.
        }
    }
}
