using System.Text.Json;
using CaorenCupPlugin;
using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class AbilityRuntimeBridgeProtocolTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("start", "css_ability_runtime_start")]
    [InlineData("round_start", "css_ability_runtime_round_start")]
    [InlineData("stop", "css_ability_runtime_stop")]
    [InlineData("substitute", "css_ability_runtime_substitute")]
    [InlineData("round_control", "css_ability_runtime_round_control")]
    [InlineData("restore", "css_ability_runtime_restore")]
    public void Builds_only_fixed_runtime_commands(string action, string expectedName)
    {
        var payload = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["matchId"] = "match-1",
            ["revision"] = 1,
            ["contentDigest"] = Digest,
            ["runtimeEpoch"] = 4,
            ["roundKey"] = action is "start" ? "round-1" : action is "round_start" ? "round-2" : action is "round_control" ? "round-2" : action is "restore" ? "round-3" : null,
            ["seatId"] = action == "substitute" ? "A1" : null,
            ["steamId"] = action == "substitute" ? "76561198000000999" : null,
            ["outcome"] = action == "round_control" ? "admin_cancelled" : null,
            ["checkpointId"] = action == "restore" ? "round-3" : null,
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
            runtimeEpoch = 4,
            command = "quit",
        })));
        Assert.Throws<AbilityRuntimeProtocolException>(() => AbilityRuntimeProtocol.BuildInternalCommand(Command(new
        {
            action = "start",
            matchId = "match-1",
            revision = 0,
            contentDigest = "bad",
            runtimeEpoch = 0,
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
                runtimeEpoch = 4,
                roundKey = "round-2",
                outcome,
            }),
        };

        Assert.Equal("css_ability_runtime_round_control", AbilityRuntimeProtocol.BuildInternalCommand(command).Name);
    }

    [Fact]
    public void Restore_requires_positive_epoch_round_key_and_safe_checkpoint_id()
    {
        PluginCommand Command(long epoch, string checkpointId) => new()
        {
            Type = AbilityRuntimeProtocol.WebCommandType,
            Payload = JsonSerializer.SerializeToElement(new
            {
                action = "restore",
                matchId = "match-1",
                revision = 1,
                contentDigest = Digest,
                runtimeEpoch = epoch,
                roundKey = "round-4",
                checkpointId,
            }),
        };

        Assert.Equal(
            "css_ability_runtime_restore",
            AbilityRuntimeProtocol.BuildInternalCommand(Command(5, "round-4")).Name);
        Assert.Throws<AbilityRuntimeProtocolException>(() => AbilityRuntimeProtocol.BuildInternalCommand(Command(0, "round-4")));
        Assert.Throws<AbilityRuntimeProtocolException>(() => AbilityRuntimeProtocol.BuildInternalCommand(Command(5, "../runtime-state.json")));
    }

    [Fact]
    public void Authoritative_match_round_builds_local_start_then_round_start_commands()
    {
        var confirmed = new AbilityRuntimeProtocol.AbilityRuntimeEnvelope(
            "start",
            "match-1",
            1,
            Digest,
            1,
            "placeholder");
        var snapshot = new MatchRuntimeSnapshot(
            "match-1",
            1,
            7,
            MatchStage.FirstHalf,
            MatchPauseState.Running,
            0,
            0,
            0,
            0,
            0,
            EngineSide.CounterTerrorist,
            false,
            "match-1:7:round-3",
            9);

        var first = AbilityRuntimeProtocol.BuildAuthoritativeRoundCommand(confirmed, snapshot, firstRound: true);
        var later = AbilityRuntimeProtocol.BuildAuthoritativeRoundCommand(confirmed, snapshot, firstRound: false);
        var decoded = AbilitySyncProtocol.Decode<AbilityRuntimeProtocol.AbilityRuntimeEnvelope>(later.Argument);

        Assert.Equal(AbilityRuntimeProtocol.StartCommandName, first.Name);
        Assert.Equal(AbilityRuntimeProtocol.RoundStartCommandName, later.Name);
        Assert.Equal(7, decoded.RuntimeEpoch);
        Assert.Equal("match-1:7:round-3", decoded.RoundKey);
    }

    [Fact]
    public void Ability_ready_uses_authoritative_restart_only_for_matching_caoren_live_round()
    {
        var confirmed = new AbilityRuntimeProtocol.AbilityRuntimeEnvelope(
            "start", "match-1", 1, Digest, 1, "placeholder");
        var snapshot = new MatchRuntimeSnapshot(
            "match-1", 1, 7, MatchStage.SecondHalf, MatchPauseState.Running,
            8, 7, 15, 0, 0, EngineSide.Terrorist, false, "match-1:7:round-16", 20);

        Assert.True(AbilityRuntimeProtocol.ShouldDispatchAuthoritativeReady(confirmed, snapshot, "caoren"));
        Assert.False(AbilityRuntimeProtocol.ShouldDispatchAuthoritativeReady(confirmed, snapshot, "matchzy"));
        Assert.False(AbilityRuntimeProtocol.ShouldDispatchAuthoritativeReady(
            confirmed,
            snapshot with { CurrentRoundKey = null },
            "caoren"));
        Assert.False(AbilityRuntimeProtocol.ShouldDispatchAuthoritativeReady(
            confirmed with { MatchId = "old-match" },
            snapshot,
            "caoren"));
    }
}
