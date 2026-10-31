namespace CS2MiniGames;

internal static class TetrisStartupCoordinator
{
    internal static bool TryCommit(
        Func<bool> commit,
        Action freeze,
        Action printControls,
        Action rollback,
        Action<Exception> reportError)
    {
        try
        {
            if (!commit())
            {
                return false;
            }

            freeze();
            printControls();
            return true;
        }
        catch (Exception error)
        {
            try
            {
                rollback();
            }
            catch (Exception rollbackError)
            {
                error = new AggregateException(error, rollbackError);
            }

            reportError(error);
            return false;
        }
    }
}

internal static class SteamIdentityResolver
{
    internal static ulong? ResolveStableSteamId(ulong? authorized, ulong fallback)
    {
        if (authorized is > 0)
        {
            return authorized;
        }

        return fallback > 0 ? fallback : null;
    }
}
