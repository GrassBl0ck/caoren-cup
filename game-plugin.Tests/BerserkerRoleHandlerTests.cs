using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class BerserkerRoleHandlerTests
{
    [Fact]
    public void Confirmed_enemy_kill_heals_living_berserker_and_layers_cap_at_five()
    {
        var handler = new BerserkerRoleHandler();
        var killer = Player(Seat("A1", "A", "berserker"), true, 12, 100);
        var victim = Player(Seat("B1", "B", "medic"), false, 0, 100);

        for (var i = 0; i < 7; i++)
        {
            killer.BaseHealth = 20;
            Assert.True(handler.OnConfirmedEnemyKill(killer, victim, finalDeathConfirmed: true));
        }

        Assert.Equal(100, killer.BaseHealth);
        Assert.Equal(5, handler.GetLayers(killer.Seat));
        Assert.Equal(0.75, handler.GetIncomingDamageMultiplier(killer.Seat, isTrueDamage: false), 2);
    }

    [Fact]
    public void Death_before_confirmation_teamkill_and_duplicate_or_nonfinal_events_do_not_trigger()
    {
        var handler = new BerserkerRoleHandler();
        var killer = Player(Seat("A1", "A", "berserker"), false, 5, 100);
        var enemy = Player(Seat("B1", "B", "tank"), false, 0, 200);
        var teammate = Player(Seat("A2", "A", "tank"), false, 0, 200);

        Assert.False(handler.OnConfirmedEnemyKill(killer, enemy, true));
        killer.IsAlive = true;
        Assert.False(handler.OnConfirmedEnemyKill(killer, teammate, true));
        Assert.False(handler.OnConfirmedEnemyKill(killer, enemy, false));
        Assert.Equal(0, handler.GetLayers(killer.Seat));
        Assert.Equal(5, killer.BaseHealth);
    }

    [Fact]
    public void True_damage_bypasses_layers_and_round_reset_clears_them()
    {
        var handler = new BerserkerRoleHandler();
        var killer = Player(Seat("A1", "A", "berserker"), true, 10, 100);
        var enemy = Player(Seat("B1", "B", "tank"), false, 0, 200);
        handler.OnConfirmedEnemyKill(killer, enemy, true);

        Assert.Equal(0.95, handler.GetIncomingDamageMultiplier(killer.Seat, false), 2);
        Assert.Equal(1, handler.GetIncomingDamageMultiplier(killer.Seat, true));

        handler.OnRoundEnded(killer.Seat);
        Assert.Equal(0, handler.GetLayers(killer.Seat));
    }

    [Theory]
    [InlineData(8, 1.20)]
    [InlineData(9, 1.20)]
    [InlineData(10, 1.25)]
    [InlineData(11, 1.25)]
    [InlineData(12, 1.30)]
    public void Ultimate_uses_confirmed_charge_tiers_and_balances_per_shot_recoil(int charge, double fireRate)
    {
        var handler = new BerserkerRoleHandler();
        var seat = Seat("A1", "A", "berserker");

        var result = handler.ActivateUltimate(seat, charge);

        Assert.True(result.Ok);
        Assert.Equal(fireRate, handler.GetFireRateMultiplier(seat), 2);
        Assert.Equal(1 / fireRate, handler.GetPerShotRecoilMultiplier(seat), 4);
    }

    [Fact]
    public void Ultimate_preserves_only_magazine_ammo_and_survives_weapon_switch_until_round_end()
    {
        var handler = new BerserkerRoleHandler();
        var seat = Seat("A1", "A", "berserker");
        handler.ActivateUltimate(seat, 12);

        Assert.True(handler.ShouldPreserveAmmo(seat, WeaponUseKind.MagazineFirearm));
        Assert.False(handler.ShouldPreserveAmmo(seat, WeaponUseKind.Knife));
        Assert.False(handler.ShouldPreserveAmmo(seat, WeaponUseKind.Taser));
        Assert.False(handler.ShouldPreserveAmmo(seat, WeaponUseKind.Grenade));
        Assert.True(handler.IsUltimateActive(seat));

        handler.OnRoundEnded(seat);
        Assert.False(handler.IsUltimateActive(seat));
        Assert.Equal(1, handler.GetFireRateMultiplier(seat));
    }

    private static AbilityRolePlayer Player(AbilitySeatState seat, bool alive, int health, int maxHealth) =>
        new(seat) { IsAlive = alive, BaseHealth = health, RoleMaxHealth = maxHealth };

    private static AbilitySeatState Seat(string id, string team, string abilityId) => new(
        new AbilitySyncSeat
        {
            PlayerId = id,
            SteamId = $"7656119800001{id[^1]}01",
            RosterTeam = team,
            InitialSide = team == "A" ? "CT" : "T",
            AbilityId = abilityId,
        },
        AbilityDefinitionCatalog.CreateProduction().Get(abilityId));
}
