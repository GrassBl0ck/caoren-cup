using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class TankRoleHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Fact]
    public void Tank_max_health_and_spawn_restore_are_idempotent()
    {
        var handler = new TankRoleHandler();
        var player = new AbilityRolePlayer(Seat()) { IsAlive = true, BaseHealth = 25, RoleMaxHealth = 100 };

        handler.ApplySpawn(player, originalMaxHealth: 100);
        handler.ApplySpawn(player, originalMaxHealth: 100);

        Assert.Equal(200, player.RoleMaxHealth);
        Assert.Equal(200, player.BaseHealth);
    }

    [Theory]
    [InlineData(RoleDamageKind.Firearm, 0, 0.60)]
    [InlineData(RoleDamageKind.Firearm, 60, 0.60)]
    [InlineData(RoleDamageKind.Firearm, 61, 0.80)]
    [InlineData(RoleDamageKind.Firearm, 180, 0.80)]
    [InlineData(RoleDamageKind.Knife, 0, 0.80)]
    [InlineData(RoleDamageKind.Grenade, 0, 0.80)]
    [InlineData(RoleDamageKind.DamageOverTime, 0, 0.80)]
    public void Passive_mitigation_uses_additive_front_sector_only_for_firearms(
        RoleDamageKind kind,
        double angle,
        double expected)
    {
        var handler = new TankRoleHandler();

        Assert.Equal(expected, handler.GetIncomingDamageMultiplier(Seat(), false, kind, angle, Now), 2);
    }

    [Fact]
    public void True_damage_bypasses_tank_normal_mitigation()
    {
        Assert.Equal(1, new TankRoleHandler().GetIncomingDamageMultiplier(
            Seat(), true, RoleDamageKind.Firearm, 0, Now));
    }

    [Fact]
    public void Ultimate_front_firearm_total_is_eighty_percent_and_outgoing_life_damage_is_quarter()
    {
        var handler = new TankRoleHandler();
        var seat = Seat();
        var movement = new MovementModifierService();
        handler.ActivateUltimate(seat, movement, Now);

        Assert.Equal(0.20, handler.GetIncomingDamageMultiplier(seat, false, RoleDamageKind.Firearm, 0, Now), 2);
        Assert.Equal(0.80, handler.GetIncomingDamageMultiplier(seat, false, RoleDamageKind.Firearm, 90, Now), 2);
        Assert.Equal(0.25, handler.GetOutgoingLifeDamageMultiplier(seat, RoleDamageKind.Firearm, Now), 2);
        Assert.Equal(0.25, handler.GetOutgoingLifeDamageMultiplier(seat, RoleDamageKind.Knife, Now), 2);
        Assert.Equal(0.25, handler.GetOutgoingLifeDamageMultiplier(seat, RoleDamageKind.Grenade, Now), 2);
        Assert.Equal(1, handler.GetOutgoingLifeDamageMultiplier(seat, RoleDamageKind.NonLifeKnockback, Now));
    }

    [Fact]
    public void Ultimate_removes_only_tank_penalty_and_expiry_death_or_round_end_restores_it()
    {
        var handler = new TankRoleHandler();
        var seat = Seat();
        var movement = new MovementModifierService();
        movement.Set(seat.SeatId, "external.slow", 0.8);
        handler.ApplyPassiveMovement(seat, movement);
        Assert.Equal(0.72, movement.GetCombined(seat.SeatId), 2);

        handler.ActivateUltimate(seat, movement, Now);
        Assert.Equal(0.8, movement.GetCombined(seat.SeatId), 2);
        Assert.Equal(1, handler.GetOwnJumpMultiplier(seat, Now));

        handler.EndUltimate(seat, movement, restorePenalty: true);
        Assert.Equal(0.72, movement.GetCombined(seat.SeatId), 2);
        Assert.Equal(0.9, handler.GetOwnJumpMultiplier(seat, Now.AddSeconds(6)), 2);

        handler.OnCleanup(seat, movement);
        Assert.Equal(0.8, movement.GetCombined(seat.SeatId), 2);
    }

    private static AbilitySeatState Seat() => new(
        new AbilitySyncSeat
        {
            PlayerId = "A1",
            SteamId = "76561198000000001",
            RosterTeam = "A",
            InitialSide = "CT",
            AbilityId = "tank",
        },
        AbilityDefinitionCatalog.CreateProduction().Get("tank"));
}
