using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class BridgeServerCommandPolicyTests
{
    [Theory]
    [InlineData("wp_refresh 76561198000000001 safe", true)]
    [InlineData("wp_refresh 76561198000000001", true)]
    [InlineData("sv_cheats 1", false)]
    public void AllowsOnlyExplicitServerCommands(string command, bool expected)
    {
        Assert.Equal(expected, BridgeServerCommandPolicy.IsAllowed(command));
    }

    [Fact]
    public void CosmeticRefreshDoesNotBroadcastPrivatePlayerChangeToAllPlayers()
    {
        Assert.False(BridgeServerCommandPolicy.ShouldBroadcast("wp_refresh 76561198000000001 safe"));
        Assert.True(BridgeServerCommandPolicy.ShouldBroadcast("mp_warmup_end"));
    }
}
