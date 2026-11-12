using System.Text.Json;
using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class AuthoritativeRoundTelemetryTests
{
    private static JsonSerializerOptions Options => global::CaorenCupPlugin.CaorenCupPlugin.CreatePluginJsonOptions();

    [Fact]
    public void Shared_web_fixture_matches_actual_round_and_raw_combat_wire_contract()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "authoritative-round-telemetry.json")));
        var root = fixture.RootElement;
        var runtime = LiveRuntime();
        var target = runtime.Snapshot;
        var startedFixture = root.GetProperty("started");
        var completedFixture = root.GetProperty("completed");
        var key = runtime.OpenRound(startedFixture.GetProperty("players")).RoundKey!;
        var damage = MatchRuntimeTelemetry.AttachRoundIdentity(root.GetProperty("damage"), runtime.Snapshot, Options);
        var death = MatchRuntimeTelemetry.AttachRoundIdentity(root.GetProperty("death"), runtime.Snapshot, Options);
        Assert.NotEqual(damage.GetProperty("eventId").GetString(), death.GetProperty("eventId").GetString());
        Assert.True(death.GetProperty("eventSequence").GetInt64() > damage.GetProperty("eventSequence").GetInt64());
        foreach (var raw in new[] { damage, death })
        {
            Assert.True(Guid.TryParseExact(raw.GetProperty("eventId").GetString(), "N", out _));
            var wireCopy = JsonSerializer.SerializeToElement(raw, Options);
            Assert.Equal(raw.GetProperty("eventId").GetString(), wireCopy.GetProperty("eventId").GetString());
            Assert.Equal(raw.GetProperty("eventSequence").GetInt64(), wireCopy.GetProperty("eventSequence").GetInt64());
            Assert.True(raw.GetProperty("eventSequence").GetInt64() > 0);
            Assert.Equal(key, raw.GetProperty("roundKey").GetString());
            Assert.Equal(1, raw.GetProperty("roundNumber").GetInt32());
            Assert.Equal(1, raw.GetProperty("runtimeEpoch").GetInt64());
        }
        Assert.True(runtime.CompleteRound(key, RosterSide.A, completedFixture.GetProperty("players")).Ok);
        var started = Assert.Single(runtime.Events, item => item.Type == "round_started") with { OccurredAtUnixMs = 1000 };
        var completed = Assert.Single(runtime.Events, item => item.Type == "round_completed") with { OccurredAtUnixMs = 2000 };
        Assert.True(JsonElement.DeepEquals(startedFixture, JsonSerializer.SerializeToElement(started, Options)));
        Assert.True(JsonElement.DeepEquals(completedFixture, JsonSerializer.SerializeToElement(completed, Options)));
        Assert.False(runtime.CompleteRound(key, RosterSide.A).Ok);
        var closed = MatchRuntimeTelemetry.AttachRoundIdentity(root.GetProperty("closedRoundDamage"), runtime.Snapshot, Options);
        Assert.True(closed.GetProperty("eventSequence").GetInt64() > death.GetProperty("eventSequence").GetInt64());
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("roundKey").ValueKind);
        Assert.True(runtime.Restore(target with { RuntimeEpoch = 2 }).Ok);
        runtime.OpenRound(root.GetProperty("restoredStarted").GetProperty("players"));
        var restored = Assert.Single(runtime.Events, item => item.Type == "round_started") with { OccurredAtUnixMs = 3000 };
        Assert.True(JsonElement.DeepEquals(root.GetProperty("restoredStarted"), JsonSerializer.SerializeToElement(restored, Options)));
    }

    [Fact]
    public void Round_events_capture_immutable_players_number_side_winner_and_time_before_heartbeat()
    {
        var runtime = LiveRuntime();
        string key;
        using (var start = JsonDocument.Parse("[{\"steamId\":\"76561198000000001\",\"team\":\"CT\",\"isAlive\":true,\"health\":100,\"maxHealth\":100}]"))
            key = runtime.OpenRound(start.RootElement).RoundKey!;
        using (var end = JsonDocument.Parse("[{\"steamId\":\"76561198000000001\",\"team\":\"CT\",\"isAlive\":false,\"health\":0,\"maxHealth\":100}]"))
            Assert.True(runtime.CompleteRound(key, RosterSide.B, end.RootElement).Ok);
        Assert.False(runtime.CompleteRound(key, RosterSide.B).Ok);
        var started = Assert.Single(runtime.Events, item => item.Type == "round_started");
        var completed = Assert.Single(runtime.Events, item => item.Type == "round_completed");
        Assert.Equal(key, completed.RoundKey);
        Assert.Equal(1, started.RoundNumber);
        Assert.Equal(1, completed.RoundNumber);
        Assert.Equal(EngineSide.CounterTerrorist, started.TeamAEngineSide);
        Assert.Equal(EngineSide.Terrorist, completed.WinningEngineSide);
        Assert.True(started.Players!.Value[0].GetProperty("isAlive").GetBoolean());
        Assert.False(completed.Players!.Value[0].GetProperty("isAlive").GetBoolean());
        Assert.True(completed.OccurredAtUnixMs >= started.OccurredAtUnixMs);
        Assert.True(started.OccurredAtUnixMs > 0);
        var contract = JsonSerializer.SerializeToElement(completed, Options);
        Assert.Equal("round_completed", contract.GetProperty("type").GetString());
        Assert.Equal("terrorist", contract.GetProperty("winningEngineSide").GetString());
        Assert.Equal("counterTerrorist", contract.GetProperty("teamAEngineSide").GetString());
        Assert.Equal(1, contract.GetProperty("roundNumber").GetInt32());
        Assert.Equal(0, contract.GetProperty("players")[0].GetProperty("health").GetInt32());
        Assert.True(contract.GetProperty("occurredAtUnixMs").GetInt64() > 0);
    }

    [Fact]
    public void Delayed_round_event_keeps_pre_swap_identity_and_restore_reuses_number_with_new_key()
    {
        var runtime = LiveRuntime();
        for (var round = 0; round < 12; round++)
            Assert.True(runtime.CompleteRound(runtime.OpenRound().RoundKey!, round % 2 == 0 ? RosterSide.A : RosterSide.B).Ok);
        var beforeSwap = runtime.Events.Last(item => item.Type == "round_completed");
        Assert.Equal(12, beforeSwap.RoundNumber);
        Assert.Equal(EngineSide.Terrorist, beforeSwap.WinningEngineSide);
        Assert.True(runtime.ConfirmSideSwap().Ok);
        var restoreTarget = runtime.Snapshot;
        var key = runtime.OpenRound().RoundKey!;
        Assert.True(runtime.CompleteRound(key, RosterSide.A).Ok);
        var delayed = runtime.Events.Last(item => item.Type == "round_completed");
        Assert.Equal(13, delayed.RoundNumber);
        Assert.Equal(EngineSide.Terrorist, delayed.TeamAEngineSide);
        Assert.Equal(EngineSide.Terrorist, delayed.WinningEngineSide);
        Assert.True(runtime.Restore(restoreTarget with { RuntimeEpoch = 2 }).Ok);
        var replayKey = runtime.OpenRound().RoundKey!;
        Assert.NotEqual(key, replayKey);
        Assert.True(runtime.CompleteRound(replayKey, RosterSide.B).Ok);
        Assert.False(runtime.CompleteRound(key, RosterSide.A).Ok);
        var replayed = Assert.Single(runtime.Events, item => item.Type == "round_completed");
        Assert.Equal(13, replayed.RoundNumber);
        Assert.Equal(2, replayed.RuntimeEpoch);
        Assert.Equal(EngineSide.CounterTerrorist, replayed.WinningEngineSide);
        Assert.Equal(1, delayed.RuntimeEpoch);
        Assert.Equal(EngineSide.Terrorist, delayed.WinningEngineSide);
        Assert.Equal(EngineSide.CounterTerrorist, beforeSwap.TeamAEngineSide);
    }

    [Fact]
    public void Raw_combat_payload_keeps_captured_round_key_number_epoch_after_runtime_advances()
    {
        var runtime = LiveRuntime();
        var target = runtime.Snapshot;
        var key = runtime.OpenRound().RoundKey!;
        var combat = MatchRuntimeTelemetry.AttachRoundIdentity(new { round = 999, damage = 25 }, runtime.Snapshot, Options);
        Assert.True(runtime.CompleteRound(key, RosterSide.A).Ok);
        Assert.True(runtime.Restore(target with { RuntimeEpoch = 2 }).Ok);
        var paused = MatchRuntimeTelemetry.AttachRoundIdentity(new { round = 999 }, runtime.Snapshot, Options);
        var newKey = runtime.OpenRound().RoundKey!;
        Assert.Equal(key, combat.GetProperty("roundKey").GetString());
        Assert.Equal(1, combat.GetProperty("round").GetInt32());
        Assert.Equal(1, combat.GetProperty("roundNumber").GetInt32());
        Assert.Equal(1, combat.GetProperty("runtimeEpoch").GetInt64());
        Assert.Equal(25, combat.GetProperty("damage").GetInt32());
        Assert.Equal(JsonValueKind.Null, paused.GetProperty("roundKey").ValueKind);
        Assert.Equal(2, paused.GetProperty("runtimeEpoch").GetInt64());
        Assert.NotEqual(key, newKey);
    }

    private static CompetitiveMatchRuntime LiveRuntime()
    {
        var runtime = new CompetitiveMatchRuntime();
        Assert.True(runtime.Load(new MatchDefinition("telemetry", 1, "de_mirage", "A", "B", EngineSide.CounterTerrorist,
            [new("a", RosterSide.A, "76561198000000001", "A", true), new("b", RosterSide.B, "76561198000000002", "B", true)])).Ok);
        Assert.True(runtime.BeginWarmup().Ok);
        Assert.True(runtime.BeginReadyCheck().Ok);
        Assert.True(runtime.BeginGoingLive().Ok);
        Assert.True(runtime.ConfirmLive().Ok);
        return runtime;
    }
}
