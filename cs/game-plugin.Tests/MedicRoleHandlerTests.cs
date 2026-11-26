using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class MedicRoleHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Fact]
    public void Firearm_hit_on_teammate_replaces_damage_with_healing_and_speed()
    {
        var medic = Seat("A1", "A", "medic");
        var target = Seat("A2", "A", "tank");

        var result = new MedicRoleHandler().ResolveFriendlyShot(
            medic, target, Now, isFirearm: true, baseHealth: 150, roleMaxHealth: 200);

        Assert.True(result.ReplacesFriendlyDamage);
        Assert.True(result.Triggered);
        Assert.Equal(10, result.Healing);
        Assert.Equal(1.10, result.SpeedMultiplier, 2);
        Assert.Equal(TimeSpan.FromSeconds(2), result.SpeedDuration);
    }

    [Fact]
    public void Recent_actual_health_loss_halves_effect_but_zero_friendly_damage_does_not_mark_recent_loss()
    {
        var handler = new MedicRoleHandler();
        var medic = Seat("A1", "A", "medic");
        var target = Seat("A2", "A", "tank");
        handler.RecordActualHealthLoss(target, Now, 1);

        var recent = handler.ResolveFriendlyShot(medic, target, Now.AddMilliseconds(10), true, 150, 200);
        var friendlyZero = handler.RecordActualHealthLoss(target, Now.AddMilliseconds(20), 0);

        Assert.Equal(5, recent.Healing);
        Assert.Equal(1.05, recent.SpeedMultiplier, 2);
        Assert.False(friendlyZero);
    }

    [Fact]
    public void Cooldown_is_independent_per_target_and_blocks_heal_and_speed_without_restoring_damage()
    {
        var handler = new MedicRoleHandler();
        var medic = Seat("A1", "A", "medic");
        var first = Seat("A2", "A", "tank");
        var second = Seat("A3", "A", "balance");

        Assert.True(handler.ResolveFriendlyShot(medic, first, Now, true, 100, 200).Triggered);
        var blocked = handler.ResolveFriendlyShot(medic, first, Now.AddMilliseconds(499), true, 100, 200);
        var independent = handler.ResolveFriendlyShot(medic, second, Now.AddMilliseconds(100), true, 50, 100);

        Assert.True(blocked.ReplacesFriendlyDamage);
        Assert.False(blocked.Triggered);
        Assert.Equal(0, blocked.Healing);
        Assert.Equal(1, blocked.SpeedMultiplier);
        Assert.True(independent.Triggered);
    }

    [Fact]
    public void Successful_repeat_after_cooldown_refreshes_same_speed_source_without_stacking()
    {
        var handler = new MedicRoleHandler();
        var medic = Seat("A1", "A", "medic");
        var target = Seat("A2", "A", "tank");
        var movement = new MovementModifierService();

        handler.ApplySpeed(movement, target, handler.ResolveFriendlyShot(medic, target, Now, true, 200, 200), Now);
        handler.ApplySpeed(movement, target, handler.ResolveFriendlyShot(medic, target, Now.AddSeconds(1), true, 200, 200), Now.AddSeconds(1));

        Assert.Equal(1.10, movement.GetCombined(target.SeatId), 2);
        Assert.Equal(Now.AddSeconds(3), target.RecoverableUntil[MedicRoleHandler.SpeedStateKey]);
    }

    [Fact]
    public void Invalid_weapon_enemy_and_self_do_not_trigger_and_healing_is_capped_at_role_maximum()
    {
        var handler = new MedicRoleHandler();
        var medic = Seat("A1", "A", "medic");
        var teammate = Seat("A2", "A", "tank");
        var enemy = Seat("B1", "B", "tank");

        Assert.False(handler.ResolveFriendlyShot(medic, teammate, Now, false, 100, 200).ReplacesFriendlyDamage);
        Assert.False(handler.ResolveFriendlyShot(medic, enemy, Now, true, 100, 200).ReplacesFriendlyDamage);
        Assert.False(handler.ResolveFriendlyShot(medic, medic, Now, true, 50, 100).ReplacesFriendlyDamage);
        Assert.Equal(3, handler.ResolveFriendlyShot(medic, teammate, Now, true, 197, 200).Healing);
    }

    [Fact]
    public void Ultimate_restores_only_alive_team_base_health_and_allows_empty_cast()
    {
        var handler = new MedicRoleHandler();
        var medic = Player(Seat("A1", "A", "medic"), true, 1, 100, temporaryHealth: 0);
        var tank = Player(Seat("A2", "A", "tank"), true, 20, 200, temporaryHealth: 0);
        var balance = Player(Seat("A3", "A", "balance"), true, 60, 100, temporaryHealth: 25);
        var dead = Player(Seat("A4", "A", "commander"), false, 0, 100, temporaryHealth: 0);
        var enemy = Player(Seat("B1", "B", "tank"), true, 50, 200, temporaryHealth: 0);

        var result = handler.ApplyUltimate(medic.Seat, [medic, tank, balance, dead, enemy]);
        var empty = handler.ApplyUltimate(medic.Seat, [Player(Seat("A5", "A", "tank"), true, 200, 200, 0)]);

        Assert.True(result.Ok);
        Assert.Equal(100, medic.BaseHealth);
        Assert.Equal(200, tank.BaseHealth);
        Assert.Equal(100, balance.BaseHealth);
        Assert.Equal(25, balance.TemporaryHealth);
        Assert.Equal(0, dead.BaseHealth);
        Assert.Equal(50, enemy.BaseHealth);
        Assert.True(empty.Ok);
        Assert.True(empty.EmptyCast);
    }

    private static AbilityRolePlayer Player(AbilitySeatState seat, bool alive, int health, int maxHealth, int temporaryHealth) =>
        new(seat) { IsAlive = alive, BaseHealth = health, RoleMaxHealth = maxHealth, TemporaryHealth = temporaryHealth };

    private static AbilitySeatState Seat(string id, string team, string abilityId) => new(
        new AbilitySyncSeat
        {
            PlayerId = id,
            SteamId = $"7656119800000{id[^1]}01",
            RosterTeam = team,
            InitialSide = team == "A" ? "CT" : "T",
            AbilityId = abilityId,
        },
        AbilityDefinitionCatalog.CreateProduction().Get(abilityId));
}
