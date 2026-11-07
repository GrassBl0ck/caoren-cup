using System.Runtime.CompilerServices;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

// 原生宿主不可在单元测试中启动。这里只补充校验实际功能接入；传输和模式流程另有执行测试。
public sealed class MessageFeatureWiringTests
{
    private static string Source(string path, [CallerFilePath] string test = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(test)!, "../game-plugin", path)));

    [Theory]
    [InlineData("Features/FriendsVineFeature.cs", "CaorenCupUtils.PrintToChatAll(FormattableString.Invariant(")]
    [InlineData("Features/DoubleJumpFeature.cs", "CaorenCupUtils.PrintToChatAll(reply)")]
    [InlineData("Features/HeadSizeFeature.cs", "if (result.Success) CaorenCupUtils.PrintToChatAll(message)")]
    [InlineData("Features/MovementRulesFeature.cs", "Announce(\"Move 已恢复默认。\")")]
    [InlineData("Features/LoadoutFeature.cs", "Announce(\"Loadout 已恢复默认。\")")]
    [InlineData("Features/ModifierFeature.cs", "Announce(\"Modifier 已恢复默认。\")")]
    public void Management_changes_reach_the_public_sender(string path, string call) => Assert.Contains(call, Source(path));

    [Fact]
    public void Role_changes_keep_private_errors_and_public_success_with_display_names()
    {
        var source = Source("PluginSplit/CaorenCupGamemode_Ability/CaorenCupAbilityPlugin.cs");
        Assert.Contains("definition.Name : abilityId", source);
        Assert.Contains("CaorenCupChat.PrintToChatAll($\"{target.PlayerName} 的异能职业已设为 {roleName}", source);
        Assert.Contains("if (removed) CaorenCupChat.PrintToChatAll", source);
        Assert.Contains("if (count > 0) CaorenCupChat.PrintToChatAll", source);
        Assert.Contains("将在下一回合开始时生效", source);
        Assert.Contains("Reply(player, $\"{target.PlayerName} 当前没有异能职业分配", source);
    }

    [Fact]
    public void Vote_result_is_one_broadcast_outside_the_recipient_loop()
    {
        var source = Source("PluginSplit/CaorenCupInGameMenu/VotingFeature.cs");
        var start = source.IndexOf("private void ProcessVoteResult()", StringComparison.Ordinal);
        var end = source.IndexOf("private void RaiseVoteCompleted", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Equal(1, method.Split("CaorenCupChat.PrintToChatAll(").Length - 1);
        Assert.DoesNotContain("Chat(player.SteamID", method);
        Assert.True(method.IndexOf("CaorenCupChat.PrintToChatAll", StringComparison.Ordinal) > method.IndexOf("RenderVoteNotice(player, true);\n        }", StringComparison.Ordinal));
    }
}
