using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class LobbyReminderPolicyTests
{
    private const string ValidSteamId = "76561198000000060";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(10_000);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(15);
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    [Fact]
    public void Fresh_state_reminds_real_unregistered_player()
    {
        Assert.True(LobbyReminderPolicy.ShouldRemind(true, ValidSteamId, true, Now, Now, MaxAge, Empty));
    }

    [Fact]
    public void Registered_player_is_not_reminded()
    {
        IReadOnlySet<string> registered = new HashSet<string> { ValidSteamId };

        Assert.False(LobbyReminderPolicy.ShouldRemind(true, ValidSteamId, true, Now, Now, MaxAge, registered));
    }

    [Theory]
    [InlineData(false, "76561198000000060")]
    [InlineData(true, "BOT")]
    [InlineData(true, "0")]
    [InlineData(true, "7656119800000006")]
    public void Invalid_or_non_real_players_are_not_reminded(bool isRealPlayer, string steamId)
    {
        Assert.False(LobbyReminderPolicy.ShouldRemind(isRealPlayer, steamId, true, Now, Now, MaxAge, Empty));
    }

    [Fact]
    public void State_that_never_synchronized_is_not_used()
    {
        Assert.False(LobbyReminderPolicy.ShouldRemind(true, ValidSteamId, false, default, Now, MaxAge, Empty));
    }

    [Fact]
    public void State_at_maximum_age_is_still_valid_but_older_state_is_not()
    {
        Assert.True(LobbyReminderPolicy.ShouldRemind(true, ValidSteamId, true, Now - MaxAge, Now, MaxAge, Empty));
        Assert.False(LobbyReminderPolicy.ShouldRemind(true, ValidSteamId, true, Now - MaxAge - TimeSpan.FromMilliseconds(1), Now, MaxAge, Empty));
    }
}
