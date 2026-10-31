using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class RoundMvpRegressionTests
{
    [Fact]
    public void MvpRelay_IsMarkedBeforeSyntheticEventCanReenterHandler()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "PluginRuntime.cs"));
        var source = File.ReadAllText(sourcePath);

        var roundStart = SliceMethod(source, "private HookResult OnRoundStart", "private HookResult OnRoundEnd");
        var roundMvp = SliceMethod(source, "private HookResult OnRoundMvp", "private HookResult OnItemPickup");
        var guardIndex = roundMvp.IndexOf("if (_mvpPlayed)", StringComparison.Ordinal);
        var markIndex = roundMvp.IndexOf("_mvpPlayed = true;", StringComparison.Ordinal);
        var fireIndex = roundMvp.IndexOf(".FireEvent(false)", StringComparison.Ordinal);

        Assert.Contains("_mvpPlayed = false;", roundStart);
        Assert.True(guardIndex >= 0, "MVP 处理器必须拒绝自己重新触发的事件。");
        Assert.True(markIndex >= 0 && fireIndex >= 0 && markIndex < fireIndex,
            "必须先标记已转发，再触发替代 MVP 事件，避免同步递归。");
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"无法定位方法：{startMarker}");
        return source[start..end];
    }
}
