using Caoren;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

/// <summary>异能席位身份键：真人 SteamID 与 bot 名字键的边界。</summary>
public sealed class AbilityIdentityTests
{
    [Theory]
    [InlineData("76561198000000001")]
    [InlineData("bot:Alex")]
    [InlineData("bot:Bot Alex")]
    public void Valid_seat_keys_are_accepted(string key)
    {
        Assert.True(AbilityIdentity.IsValidSeatKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bot:")]
    [InlineData("bot:   ")]
    [InlineData("12345")]
    [InlineData("765611980000000")]  // 少一位
    [InlineData("7656119800000000123")] // 多一位
    public void Invalid_seat_keys_are_rejected(string? key)
    {
        Assert.False(AbilityIdentity.IsValidSeatKey(key));
    }

    [Theory]
    [InlineData("bot:Alex", true)]
    [InlineData("bot:", true)]
    [InlineData("76561198000000001", false)]
    [InlineData(null, false)]
    public void Bot_keys_are_recognized(string? key, bool expected)
    {
        Assert.Equal(expected, AbilityIdentity.IsBotKey(key));
    }
}
