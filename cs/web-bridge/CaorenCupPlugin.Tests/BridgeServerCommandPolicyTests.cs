using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class BridgeServerCommandPolicyTests
{
    [Theory]
    [InlineData("wp_refresh 76561198000000001 safe", true)]
    [InlineData("wp_refresh 76561198000000001", true)]
    [InlineData("wp_refresh all", false)]
    [InlineData("wp_refresh 76561198000000001 unsafe", false)]
    [InlineData("wp_refresh 76561198000000001 safe; sv_cheats 1", false)]
    [InlineData("wp_refresh 76561198000000001 safe\nsv_cheats 1", false)]
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
