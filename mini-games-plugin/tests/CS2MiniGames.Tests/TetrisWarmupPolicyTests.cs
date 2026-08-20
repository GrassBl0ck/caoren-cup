using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class TetrisWarmupPolicyTests
{
    [Fact]
    public void OnlyExplicitlyInactiveWarmupAllowsStartup()
    {
        Assert.Null(TetrisWarmupPolicy.GetStartRejection(WarmupState.Inactive));
        Assert.Equal(
            "[小游戏] 暖身期间无法开始俄罗斯方块，请在暖身结束后重试。",
            TetrisWarmupPolicy.GetStartRejection(WarmupState.Active));
        Assert.Equal(
            "[小游戏] 暂时无法确认暖身状态，请稍后重试。",
            TetrisWarmupPolicy.GetStartRejection(WarmupState.Unavailable));
    }

    [Fact]
    public void ActiveStateClosesEveryActiveSlot()
    {
        Assert.Equal(
            [3, 7],
            TetrisWarmupPolicy.GetSlotsToClose(WarmupState.Active, [7, 3, 7]));
    }

    [Fact]
    public void UnavailableStateClosesEveryActiveSlot()
    {
        Assert.Equal(
            [3, 7],
            TetrisWarmupPolicy.GetSlotsToClose(WarmupState.Unavailable, [7, 3, 7]));
    }

    [Fact]
    public void InactiveStateDoesNotCloseSessions()
    {
        Assert.Empty(TetrisWarmupPolicy.GetSlotsToClose(WarmupState.Inactive, [3, 7]));
    }
}
