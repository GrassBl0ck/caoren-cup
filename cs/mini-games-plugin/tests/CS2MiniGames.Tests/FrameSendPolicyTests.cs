using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class FrameSendPolicyTests
{
    [Fact]
    public void SendsFirstFrameChangesAndKeepalivesButNotUnchangedTicks()
    {
        var policy = new FrameSendPolicy(TimeSpan.FromMilliseconds(750));

        Assert.True(policy.ShouldSend(revision: 1, TimeSpan.Zero));
        Assert.False(policy.ShouldSend(revision: 1, TimeSpan.FromMilliseconds(749)));
        Assert.True(policy.ShouldSend(revision: 1, TimeSpan.FromMilliseconds(750)));
        Assert.True(policy.ShouldSend(revision: 2, TimeSpan.FromMilliseconds(751)));
        Assert.False(policy.ShouldSend(revision: 2, TimeSpan.FromMilliseconds(1_500)));
        Assert.True(policy.ShouldSend(revision: 2, TimeSpan.FromMilliseconds(1_501)));
    }

    [Fact]
    public void InstancesKeepIndependentSendState()
    {
        var first = new FrameSendPolicy(TimeSpan.FromMilliseconds(750));
        var second = new FrameSendPolicy(TimeSpan.FromMilliseconds(750));

        Assert.True(first.ShouldSend(1, TimeSpan.Zero));
        Assert.False(first.ShouldSend(1, TimeSpan.FromMilliseconds(100)));
        Assert.True(second.ShouldSend(1, TimeSpan.FromMilliseconds(100)));
    }
}
