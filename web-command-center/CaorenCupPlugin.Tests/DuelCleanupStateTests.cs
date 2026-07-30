using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class DuelCleanupStateTests
{
    [Fact]
    public void Restart_round_is_consumed_once()
    {
        var state = new DuelCleanupState();
        state.Begin(DuelControlMode.WebManaged);

        Assert.True(state.TryConsumeRestartRound(out var mode));
        Assert.Equal(DuelControlMode.WebManaged, mode);
        Assert.False(state.TryConsumeRestartRound(out _));
    }

    [Fact]
    public void Duplicate_begin_is_idempotent_for_same_mode()
    {
        var state = new DuelCleanupState();
        state.Begin(DuelControlMode.GameManaged);
        state.Begin(DuelControlMode.GameManaged);

        Assert.True(state.RestartPending);
    }
}
