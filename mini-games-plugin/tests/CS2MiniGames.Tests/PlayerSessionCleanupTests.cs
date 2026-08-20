using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class PlayerSessionCleanupTests
{
    [Fact]
    public void CleansTheDetachedOwnerInsteadOfAReplacementInTheSameSlot()
    {
        var calls = new List<string>();
        var original = new TestPlayer("original");
        var replacement = new TestPlayer("replacement");
        var current = original;

        PlayerSessionCleanup.RunOwned(
            detachOwner: () =>
            {
                var detached = current;
                current = replacement;
                return detached;
            },
            clearHud: player => calls.Add($"clear:{player.Name}"),
            restoreMovement: player => calls.Add($"restore:{player.Name}"),
            _ => calls.Add("warn"));

        Assert.Equal(["clear:original", "restore:original"], calls);
    }

    [Fact]
    public void RestoreStillRunsWhenHudClearFails()
    {
        var calls = new List<string>();

        PlayerSessionCleanup.RunOwned(
            detachOwner: () => new TestPlayer("original"),
            clearHud: _ => throw new InvalidOperationException("clear failed"),
            restoreMovement: player => calls.Add($"restore:{player.Name}"),
            error => calls.Add(error.Message));

        Assert.Equal(["clear failed", "restore:original"], calls);
    }

    [Fact]
    public void RestoreStillRunsWhenWarningReportingFails()
    {
        var calls = new List<string>();

        PlayerSessionCleanup.RunOwned(
            detachOwner: () => new TestPlayer("original"),
            clearHud: _ => throw new InvalidOperationException("clear failed"),
            restoreMovement: player => calls.Add($"restore:{player.Name}"),
            _ => throw new InvalidOperationException("logger failed"));

        Assert.Equal(["restore:original"], calls);
    }

    [Fact]
    public void MissingOwnerDoesNotTouchAnyPlayer()
    {
        var calls = new List<string>();

        PlayerSessionCleanup.RunOwned<TestPlayer>(
            detachOwner: () => null,
            clearHud: _ => calls.Add("clear"),
            restoreMovement: _ => calls.Add("restore"),
            _ => calls.Add("warn"));

        Assert.Empty(calls);
    }

    [Fact]
    public void CloseEachContinuesAfterOnePlayerCleanupFails()
    {
        var calls = new List<string>();

        PlayerSessionCleanup.CloseEach(
            [3, 7],
            beforeClose: slot => calls.Add($"notify:{slot}"),
            close: slot =>
            {
                calls.Add($"close:{slot}");
                if (slot == 3)
                {
                    throw new InvalidOperationException("slot 3 failed");
                }
            },
            reportWarning: (slot, error) => calls.Add($"warn:{slot}:{error.Message}"));

        Assert.Equal(
            [
                "notify:3",
                "close:3",
                "warn:3:slot 3 failed",
                "notify:7",
                "close:7"
            ],
            calls);
    }

    [Fact]
    public void CloseEachStillClosesWhenNotificationFails()
    {
        var calls = new List<string>();

        PlayerSessionCleanup.CloseEach(
            [3],
            beforeClose: _ => throw new InvalidOperationException("notify failed"),
            close: slot => calls.Add($"close:{slot}"),
            reportWarning: (slot, error) => calls.Add($"warn:{slot}:{error.Message}"));

        Assert.Equal(["warn:3:notify failed", "close:3"], calls);
    }

    private sealed record TestPlayer(string Name);
}
