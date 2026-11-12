using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class RoundPauseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Regulation_allows_four_tactical_pauses_per_roster()
    {
        var pauses = new PauseCoordinator();

        for (var index = 0; index < 4; index++)
        {
            Assert.True(pauses.RequestTacticalPause(RosterSide.A, overtimeIndex: 0, Now.AddMinutes(index), atSafeBoundary: true).Ok);
            Assert.True(pauses.ForceUnpause().Ok);
        }

        var exhausted = pauses.RequestTacticalPause(RosterSide.A, overtimeIndex: 0, Now.AddMinutes(5), atSafeBoundary: true);

        Assert.False(exhausted.Ok);
        Assert.Equal("TACTICAL_PAUSES_EXHAUSTED", exhausted.Code);
        Assert.Equal(4, pauses.GetTacticalPausesUsed(RosterSide.A, overtimeIndex: 0));
        Assert.Equal(0, pauses.GetTacticalPausesUsed(RosterSide.B, overtimeIndex: 0));
    }

    [Fact]
    public void Each_overtime_stage_adds_one_tactical_pause_per_roster()
    {
        var pauses = new PauseCoordinator();

        Assert.True(pauses.RequestTacticalPause(RosterSide.B, overtimeIndex: 1, Now, atSafeBoundary: true).Ok);
        pauses.ForceUnpause();
        Assert.Equal("TACTICAL_PAUSES_EXHAUSTED", pauses.RequestTacticalPause(RosterSide.B, 1, Now.AddMinutes(1), true).Code);
        Assert.True(pauses.RequestTacticalPause(RosterSide.B, overtimeIndex: 2, Now.AddMinutes(2), atSafeBoundary: true).Ok);

        Assert.Equal(1, pauses.GetTacticalPausesUsed(RosterSide.B, overtimeIndex: 1));
        Assert.Equal(1, pauses.GetTacticalPausesUsed(RosterSide.B, overtimeIndex: 2));
    }

    [Fact]
    public void Tactical_pause_has_a_thirty_second_deadline_and_can_expire()
    {
        var pauses = new PauseCoordinator();

        pauses.RequestTacticalPause(RosterSide.A, 0, Now, atSafeBoundary: true);

        Assert.Equal(MatchPauseState.TacticalPaused, pauses.Snapshot.State);
        Assert.Equal(Now.AddSeconds(30), pauses.Snapshot.EndsAt);
        Assert.False(pauses.TryExpire(Now.AddSeconds(29)).Ok);
        Assert.True(pauses.TryExpire(Now.AddSeconds(30)).Ok);
        Assert.Equal(MatchPauseState.Running, pauses.Snapshot.State);
    }

    [Fact]
    public void Technical_pause_has_no_deadline_and_requires_both_rosters_to_unpause()
    {
        var pauses = new PauseCoordinator();
        pauses.RequestTechnicalPause("player disconnected", Now, atSafeBoundary: true);

        var first = pauses.ConfirmUnpause(RosterSide.A);
        var second = pauses.ConfirmUnpause(RosterSide.B);

        Assert.Null(pauses.Snapshot.EndsAt);
        Assert.True(first.Ok);
        Assert.Equal("WAITING_FOR_OTHER_ROSTER", first.Code);
        Assert.True(second.Ok);
        Assert.Equal("UNPAUSED", second.Code);
        Assert.Equal(MatchPauseState.Running, pauses.Snapshot.State);
    }

    [Fact]
    public void Administrator_can_force_pause_and_force_unpause()
    {
        var pauses = new PauseCoordinator();

        Assert.True(pauses.ForcePause("administrator", Now).Ok);
        Assert.Equal(MatchPauseState.TechnicalPaused, pauses.Snapshot.State);
        Assert.True(pauses.Snapshot.AdminForced);
        Assert.True(pauses.ForceUnpause().Ok);
        Assert.Equal(MatchPauseState.Running, pauses.Snapshot.State);
    }

    [Fact]
    public void Player_pause_requests_are_rejected_outside_a_safe_boundary()
    {
        var pauses = new PauseCoordinator();

        var tactical = pauses.RequestTacticalPause(RosterSide.A, 0, Now, atSafeBoundary: false);
        var technical = pauses.RequestTechnicalPause("network", Now, atSafeBoundary: false);

        Assert.Equal("UNSAFE_BOUNDARY", tactical.Code);
        Assert.Equal("UNSAFE_BOUNDARY", technical.Code);
        Assert.Equal(MatchPauseState.Running, pauses.Snapshot.State);
    }

    [Fact]
    public void A_second_pause_cannot_replace_an_active_pause()
    {
        var pauses = new PauseCoordinator();
        pauses.RequestTechnicalPause("network", Now, atSafeBoundary: true);

        var result = pauses.RequestTacticalPause(RosterSide.A, 0, Now, atSafeBoundary: true);

        Assert.False(result.Ok);
        Assert.Equal("ALREADY_PAUSED", result.Code);
        Assert.Equal("network", pauses.Snapshot.Reason);
    }
}
