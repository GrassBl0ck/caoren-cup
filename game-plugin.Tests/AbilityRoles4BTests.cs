using Caoren;
using Caoren.AbilityMode;
using CaorenCup.Gamemodes.Ability;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRoles4BTests
{
    [Fact]
    public void Prototype_gate_reports_unavailable_without_engine_capability()
    {
        var gate = new AbilityEnginePrototypeGate(NullAbilityEngineCapabilities.Instance);
        var result = gate.Check(AbilityPrototypeKind.DecoyLanding);
        Assert.False(result.Ok);
        Assert.Equal("PROTOTYPE_UNAVAILABLE", result.Code);
    }

    [Fact]
    public void Assassin_passive_is_hidden_only_when_stationary_and_back_attack_gets_bonus()
    {
        var handler = new AssassinRoleHandler();
        var seat = Seat("A1", "assassin");
        Assert.True(handler.IsHidden(seat, speed: 0, active: false));
        Assert.False(handler.IsHidden(seat, speed: 1, active: false));
        Assert.Equal(1.5, handler.GetDamageMultiplier(seat, signedFrontAngleDegrees: 180));
        Assert.Equal(1, handler.GetDamageMultiplier(seat, signedFrontAngleDegrees: 20));
        Assert.True(handler.ActivateUltimate(seat, 8).Ok);
        Assert.True(handler.IsActive(seat, DateTimeOffset.UtcNow));
        Assert.False(handler.IsActive(seat, DateTimeOffset.UtcNow.AddSeconds(4)));
    }

    [Fact]
    public void Snow_golem_converts_firearm_life_damage_to_knockback_until_ultimate()
    {
        var handler = new SnowGolemRoleHandler();
        var seat = Seat("A1", "snow_golem");
        var normal = handler.ResolveFirearmDamage(seat, 40, knockback: 300);
        Assert.Equal(0, normal.LifeDamage);
        Assert.Equal(300, normal.Knockback);
        Assert.True(handler.ActivateUltimate(seat, 8).Ok);
        var empowered = handler.ResolveFirearmDamage(seat, 40, 300);
        Assert.Equal(40, empowered.LifeDamage);
        Assert.Equal(300, empowered.Knockback);
        Assert.Equal(40, handler.ResolveLifeDamage(seat, 40, isTrueDamage: true));
        Assert.False(handler.IsUltimateActive(seat, DateTimeOffset.UtcNow.AddSeconds(6)));
    }

    [Fact]
    public void Witch_poison_does_not_stack_from_duplicate_event()
    {
        var handler = new WitchRoleHandler();
        var target = Seat("B1", "tank");
        Assert.True(handler.ApplyPoison(target, 10, "life-1"));
        Assert.False(handler.ApplyPoison(target, 10, "life-1"));
        Assert.Equal(10, handler.GetPoisonDamage(target));
    }

    [Fact]
    public void Glass_cannon_uses_fixed_health_and_blocks_duplicate_explosion()
    {
        var handler = new GlassCannonRoleHandler();
        var seat = Seat("A1", "glass_cannon");
        Assert.Equal(50, handler.GetRoleMaxHealth(seat, 100));
        Assert.Equal(1.25, handler.GetFirearmMultiplier(seat));
        Assert.True(handler.ActivateUltimate(seat, 10).Ok);
        Assert.True(handler.TryTriggerExplosion(seat));
        Assert.False(handler.TryTriggerExplosion(seat));
    }

    [Fact]
    public void Utility_specialist_refund_is_single_use_per_throw()
    {
        var handler = new UtilitySpecialistRoleHandler();
        var seat = Seat("A1", "utility_specialist");
        Assert.True(handler.ActivateUltimate(seat, 10).Ok);
        Assert.True(handler.TryRefund(seat, "throw-1"));
        Assert.False(handler.TryRefund(seat, "throw-1"));
    }

    [Fact]
    public void Sky_courier_requires_valid_safe_landing_point()
    {
        var handler = new SkyCourierRoleHandler();
        var seat = Seat("A1", "sky_courier");
        Assert.True(handler.CanAirJump(seat));
        Assert.False(handler.TryTeleport(seat, isValidEntity: false, isSafePoint: true).Ok);
        Assert.True(handler.TryTeleport(seat, isValidEntity: true, isSafePoint: true).Ok);
    }

    [Fact]
    public void Coordinator_applies_glass_cannon_fixed_health_on_spawn()
    {
        var seat = Seat("A1", "glass_cannon");
        var coordinator = new AbilityRoleCoordinator(AbilityRoleRegistry.Create(new AbilityRoleSettings()), new MovementModifierService());
        coordinator.BindSeats([seat]);
        var player = coordinator.GetPlayer(seat.SeatId)!;
        coordinator.OnSpawn(player, 100);
        Assert.Equal(50, player.RoleMaxHealth);
        Assert.Equal(50, player.BaseHealth);
    }

    [Fact]
    public void Witch_ultimate_damages_only_nearby_poisoned_enemies_and_deepens_once()
    {
        var handler = new WitchRoleHandler();
        var caster = Seat("A1", "witch");
        var near = Seat("B1", "tank", "B");
        var far = Seat("B2", "tank", "B");
        handler.ApplyPoison(near, 10, "life-1");
        handler.ApplyPoison(far, 10, "life-2");
        far.RoundTemporaryState["witch.position"] = "1000,0,0";
        Assert.True(handler.ActivateUltimate(caster, 10).Ok);
        var first = handler.ApplyUltimate(caster, [near, far], new(0, 0, 0), 100);
        Assert.Equal(1, first.GeneratedDamageEvents);
        Assert.Equal(20, handler.GetPoisonDamage(near));
        Assert.Equal(10, handler.GetPoisonDamage(far));
        var second = handler.ApplyUltimate(caster, [near], new(0, 0, 0), 100);
        Assert.Equal(0, second.GeneratedDamageEvents);
    }

    [Fact]
    public void Utility_specialist_increases_capacity_without_affecting_other_roles()
    {
        var handler = new UtilitySpecialistRoleHandler();
        Assert.Equal(6, handler.GetGrenadeCapacity(Seat("A1", "utility_specialist"), 4));
        Assert.Equal(4, handler.GetGrenadeCapacity(Seat("A2", "tank"), 4));
    }

    [Fact]
    public void Glass_cannon_explosion_plan_is_created_once_with_fixed_values()
    {
        var handler = new GlassCannonRoleHandler();
        var seat = Seat("A1", "glass_cannon");
        handler.ActivateUltimate(seat, 10);
        var first = handler.TryBuildExplosion(seat, 80, 250);
        var second = handler.TryBuildExplosion(seat, 80, 250);
        Assert.NotNull(first);
        Assert.Equal(80, first!.Damage);
        Assert.Equal(250, first.Radius);
        Assert.Null(second);
    }

    private static AbilitySeatState Seat(string id, string role, string team = "A") => new(
        new AbilitySyncSeat { PlayerId = id, SteamId = id, RosterTeam = team, InitialSide = team == "A" ? "CT" : "T", AbilityId = role },
        AbilityDefinitionCatalog.CreateProduction().Get(role));
}
