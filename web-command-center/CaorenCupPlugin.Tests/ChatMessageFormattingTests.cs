using BridgePlugin = CaorenCupPlugin.CaorenCupPlugin;
using CounterStrikeSharp.API.Modules.Utils;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class ChatMessageFormattingTests
{
    [Fact]
    public void Private_message_keeps_a_single_green_leading_tag()
    {
        string message = BridgePlugin.FormatPrivateChatMessage("[草人杯] [草人杯] 正在生成登录码");

        Assert.StartsWith($" {ChatColors.Green}[草人杯]{ChatColors.Default} ", message);
        Assert.Equal(1, message.Split("[草人杯]").Length - 1);
        Assert.EndsWith("正在生成登录码", message);
    }

    [Fact]
    public void Global_message_uses_red_tag()
    {
        string message = BridgePlugin.FormatGlobalChatMessage("[草人杯] 全员公告");

        Assert.StartsWith($" {ChatColors.Red}[草人杯]{ChatColors.Default} ", message);
        Assert.Equal(1, message.Split("[草人杯]").Length - 1);
    }
}
