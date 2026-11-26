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
    [InlineData("mp_warmuptime 15", true)]
    [InlineData("mp_warmup_end extra", false)]
    [InlineData("changelevel de_dust2", true)]
    [InlineData("changelevel de_dust2 demo", false)]
    [InlineData("css_tag ct default", true)]
    [InlineData("css_fov observers 120", false)]
    [InlineData("css_fov ct unknown", false)]
    public void AllowsOnlyExplicitServerCommands(string command, bool expected)
    {
        Assert.Equal(expected, BridgeServerCommandPolicy.IsAllowed(command));
    }

    [Theory]
    [InlineData("css_fov t 120")]
    [InlineData("CSS_DJ all 2 300 true")]
    [InlineData("reset_plu")]
    [InlineData("css_hpcap 1 150")]
    public void Managed_command_results_are_announced_by_the_module_without_a_bridge_duplicate(string command)
    {
        Assert.True(BridgeServerCommandPolicy.IsAllowed(command));
        Assert.False(BridgeServerCommandPolicy.ShouldBroadcast(command));
    }

    [Theory]
    [InlineData("https://cup.example.test")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("http://[::1]:3000")]
    public void AcceptsTlsAndLoopbackBridgeUrls(string url) => Assert.NotNull(BridgeServerCommandPolicy.ParseBackendAddress(url));

    [Theory]
    [InlineData("http://cup.example.test")]
    [InlineData("https://name@cup.example.test")]
    [InlineData("https://cup.example.test/?key=value")]
    public void RejectsRemotePlaintextAndEmbeddedCredentials(string url) =>
        Assert.Throws<InvalidOperationException>(() => BridgeServerCommandPolicy.ParseBackendAddress(url));

    [Fact]
    public void CosmeticRefreshDoesNotBroadcastPrivatePlayerChangeToAllPlayers()
    {
        Assert.False(BridgeServerCommandPolicy.ShouldBroadcast("wp_refresh 76561198000000001 safe"));
        Assert.True(BridgeServerCommandPolicy.ShouldBroadcast("mp_warmup_end"));
    }
}
