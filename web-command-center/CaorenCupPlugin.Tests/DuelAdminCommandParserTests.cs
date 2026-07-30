using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class DuelAdminCommandParserTests
{
    [Theory]
    [InlineData("help", DuelAdminCommandKind.Help)]
    [InlineData("status", DuelAdminCommandKind.Status)]
    [InlineData("start", DuelAdminCommandKind.Start)]
    [InlineData("start confirm", DuelAdminCommandKind.StartConfirm)]
    [InlineData("stop confirm", DuelAdminCommandKind.StopConfirm)]
    [InlineData("pause", DuelAdminCommandKind.Pause)]
    [InlineData("resume", DuelAdminCommandKind.Resume)]
    [InlineData("maps", DuelAdminCommandKind.Maps)]
    public void Parses_fixed_commands(string raw, DuelAdminCommandKind expected)
    {
        Assert.Equal(expected, DuelAdminCommandParser.Parse(raw.Split(' ')).Kind);
    }

    [Fact]
    public void Parses_rounds_time_utility_and_map()
    {
        Assert.Equal((8, 16, 12), DuelAdminCommandParser.Parse(["rounds", "8", "16", "12"]).Rounds);
        Assert.Equal(1.25, DuelAdminCommandParser.Parse(["time", "1.25"]).RoundTimeMinutes);
        Assert.Equal("random2", DuelAdminCommandParser.Parse(["utility", "random2"]).Value);
        Assert.Equal("3250543760", DuelAdminCommandParser.Parse(["map", "3250543760"]).Value);
    }

    [Fact]
    public void Invalid_syntax_returns_chinese_error()
    {
        var result = DuelAdminCommandParser.Parse(["rounds", "8", "x", "12"]);

        Assert.Equal(DuelAdminCommandKind.Invalid, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
