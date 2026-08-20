using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class FrameSendPolicyTests
{
    [Fact]
    public void UnchangedRevisionNeverResends()
    {
        var policy = new FrameSendPolicy(TimeSpan.FromMilliseconds(100));

        Assert.True(policy.ShouldSend(1, TimeSpan.Zero));
        Assert.False(policy.ShouldSend(1, TimeSpan.FromMilliseconds(100)));
        Assert.False(policy.ShouldSend(1, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ChangedRevisionsCoalesceUntilTheMinimumInterval()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(100),
            CS2MiniGamesPlugin.ActiveSessionMinimumFrameInterval);
        var policy = CS2MiniGamesPlugin.CreateActiveSessionFrameSendPolicy();

        Assert.True(policy.ShouldSend(1, TimeSpan.Zero));
        Assert.False(policy.ShouldSend(2, TimeSpan.FromMilliseconds(40)));
        Assert.False(policy.ShouldSend(3, TimeSpan.FromMilliseconds(99)));
        Assert.True(policy.ShouldSend(3, TimeSpan.FromMilliseconds(100)));
        Assert.False(policy.ShouldSend(3, TimeSpan.FromSeconds(5)));
        Assert.True(policy.ShouldSend(4, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void InstancesKeepIndependentSendState()
    {
        var first = new FrameSendPolicy(TimeSpan.FromMilliseconds(100));
        var second = new FrameSendPolicy(TimeSpan.FromMilliseconds(100));

        Assert.True(first.ShouldSend(1, TimeSpan.Zero));
        Assert.False(first.ShouldSend(1, TimeSpan.FromMilliseconds(100)));
        Assert.True(second.ShouldSend(1, TimeSpan.FromMilliseconds(100)));
    }
}
