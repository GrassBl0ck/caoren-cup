using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class DevelopmentMenuTransparencyTests
{
    [Fact]
    public void Changes_are_per_player_and_reset_when_menu_session_ends()
    {
        var state = new DevelopmentMenuTransparency();
        Assert.True(state.TryApplyClick("admin-a", "dev_transparency_8", true, true, out var percent));
        Assert.Equal(40, percent);
        Assert.Equal(10, state.GetPercent("admin-b"));
        state.Forget("admin-a");
        Assert.Equal(10, state.GetPercent("admin-a"));
    }

    [Theory]
    [InlineData("dev_transparency_21", true, true)]
    [InlineData("dev_transparency_-1", true, true)]
    [InlineData("dev_transparency_ 2", true, true)]
    [InlineData("menu_btn_0", true, true)]
    [InlineData("dev_transparency_2", false, true)]
    [InlineData("dev_transparency_2", true, false)]
    public void Invalid_or_unauthorized_clicks_do_not_change_state(string button, bool hasSession, bool isRoot)
    {
        var state = new DevelopmentMenuTransparency();
        Assert.False(state.TryApplyClick("admin-a", button, hasSession, isRoot, out _));
        Assert.Equal(10, state.GetPercent("admin-a"));
    }
}
