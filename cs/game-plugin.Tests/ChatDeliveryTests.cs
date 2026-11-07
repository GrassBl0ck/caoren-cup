using CaorenCup.Contracts;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class ChatDeliveryTests
{
    [Theory]
    [InlineData(CaorenCupChatAudience.Private)]
    [InlineData(CaorenCupChatAudience.Group)]
    [InlineData(CaorenCupChatAudience.Global)]
    public void Actual_delivery_keeps_the_selected_audience_and_one_prefix(CaorenCupChatAudience audience)
    {
        var received = new List<(string Recipient, string Text)>();
        CaorenCupChat.SendToRecipients(new[] { "目标", "队友", "目标" }, "[草人杯] 第一行\n第二行", audience,
            (recipient, text) => received.Add((recipient, text)));

        Assert.Equal(4, received.Count);
        Assert.Equal(new[] { "目标", "队友" }, received.Select(item => item.Recipient).Distinct());
        var color = audience switch
        {
            CaorenCupChatAudience.Private => ChatColors.Green,
            CaorenCupChatAudience.Group => ChatColors.Yellow,
            _ => ChatColors.Red,
        };
        Assert.All(received, item =>
        {
            Assert.StartsWith($" {color}[草人杯]{ChatColors.Default} ", item.Text);
            Assert.Equal(1, item.Text.Split("[草人杯]").Length - 1);
            Assert.DoesNotContain('\n', item.Text);
        });
        Assert.DoesNotContain(received, item => item.Recipient == "敌方");
    }

    [Fact]
    public void Long_unicode_messages_are_delivered_without_loss_or_broken_runes()
    {
        var content = string.Concat(Enumerable.Repeat("头部缩放😀", 90));
        var delivered = new List<string>();
        CaorenCupChat.Send(content, CaorenCupChatAudience.Global, delivered.Add);
        Assert.True(delivered.Count > 1);
        var prefix = CaorenCupChat.FormatGlobalMessage(string.Empty);
        Assert.All(delivered, line =>
        {
            Assert.InRange(Encoding.UTF8.GetByteCount(line), 1, CaorenCupChat.MaxChatBytes);
            Assert.DoesNotContain('�', line);
            Assert.StartsWith(prefix, line);
        });
        Assert.Equal(content, string.Concat(delivered.Select(line => line[prefix.Length..])));
    }

    [Fact]
    public void Empty_groups_and_blank_lines_do_not_send_messages()
    {
        var delivered = new List<string>();
        CaorenCupChat.SendToRecipients(Array.Empty<int>(), "秘密任务", CaorenCupChatAudience.Group, (_, line) => delivered.Add(line));
        CaorenCupChat.Send(" \r\n ", CaorenCupChatAudience.Global, delivered.Add);
        Assert.Empty(delivered);
    }

    [Fact]
    public void Console_output_has_one_plain_prefix_and_no_chat_color_controls()
    {
        var result = CaorenCupChat.FormatConsoleMessage($"{ChatColors.Red}[草人杯][草人杯] 已恢复{ChatColors.Default}");
        Assert.Equal("[草人杯] 已恢复", result);
    }
}
