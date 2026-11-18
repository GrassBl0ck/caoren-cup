using CaorenCup.Core;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class GameModePauseTests
{
    [Theory]
    [InlineData(0, 11, 12, 24, 6)]
    [InlineData(1, 26, 27, 24, 6)]
    [InlineData(2, 32, 33, 24, 6)]
    public void TacticalQuotaFollowsTheRosterAcrossHalftime(int overtime, int before, int after, int regulation, int overtimeRounds)
    {
        var firstHalfCt = GameModePause.RosterForSide(true, true, before, regulation, overtime, overtimeRounds);
        var secondHalfT = GameModePause.RosterForSide(false, true, after, regulation, overtime, overtimeRounds);
        var secondHalfCt = GameModePause.RosterForSide(true, true, after, regulation, overtime, overtimeRounds);
        Assert.Equal(firstHalfCt, secondHalfT);
        Assert.NotEqual(firstHalfCt, secondHalfCt);

        var pause = new GameModePause();
        var allowance = overtime == 0 ? 4 : 1;
        for (var i = 0; i < allowance; i++)
        {
            Assert.True(pause.Request(true, overtime, firstHalfCt).Success);
            Assert.True(pause.ConfirmTacticalPause(i));
            Assert.True(pause.Clear());
        }
        Assert.False(pause.Request(true, overtime, secondHalfT).Success);
        Assert.True(pause.Request(true, overtime, secondHalfCt).Success);
        Assert.True(pause.Clear());
        Assert.True(pause.Request(true, overtime + 1, firstHalfCt).Success);
    }

    [Fact]
    public void DeadlineAndAllowanceStartOnlyWhenTheEngineConfirmsThePause()
    {
        var pause = new GameModePause();
        Assert.True(pause.Request(true, 1, true).Success);
        Assert.False(pause.Request(false, 1, true).Success);
        Assert.False(pause.IsDue(500));
        Assert.True(pause.Clear());
        Assert.True(pause.Request(true, 1, true).Success);
        Assert.True(pause.ConfirmTacticalPause(510));
        Assert.False(pause.ConfirmTacticalPause(520));
        Assert.False(pause.IsDue(539.99));
        Assert.True(pause.IsDue(540));
        Assert.True(pause.Clear());
        Assert.False(pause.Request(true, 1, true).Success);
        pause.Reset();
        Assert.True(pause.Request(true, 1, true).Success);
    }

    [Fact]
    public void TechnicalPauseHasNoDeadlineAndDoesNotUseTacticalAllowance()
    {
        var pause = new GameModePause();
        Assert.True(pause.Request(false, 1, true).Success);
        Assert.False(pause.ConfirmTacticalPause(100));
        Assert.False(pause.IsDue(10_000));
        Assert.True(pause.Clear());
        Assert.True(pause.Request(true, 1, true).Success);
    }
}
