using CaorenCup.Gamemodes.Rush;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class RushFeatureTests
{
    [Fact]
    public void Start_commands_sleep_matchzy_before_switching_to_official_rush_mode()
    {
        Assert.Equal(
            new[]
            {
                "css_reset_plu",
                "css_sleep",
                "game_type 0",
                "game_mode 6",
                "changelevel rush_001",
            },
            RushModePlan.StartCommands);
    }

    [Fact]
    public void Post_map_start_reapplies_official_rush_config_after_matchzy_sleep()
    {
        Assert.Equal(2.0f, RushModePlan.ReapplyDelaySeconds);
        Assert.Equal(new[] { "exec gamemode_rush.cfg" }, RushModePlan.PostMapStartCommands);
    }

    [Fact]
    public void New_match_restores_official_round_limit_after_early_castle_win()
    {
        Assert.Equal(new[] { "mp_maxrounds 15" }, RushModePlan.NewMatchCommands);
    }

    [Theory]
    [InlineData("rush_001")]
    [InlineData("RUSH_001")]
    [InlineData(" rush_001 ")]
    public void Official_rush_map_is_recognized(string mapName)
    {
        Assert.True(RushModePlan.IsRushMap(mapName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de_ancient")]
    [InlineData("aim_rush")]
    public void Other_maps_are_not_recognized_as_official_rush(string? mapName)
    {
        Assert.False(RushModePlan.IsRushMap(mapName));
    }
}
