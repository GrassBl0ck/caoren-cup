using CS2MiniGames.Framework;

namespace CS2MiniGames;

internal static class TetrisWarmupPolicy
{
    internal const string WarmupRejection =
        "[小游戏] 暖身期间无法开始俄罗斯方块，请在暖身结束后重试。";
    internal const string UnavailableRejection =
        "[小游戏] 暂时无法确认暖身状态，请稍后重试。";
    internal const string Interrupted =
        "[小游戏] 暖身已开始或状态无法确认，俄罗斯方块已结束。";

    internal static string? GetStartRejection(WarmupState state) => state switch
    {
        WarmupState.Inactive => null,
        WarmupState.Active => WarmupRejection,
        WarmupState.Unavailable => UnavailableRejection,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    internal static IReadOnlyList<int> GetSlotsToClose(
        WarmupState state,
        IEnumerable<int> activeSlots)
    {
        ArgumentNullException.ThrowIfNull(activeSlots);
        return state == WarmupState.Inactive
            ? []
            : activeSlots.Distinct().Order().ToArray();
    }
}
