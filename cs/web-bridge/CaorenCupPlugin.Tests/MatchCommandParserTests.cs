using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class MatchCommandParserTests
{
    [Theory]
    [InlineData(".ready", MatchCommandKind.Ready)]
    [InlineData("!r", MatchCommandKind.Ready)]
    [InlineData("/unready", MatchCommandKind.Unready)]
    [InlineData(".ur", MatchCommandKind.Unready)]
    [InlineData(".notready", MatchCommandKind.Unready)]
    [InlineData(".start", MatchCommandKind.Start)]
    [InlineData(".pause", MatchCommandKind.Pause)]
    [InlineData(".tech", MatchCommandKind.TechnicalPause)]
    [InlineData(".tac", MatchCommandKind.TacticalPause)]
    [InlineData(".unpause", MatchCommandKind.Unpause)]
    [InlineData(".forcepause", MatchCommandKind.ForcePause)]
    [InlineData(".fp", MatchCommandKind.ForcePause)]
    [InlineData(".forceunpause", MatchCommandKind.ForceUnpause)]
    [InlineData(".fup", MatchCommandKind.ForceUnpause)]
    [InlineData(".stop", MatchCommandKind.Stop)]
    [InlineData(".prac", MatchCommandKind.Practice)]
    [InlineData(".exitprac", MatchCommandKind.ExitPractice)]
    public void Legacy_aliases_map_to_structured_commands(string text, MatchCommandKind expected)
    {
        var result = MatchCommandParser.Parse(text);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Command?.Kind);
        Assert.Empty(result.Command?.Arguments ?? []);
    }

    [Fact]
    public void Restore_accepts_exactly_one_positive_round_number()
    {
        var valid = MatchCommandParser.Parse(".restore 17");
        var missing = MatchCommandParser.Parse(".restore");
        var zero = MatchCommandParser.Parse(".restore 0");
        var extra = MatchCommandParser.Parse(".restore 17 now");

        Assert.True(valid.Ok);
        Assert.Equal(MatchCommandKind.Restore, valid.Command?.Kind);
        Assert.Equal(["17"], valid.Command?.Arguments);
        Assert.Equal("INVALID_ARGUMENTS", missing.Code);
        Assert.Equal("INVALID_ARGUMENTS", zero.Code);
        Assert.Equal("INVALID_ARGUMENTS", extra.Code);
    }

    [Fact]
    public void Start_and_restore_support_an_explicit_confirmation_token()
    {
        var start = MatchCommandParser.Parse(".start confirm");
        var restore = MatchCommandParser.Parse(".restore 17 confirm");
        var webStart = MatchCommandParser.Parse("/ccmatch start confirm");
        var webRestore = MatchCommandParser.Parse("/ccmatch restore 17 confirm");

        Assert.True(start.Ok);
        Assert.Equal(["confirm"], start.Command?.Arguments);
        Assert.True(restore.Ok);
        Assert.Equal(["17", "confirm"], restore.Command?.Arguments);
        Assert.True(webStart.Ok);
        Assert.Equal(["start", "confirm"], webStart.Command?.Arguments);
        Assert.True(webRestore.Ok);
        Assert.Equal(["restore", "17", "confirm"], webRestore.Command?.Arguments);
    }

    [Theory]
    [InlineData(".ready now")]
    [InlineData(".start confirm extra")]
    [InlineData(".stop please")]
    public void Simple_commands_reject_unexpected_arguments(string text)
    {
        var result = MatchCommandParser.Parse(text);

        Assert.False(result.Ok);
        Assert.Equal("INVALID_ARGUMENTS", result.Code);
    }

    [Theory]
    [InlineData(".restore 1;quit")]
    [InlineData(".ready\nquit")]
    [InlineData("/ccmatch status\rquit")]
    public void Parser_rejects_command_injection_characters(string text)
    {
        var result = MatchCommandParser.Parse(text);

        Assert.False(result.Ok);
        Assert.Equal("UNSAFE_COMMAND", result.Code);
    }

    [Theory]
    [InlineData("/ccmatch status", "status")]
    [InlineData("/ccmatch start", "start")]
    [InlineData("/ccmatch pause", "pause")]
    [InlineData("/ccmatch abort", "abort")]
    public void Ccmatch_accepts_known_admin_subcommands(string text, string subcommand)
    {
        var result = MatchCommandParser.Parse(text);

        Assert.True(result.Ok);
        Assert.Equal(MatchCommandKind.CcMatch, result.Command?.Kind);
        Assert.Equal(subcommand, result.Command?.Arguments.Single());
    }

    [Fact]
    public void Ccmatch_restore_requires_a_positive_round_number()
    {
        var valid = MatchCommandParser.Parse("/ccmatch restore 9");
        var invalid = MatchCommandParser.Parse("/ccmatch restore nine");

        Assert.True(valid.Ok);
        Assert.Equal(["restore", "9"], valid.Command?.Arguments);
        Assert.Equal("INVALID_ARGUMENTS", invalid.Code);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData(".unknown")]
    [InlineData("/ccmatch rcon")]
    public void Non_match_or_unknown_commands_are_not_claimed(string text)
    {
        var result = MatchCommandParser.Parse(text);

        Assert.False(result.Ok);
        Assert.Equal("UNKNOWN_COMMAND", result.Code);
        Assert.Null(result.Command);
    }
}
