using CaorenCup;
using CounterStrikeSharp.API.Modules.Utils;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class ChatMessageFormattingTests
{
    [Theory]
    [InlineData("[草人杯] 已开启")]
    [InlineData("[草人杯][草人杯] 已开启")]
    [InlineData("已开启")]
    public void Private_message_has_one_green_leading_tag(string input)
    {
        string result = CaorenCupUtils.FormatPrivateMessage(input);

        Assert.StartsWith($" {ChatColors.Green}[草人杯]{ChatColors.Default} ", result);
        Assert.Equal(1, result.Split("[草人杯]").Length - 1);
        Assert.EndsWith("已开启", result);
    }

    [Fact]
    public void Team_and_global_message_tags_match_their_recipient_scope()
    {
        Assert.StartsWith($" {ChatColors.Yellow}[草人杯]{ChatColors.Default} ",
            CaorenCupUtils.FormatTeamMessage("队伍提示"));
        Assert.StartsWith($" {ChatColors.Red}[草人杯]{ChatColors.Default} ",
            CaorenCupUtils.FormatGlobalMessage("全员提示"));
    }
}
