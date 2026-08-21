using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRuntimeStateTests
{
    private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Confirmed_config_arms_runtime_and_identity_must_match_before_start()
    {
        var runtime = CreateRuntime();

        var activation = runtime.ApplyConfirmed(Config());

        Assert.True(activation.Activated);
        Assert.Equal(AbilityRuntimeLifecycle.Armed, runtime.State!.Lifecycle);
        Assert.False(runtime.Start(new("wrong", 1, DigestA)).Ok);
        Assert.True(runtime.Start(new("match-1", 1, DigestA)).Ok);
        Assert.Equal(AbilityRuntimeLifecycle.Running, runtime.State.Lifecycle);
    }

    [Fact]
    public void Disabled_or_invalid_identity_config_never_activates()
    {
        var runtime = CreateRuntime();
        var disabled = Config() with { AbilityModeEnabled = false };

        Assert.False(runtime.ApplyConfirmed(disabled).Activated);
        Assert.Null(runtime.State);

        var badDigest = Config() with { ContentDigest = "invalid" };
        Assert.False(runtime.ApplyConfirmed(badDigest).Activated);
        Assert.Null(runtime.State);
    }

    [Fact]
    public void Duplicate_sync_preserves_runtime_but_new_match_replaces_it()
    {
        var runtime = CreateRuntime();
        runtime.ApplyConfirmed(Config());
        var seat = runtime.State!.Seats["A1"];
        runtime.Charges.Add(seat, 7, ChargeReason.Test);

        var duplicate = runtime.ApplyConfirmed(Config() with { SyncId = "sync-retry" });

        Assert.True(duplicate.Duplicate);
        Assert.Same(seat, runtime.State.Seats["A1"]);
        Assert.Equal(7, seat.Charge);

        var replacement = Config("match-2", 1, DigestB);
        var replaced = runtime.ApplyConfirmed(replacement);

        Assert.True(replaced.ReplacedPreviousMatch);
        Assert.Equal("match-2", runtime.State.MatchId);
        Assert.All(runtime.State.Seats.Values, item => Assert.Equal(0, item.Charge));
    }

    [Fact]
    public void Disconnect_and_same_steam_reconnect_preserve_seat_state()
    {
        var runtime = RunningRuntime();
        var seat = runtime.State!.Seats["A1"];
        runtime.Charges.Add(seat, 5, ChargeReason.Test);
        seat.CrossRoundState["stack"] = "3";

        Assert.True(runtime.Disconnect("76561198000000001"));
        Assert.False(seat.IsConnected);
        Assert.True(runtime.Reconnect("76561198000000001"));

        Assert.True(seat.IsConnected);
        Assert.Equal(5, seat.Charge);
        Assert.Equal("3", seat.CrossRoundState["stack"]);
    }

    [Fact]
    public void Formal_substitute_takes_over_next_round_and_inherits_only_cross_round_state()
    {
        var runtime = RunningRuntime();
        var seat = runtime.State!.Seats["A1"];
        runtime.Charges.Add(seat, 6, ChargeReason.Test);
        seat.CrossRoundState["persistent"] = "yes";
        seat.RoundTemporaryState["temporary"] = "no";

        Assert.True(runtime.QueueSubstitution("A1", "76561198000000999").Ok);
        Assert.Equal("76561198000000001", seat.CurrentSteamId);

        runtime.BeginRound("round-2");

        Assert.Equal("76561198000000999", seat.CurrentSteamId);
        Assert.Equal(6, seat.Charge);
        Assert.Equal("yes", seat.CrossRoundState["persistent"]);
        Assert.Empty(seat.RoundTemporaryState);
    }

    [Fact]
    public void New_round_resets_per_round_usage_without_resetting_charge()
    {
        var runtime = RunningRuntime();
        var seat = runtime.State!.Seats["A1"];
        runtime.Charges.Add(seat, 4, ChargeReason.Test);
        seat.MarkAbilityUsed();
        seat.MarkChargePurchased();

        runtime.BeginRound("round-2");

        Assert.False(seat.AbilityUsedThisRound);
        Assert.False(seat.ChargePurchasedThisRound);
        Assert.Equal(4, seat.Charge);
    }

    [Fact]
    public void Actual_life_death_and_enemy_kill_award_once_but_new_life_can_award_again()
    {
        var runtime = RunningRuntime();
        var attacker = runtime.State!.Seats["A1"];
        var victim = runtime.State.Seats["B1"];

        var first = runtime.RecordDeath("life-b1-1", victim.SeatId, attacker.SeatId);
        var duplicate = runtime.RecordDeath("life-b1-1", victim.SeatId, attacker.SeatId);
        var secondLife = runtime.RecordDeath("life-b1-2", victim.SeatId, attacker.SeatId);

        Assert.True(first.Counted);
        Assert.False(duplicate.Counted);
        Assert.True(secondLife.Counted);
        Assert.Equal(4, attacker.Charge);
        Assert.Equal(2, victim.Charge);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A1")]
    [InlineData("A2")]
    public void Suicide_teamkill_and_world_death_never_award_kill_charge(string? attackerSeatId)
    {
        var runtime = RunningRuntime();
        var victim = runtime.State!.Seats["A1"];

        runtime.RecordDeath("life-a1", victim.SeatId, attackerSeatId);

        Assert.Equal(1, victim.Charge);
        if (attackerSeatId is not null && attackerSeatId != victim.SeatId)
            Assert.Equal(0, runtime.State.Seats[attackerSeatId].Charge);
    }

    [Fact]
    public void Final_round_result_awards_connected_dead_and_disconnected_seats_once()
    {
        var runtime = RunningRuntime();
        runtime.Disconnect("76561198000000002");

        Assert.True(runtime.CompleteRound("round-1", RoundOutcome.TeamAWin).Counted);
        Assert.False(runtime.CompleteRound("round-1", RoundOutcome.TeamAWin).Counted);

        Assert.All(runtime.State!.Seats.Values.Where(s => s.RosterTeam == "A"), seat => Assert.Equal(1, seat.Charge));
        Assert.All(runtime.State.Seats.Values.Where(s => s.RosterTeam == "B"), seat => Assert.Equal(2, seat.Charge));
    }

    [Theory]
    [InlineData(RoundOutcome.Draw)]
    [InlineData(RoundOutcome.Restarted)]
    [InlineData(RoundOutcome.AdminCancelled)]
    public void Non_final_round_outcomes_award_nothing(RoundOutcome outcome)
    {
        var runtime = RunningRuntime();

        runtime.CompleteRound("round-1", outcome);

        Assert.All(runtime.State!.Seats.Values, seat => Assert.Equal(0, seat.Charge));
    }

    private static AbilityMatchRuntime RunningRuntime()
    {
        var runtime = CreateRuntime();
        runtime.ApplyConfirmed(Config());
        runtime.Start(new("match-1", 1, DigestA));
        runtime.BeginRound("round-1");
        return runtime;
    }

    private static AbilityMatchRuntime CreateRuntime() => new(AbilityDefinitionCatalog.CreateForRuntime([
        new("medic", "医师", 12, AbilityReleaseModel.A),
        new("tank", "坦克", 12, AbilityReleaseModel.B, FixedCost: 8),
        new("witch", "女巫", 10, AbilityReleaseModel.A),
        new("commander", "指挥官", 10, AbilityReleaseModel.B, FixedCost: 5),
    ]));

    private static AbilitySyncConfig Config(string matchId = "match-1", int revision = 1, string digest = DigestA) => new()
    {
        ProtocolVersion = 1,
        SyncId = $"sync-{matchId}-{revision}",
        MatchId = matchId,
        Revision = revision,
        CatalogVersion = "ability-catalog-v1",
        AbilityModeEnabled = true,
        ContentDigest = digest,
        Seats =
        [
            new() { PlayerId = "A1", SteamId = "76561198000000001", RosterTeam = "A", InitialSide = "CT", AbilityId = "medic" },
            new() { PlayerId = "A2", SteamId = "76561198000000002", RosterTeam = "A", InitialSide = "CT", AbilityId = "tank" },
            new() { PlayerId = "B1", SteamId = "76561198000000003", RosterTeam = "B", InitialSide = "T", AbilityId = "witch" },
            new() { PlayerId = "B2", SteamId = "76561198000000004", RosterTeam = "B", InitialSide = "T", AbilityId = "commander" },
        ],
    };
}
