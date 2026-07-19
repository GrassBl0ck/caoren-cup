namespace CS2MiniGames.Tests;

public sealed class TetrisStartupCoordinatorTests
{
    [Fact]
    public void FreezeFailureRollsBackAndReportsTheError()
    {
        var error = new InvalidOperationException("freeze failed");
        var calls = new List<string>();
        Exception? reported = null;

        var committed = TetrisStartupCoordinator.TryCommit(
            commit: () =>
            {
                calls.Add("commit");
                return true;
            },
            freeze: () =>
            {
                calls.Add("freeze");
                throw error;
            },
            printControls: () => calls.Add("print"),
            rollback: () => calls.Add("rollback"),
            reportError: caught =>
            {
                calls.Add("log");
                reported = caught;
            });

        Assert.False(committed);
        Assert.Equal(["commit", "freeze", "rollback", "log"], calls);
        Assert.Same(error, reported);
    }

    [Fact]
    public void PrintFailureRollsBackAndRestoresAfterFreeze()
    {
        var error = new InvalidOperationException("print failed");
        var calls = new List<string>();
        var restoreCount = 0;
        Exception? reported = null;

        var committed = TetrisStartupCoordinator.TryCommit(
            commit: () =>
            {
                calls.Add("commit");
                return true;
            },
            freeze: () => calls.Add("freeze"),
            printControls: () =>
            {
                calls.Add("print");
                throw error;
            },
            rollback: () =>
            {
                calls.Add("rollback");
                restoreCount++;
            },
            reportError: caught =>
            {
                calls.Add("log");
                reported = caught;
            });

        Assert.False(committed);
        Assert.Equal(["commit", "freeze", "print", "rollback", "log"], calls);
        Assert.Equal(1, restoreCount);
        Assert.Same(error, reported);
    }

    [Fact]
    public void SuccessfulStartupCommitsAndRunsEachStepExactlyOnce()
    {
        var commitCount = 0;
        var freezeCount = 0;
        var printCount = 0;
        var rollbackCount = 0;
        var reportCount = 0;

        var committed = TetrisStartupCoordinator.TryCommit(
            commit: () =>
            {
                commitCount++;
                return true;
            },
            freeze: () => freezeCount++,
            printControls: () => printCount++,
            rollback: () => rollbackCount++,
            reportError: _ => reportCount++);

        Assert.True(committed);
        Assert.Equal(1, commitCount);
        Assert.Equal(1, freezeCount);
        Assert.Equal(1, printCount);
        Assert.Equal(0, rollbackCount);
        Assert.Equal(0, reportCount);
    }

    [Fact]
    public void ResolveStableSteamIdPrefersANonZeroAuthorizedId()
    {
        Assert.Equal(
            76561198000000001UL,
            SteamIdentityResolver.ResolveStableSteamId(
                authorized: 76561198000000001UL,
                fallback: 76561198000000002UL));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0UL)]
    public void ResolveStableSteamIdUsesANonZeroFallbackWhenAuthorizedIsUnavailable(
        ulong? authorized)
    {
        Assert.Equal(
            76561198000000002UL,
            SteamIdentityResolver.ResolveStableSteamId(
                authorized,
                fallback: 76561198000000002UL));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0UL)]
    public void ResolveStableSteamIdReturnsNullWhenNoNonZeroIdExists(ulong? authorized)
    {
        Assert.Null(SteamIdentityResolver.ResolveStableSteamId(authorized, fallback: 0));
    }
}
