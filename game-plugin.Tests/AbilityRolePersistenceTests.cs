using Caoren;
using Caoren.AbilityMode;
using CaorenCup;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRolePersistenceTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Fact]
    public async Task Hot_reload_restores_all_phase_4a_recoverable_role_state_without_replaying_effects()
    {
        var catalog = AbilityDefinitionCatalog.CreateProduction();
        var runtime = new AbilityMatchRuntime(catalog);
        runtime.ApplyConfirmed(Config());
        runtime.Start(new("match-1", 1, Digest));
        runtime.BeginRound("round-3");
        foreach (var seat in runtime.State!.Seats.Values) seat.BeginLife("round-3");
        var coordinator = Coordinator(runtime.State.Seats.Values);
        var berserker = coordinator.GetPlayer("A2")!;
        var defeated = coordinator.GetPlayer("B1")!;
        berserker.IsAlive = true;
        berserker.BaseHealth = 20;
        berserker.RoleMaxHealth = 100;
        defeated.IsAlive = false;
        coordinator.OnConfirmedKill(berserker, defeated);
        coordinator.Berserker.ActivateUltimate(berserker.Seat, 10);
        coordinator.Tank.ActivateUltimate(runtime.State.Seats["A3"], coordinator.Movement, Now);
        var tank = coordinator.GetPlayer("A3")!;
        coordinator.OnSpawn(tank, 100);
        tank.BaseHealth = 147;
        coordinator.Tank.PersistManagedHealth(tank, 100);
        coordinator.Balance.GrantTemporaryHealth(coordinator.GetPlayer("B1")!, 37, Now);
        coordinator.Commander.ActivateUltimate(runtime.State.Seats["B2"], Now);
        coordinator.Medic.RecordActualHealthLoss(runtime.State.Seats["B1"], Now, 5);
        var capitalist = coordinator.GetPlayer("A4")!;
        capitalist.Money = 1_000;
        new SeatEconomyLedger().Synchronize(capitalist, 16_000);
        var enemy = coordinator.GetPlayer("B1")!;
        enemy.Money = 1_000;
        new SeatEconomyLedger().Synchronize(enemy, 16_000);
        var plan = coordinator.Capitalist.BuildPlunderPlan(capitalist.Seat, coordinator.Players, 14, 16_000);
        Assert.True(coordinator.Capitalist.TryApplyPlan(plan, coordinator.Players, "operation-1", 16_000));
        runtime.State.Seats["B2"].MarkAbilityUsed();
        var path = Path.Combine(Path.GetTempPath(), $"ability-role-state-{Guid.NewGuid():N}.json");
        await using var persistence = new RuntimePersistence(path, catalog);

        await persistence.SaveNowAsync(runtime.State);
        var restoredState = await persistence.LoadAsync(new("match-1", 1, Digest));
        var restored = Coordinator(restoredState!.Seats.Values);

        Assert.Equal(1, restored.Berserker.GetLayers(restoredState.Seats["A2"]));
        Assert.Equal(1.25, restored.Berserker.GetFireRateMultiplier(restoredState.Seats["A2"]), 2);
        Assert.True(restored.Tank.IsUltimateActive(restoredState.Seats["A3"], Now.AddSeconds(1)));
        Assert.True(restored.GetPlayer("A3")!.HasManagedHealthSnapshot);
        Assert.Equal(147, restored.GetPlayer("A3")!.BaseHealth);
        Assert.Equal(200, restored.GetPlayer("A3")!.RoleMaxHealth);
        Assert.Equal(37, restored.GetPlayer("B1")!.TemporaryHealth);
        Assert.True(restored.Commander.BuildHud(
            restored.GetPlayer("B2")!, restored.Players, Now.AddSeconds(1)).IsUltimateView);
        Assert.True(restoredState.Seats["B1"].RoundTemporaryState.ContainsKey(MedicRoleHandler.RecentDamageStateKey));
        Assert.True(restoredState.Seats["B2"].AbilityUsedThisRound);
        Assert.Contains(restoredState.Seats["A4"].CrossRoundState.Keys, key => key.Contains("operation-1", StringComparison.Ordinal));
        Assert.Equal(capitalist.Money, restored.GetPlayer("A4")!.Money);
    }

    [Fact]
    public void Formal_substitute_inherits_role_and_economy_ledger_but_not_previous_round_temporary_state()
    {
        var catalog = AbilityDefinitionCatalog.CreateProduction();
        var runtime = new AbilityMatchRuntime(catalog);
        var config = Config() with { Seats = [Seat("A4", "A", "capitalist", 4)] };
        runtime.ApplyConfirmed(config);
        runtime.Start(new("match-1", 1, Digest));
        runtime.BeginRound("round-1");
        var seat = runtime.State!.Seats["A4"];
        new SeatEconomyLedger().Write(seat, 7_777, 16_000);
        seat.RoundTemporaryState["temporary"] = "old-round";

        Assert.True(runtime.QueueSubstitution("A4", "76561198000069999").Ok);
        runtime.BeginRound("round-2");

        Assert.Equal("76561198000069999", seat.CurrentSteamId);
        Assert.Equal("capitalist", seat.AbilityId);
        Assert.Equal("7777", seat.CrossRoundState[SeatEconomyLedger.BalanceStateKey]);
        Assert.Empty(seat.RoundTemporaryState);
    }

    private static AbilityRoleCoordinator Coordinator(IEnumerable<AbilitySeatState> seats)
    {
        var coordinator = new AbilityRoleCoordinator(
            AbilityRoleRegistry.Create(new AbilityRoleSettings()),
            new MovementModifierService(),
            () => Now);
        coordinator.BindSeats(seats);
        foreach (var player in coordinator.Players)
        {
            player.IsAlive = player.Seat.IsAlive;
            if (!player.HasManagedHealthSnapshot)
            {
                player.BaseHealth = 100;
                player.RoleMaxHealth = player.Seat.AbilityId == "tank" ? 200 : 100;
            }
        }
        return coordinator;
    }

    private static AbilitySyncConfig Config() => new()
    {
        ProtocolVersion = 1,
        SyncId = "sync-1",
        MatchId = "match-1",
        Revision = 1,
        CatalogVersion = "ability-catalog-v1",
        AbilityModeEnabled = true,
        ContentDigest = Digest,
        Seats =
        [
            Seat("A1", "A", "medic", 1),
            Seat("A2", "A", "berserker", 2),
            Seat("A3", "A", "tank", 3),
            Seat("A4", "A", "capitalist", 4),
            Seat("B1", "B", "balance", 5),
            Seat("B2", "B", "commander", 6),
        ],
    };

    private static AbilitySyncSeat Seat(string id, string team, string ability, int steamTail) => new()
    {
        PlayerId = id,
        SteamId = $"7656119800006000{steamTail}",
        RosterTeam = team,
        InitialSide = team == "A" ? "CT" : "T",
        AbilityId = ability,
    };
}
