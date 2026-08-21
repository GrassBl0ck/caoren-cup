using Caoren;
using Caoren.AbilityMode;
using CaorenCup;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRoleCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Fact]
    public void Active_handler_map_contains_enabled_basic_roles_and_command_commits_only_after_effect()
    {
        var seats = new[] { Seat("A1", "A", "medic") };
        var coordinator = Coordinator(seats);
        var seat = seats[0];
        var player = coordinator.GetPlayer("A1")!;
        player.IsAlive = true;
        player.BaseHealth = 20;
        player.RoleMaxHealth = 100;
        var charges = new ChargeService();
        charges.Add(seat, 12, ChargeReason.Test);
        var commands = new AbilityCommandService(charges, coordinator.ActiveHandlers);

        var result = commands.UseUltimate(seat, new(true, true, true, true, true), () => { });

        Assert.True(result.Ok);
        Assert.Equal(0, seat.Charge);
        Assert.Equal(100, player.BaseHealth);
        Assert.Equal(6, coordinator.ActiveHandlers.Count);
    }

    [Fact]
    public void Medic_speed_multiplies_with_tank_penalty_and_refreshes_without_overwriting_external_sources()
    {
        var medic = Seat("A1", "A", "medic");
        var tank = Seat("A2", "A", "tank");
        var coordinator = Coordinator([medic, tank]);
        var target = coordinator.GetPlayer("A2")!;
        target.IsAlive = true;
        target.BaseHealth = 200;
        target.RoleMaxHealth = 200;
        coordinator.OnSpawn(target, originalMaxHealth: 100);

        var shot = coordinator.ResolveMedicShot(medic, tank, isFirearm: true);

        Assert.True(shot.Triggered);
        Assert.Equal(0.99, coordinator.Movement.GetCombined("A2"), 2);
    }

    [Fact]
    public void Role_damage_rules_and_balance_temporary_health_remain_isolated_per_seat()
    {
        var berserker = Seat("A1", "A", "berserker");
        var enemy = Seat("B1", "B", "tank");
        var secondTank = Seat("A2", "A", "tank");
        var coordinator = Coordinator([berserker, enemy, secondTank]);
        var killer = coordinator.GetPlayer("A1")!;
        var victim = coordinator.GetPlayer("B1")!;
        killer.IsAlive = true;
        killer.BaseHealth = 10;
        killer.RoleMaxHealth = 100;
        victim.IsAlive = false;
        coordinator.OnConfirmedKill(killer, victim);
        coordinator.Balance.GrantTemporaryHealth(victim, 40, Now);

        Assert.Equal(0.95, coordinator.GetIncomingDamageMultiplier(berserker, false, RoleDamageKind.Firearm, 0), 2);
        Assert.Equal(0.60, coordinator.GetIncomingDamageMultiplier(enemy, false, RoleDamageKind.Firearm, 0), 2);
        Assert.Equal(0.60, coordinator.GetIncomingDamageMultiplier(secondTank, false, RoleDamageKind.Firearm, 0), 2);
        Assert.Equal(1, coordinator.GetIncomingDamageMultiplier(enemy, true, RoleDamageKind.Firearm, 0));
        Assert.Equal(0, coordinator.Balance.AbsorbDamage(victim, 20, true).RemainingLifeDamage);
        Assert.Equal(20, victim.TemporaryHealth);
    }

    [Fact]
    public void Cleanup_restores_movement_and_removes_temporary_and_active_role_state()
    {
        var tank = Seat("A1", "A", "tank");
        var commander = Seat("A2", "A", "commander");
        var coordinator = Coordinator([tank, commander]);
        var tankPlayer = coordinator.GetPlayer("A1")!;
        var commanderPlayer = coordinator.GetPlayer("A2")!;
        tankPlayer.IsAlive = true;
        coordinator.OnSpawn(tankPlayer, 100);
        coordinator.Tank.ActivateUltimate(tank, coordinator.Movement, Now);
        coordinator.Balance.GrantTemporaryHealth(tankPlayer, 40, Now);
        coordinator.Commander.ActivateUltimate(commander, Now);

        coordinator.ClearAll();

        Assert.Equal(1, coordinator.Movement.GetCombined("A1"));
        Assert.Equal(0, tankPlayer.TemporaryHealth);
        Assert.False(tank.RecoverableUntil.ContainsKey(TankRoleHandler.UltimateStateKey));
        Assert.False(commander.RecoverableUntil.ContainsKey(CommanderRoleHandler.UltimateStateKey));
    }

    [Fact]
    public void Hot_reload_binding_rehydrates_tank_penalty_and_remaining_medic_speed()
    {
        var medic = Seat("A1", "A", "medic");
        var tank = Seat("A2", "A", "tank");
        tank.BeginLife("round-1");
        var first = Coordinator([medic, tank]);
        var target = first.GetPlayer("A2")!;
        target.BaseHealth = 200;
        target.RoleMaxHealth = 200;
        first.OnSpawn(target, 100);
        first.ResolveMedicShot(medic, tank, true);

        var restored = Coordinator([medic, tank]);

        Assert.Equal(0.99, restored.Movement.GetCombined("A2"), 2);
    }

    [Fact]
    public void Death_clears_tank_commander_and_temporary_health_without_touching_other_seats()
    {
        var tank = Seat("A1", "A", "tank");
        var commander = Seat("A2", "A", "commander");
        var coordinator = Coordinator([tank, commander]);
        var tankPlayer = coordinator.GetPlayer("A1")!;
        var commanderPlayer = coordinator.GetPlayer("A2")!;
        tankPlayer.IsAlive = true;
        commanderPlayer.IsAlive = true;
        coordinator.Tank.ActivateUltimate(tank, coordinator.Movement, Now);
        coordinator.Commander.ActivateUltimate(commander, Now);
        coordinator.Balance.GrantTemporaryHealth(tankPlayer, 40, Now);

        coordinator.OnDeath(tankPlayer);

        Assert.False(coordinator.Tank.IsUltimateActive(tank, Now));
        Assert.Equal(0, tankPlayer.TemporaryHealth);
        Assert.True(coordinator.Commander.BuildHud(commanderPlayer, coordinator.Players, Now).IsUltimateView);
    }

    [Fact]
    public void Same_role_on_opposing_teams_keeps_state_completely_isolated()
    {
        var first = Seat("A1", "A", "berserker");
        var second = Seat("B1", "B", "berserker");
        var victim = Seat("B2", "B", "tank");
        var coordinator = Coordinator([first, second, victim]);
        var killer = coordinator.GetPlayer("A1")!;
        killer.IsAlive = true;
        killer.RoleMaxHealth = 100;
        coordinator.OnConfirmedKill(killer, coordinator.GetPlayer("B2")!);

        Assert.Equal(1, coordinator.Berserker.GetLayers(first));
        Assert.Equal(0, coordinator.Berserker.GetLayers(second));
    }

    private static AbilityRoleCoordinator Coordinator(IEnumerable<AbilitySeatState> seats)
    {
        var coordinator = new AbilityRoleCoordinator(
            AbilityRoleRegistry.Create(new AbilityRoleSettings()),
            new MovementModifierService(),
            () => Now);
        coordinator.BindSeats(seats);
        return coordinator;
    }

    private static AbilitySeatState Seat(string id, string team, string ability) => new(
        new AbilitySyncSeat
        {
            PlayerId = id,
            SteamId = $"7656119800005{id[^1]}01",
            RosterTeam = team,
            InitialSide = team == "A" ? "CT" : "T",
            AbilityId = ability,
        },
        AbilityDefinitionCatalog.CreateProduction().Get(ability));
}
