using System.Collections.Generic;
using System.Linq;
using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

/// <summary>中途分配：运行中的席位需要能增、删、改职业，且只在回合开始由适配器触发。</summary>
public sealed class AbilitySeatSyncTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static AbilitySyncSeat Seat(string id, string steamId, string team, string ability) => new()
    {
        PlayerId = id,
        SteamId = steamId,
        RosterTeam = team,
        InitialSide = team == "A" ? "CT" : "T",
        AbilityId = ability,
    };

    private static AbilitySyncConfig Config(params AbilitySyncSeat[] seats) => new()
    {
        ProtocolVersion = 1,
        SyncId = "sync-1",
        MatchId = "match-1",
        Revision = 1,
        CatalogVersion = "ability-catalog-v1",
        AbilityModeEnabled = true,
        ContentDigest = Digest,
        Seats = seats.ToList(),
    };

    private static AbilityMatchRuntime RunningRuntime()
    {
        var runtime = new AbilityMatchRuntime(AbilityDefinitionCatalog.CreateProduction());
        runtime.ApplyConfirmed(Config(
            Seat("A1", "76561198000000001", "A", "medic"),
            Seat("B1", "76561198000000002", "B", "tank")));
        runtime.Start(new AbilityRuntimeIdentity("match-1", 1, Digest, 1));
        return runtime;
    }

    [Fact]
    public void New_seat_is_added_with_zero_charge()
    {
        var runtime = RunningRuntime();

        var result = runtime.SynchronizeSeats(new[]
        {
            Seat("A1", "76561198000000001", "A", "medic"),
            Seat("B1", "76561198000000002", "B", "tank"),
            Seat("A2", "76561198000000003", "A", "utility_specialist"),
        });

        Assert.True(result.Ok);
        Assert.Equal(new[] { "A2" }, result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal(0, runtime.State!.Seats["A2"].Charge);
        Assert.Equal(3, runtime.State.Seats.Count);
    }

    [Fact]
    public void Seat_missing_from_desired_list_is_removed()
    {
        var runtime = RunningRuntime();

        var result = runtime.SynchronizeSeats(new[] { Seat("A1", "76561198000000001", "A", "medic") });

        Assert.True(result.Ok);
        Assert.Equal(new[] { "B1" }, result.Removed);
        Assert.False(runtime.State!.Seats.ContainsKey("B1"));
    }

    [Fact]
    public void Changed_ability_keeps_accumulated_charge()
    {
        var runtime = RunningRuntime();
        runtime.Charges.Add(runtime.State!.Seats["A1"], 6, ChargeReason.Test);

        var result = runtime.SynchronizeSeats(new[]
        {
            Seat("A1", "76561198000000001", "A", "utility_specialist"),
            Seat("B1", "76561198000000002", "B", "tank"),
        });

        Assert.True(result.Ok);
        Assert.Equal(new[] { "A1" }, result.Updated);
        Assert.Equal("utility_specialist", runtime.State!.Seats["A1"].AbilityId);
        Assert.Equal(6, runtime.State.Seats["A1"].Charge);
    }

    [Fact]
    public void Synchronizing_before_runtime_starts_is_rejected()
    {
        var runtime = new AbilityMatchRuntime(AbilityDefinitionCatalog.CreateProduction());
        runtime.ApplyConfirmed(Config(Seat("A1", "76561198000000001", "A", "medic")));

        var result = runtime.SynchronizeSeats(new[] { Seat("A1", "76561198000000001", "A", "medic") });

        Assert.False(result.Ok);
        Assert.Equal("RUNTIME_NOT_RUNNING", result.Code);
    }

    [Fact]
    public void Unknown_ability_in_desired_list_is_rejected_without_mutating_seats()
    {
        var runtime = RunningRuntime();

        var result = runtime.SynchronizeSeats(new[]
        {
            Seat("A1", "76561198000000001", "A", "medic"),
            Seat("B1", "76561198000000002", "B", "not_a_role"),
        });

        Assert.False(result.Ok);
        Assert.Equal("UNKNOWN_ABILITY", result.Code);
        Assert.Equal(2, runtime.State!.Seats.Count);
        Assert.Equal("tank", runtime.State.Seats["B1"].AbilityId);
    }
}
