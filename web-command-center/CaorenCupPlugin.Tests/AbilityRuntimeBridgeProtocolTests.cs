using System.Text.Json;
using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class AbilityRuntimeBridgeProtocolTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("start", "css_ability_runtime_start")]
    [InlineData("stop", "css_ability_runtime_stop")]
    [InlineData("substitute", "css_ability_runtime_substitute")]
    [InlineData("round_control", "css_ability_runtime_round_control")]
    public void Builds_only_fixed_runtime_commands(string action, string expectedName)
    {
        var payload = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["matchId"] = "match-1",
            ["revision"] = 1,
            ["contentDigest"] = Digest,
            ["roundKey"] = action is "start" ? "round-1" : action is "round_control" ? "round-2" : null,
            ["seatId"] = action == "substitute" ? "A1" : null,
            ["steamId"] = action == "substitute" ? "76561198000000999" : null,
            ["outcome"] = action == "round_control" ? "admin_cancelled" : null,
        };
        var command = new PluginCommand
        {
            Id = "runtime-1",
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(payload),
        };

        var built = AbilityRuntimeProtocol.BuildInternalCommand(command);

        Assert.Equal(expectedName, built.Name);
        Assert.DoesNotContain("\n", built.Argument);
    }

    [Fact]
    public void Rejects_unknown_actions_invalid_identity_and_arbitrary_command_text()
    {
        PluginCommand Command(object payload) => new()
        {
            Id = "runtime-invalid",
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(payload),
        };

        Assert.Throws<AbilityRuntimeProtocolException>(() => AbilityRuntimeProtocol.BuildInternalCommand(Command(new
        {
            action = "execute",
            matchId = "match-1",
            revision = 1,
            contentDigest = Digest,
            command = "quit",
        })));
        Assert.Throws<AbilityRuntimeProtocolException>(() => AbilityRuntimeProtocol.BuildInternalCommand(Command(new
        {
            action = "start",
            matchId = "match-1",
            revision = 0,
            contentDigest = "bad",
            roundKey = "round-1",
        })));
    }

    [Theory]
    [InlineData("draw")]
    [InlineData("restarted")]
    [InlineData("admin_cancelled")]
    public void Round_control_accepts_only_non_scoring_outcomes(string outcome)
    {
        var command = new PluginCommand
        {
            Id = "runtime-round",
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(new
            {
                action = "round_control",
                matchId = "match-1",
                revision = 1,
                contentDigest = Digest,
                roundKey = "round-2",
                outcome,
            }),
        };

        Assert.Equal("css_ability_runtime_round_control", AbilityRuntimeProtocol.BuildInternalCommand(command).Name);
    }
}
