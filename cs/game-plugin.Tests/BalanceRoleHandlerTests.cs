using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class BalanceRoleHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Theory]
    [InlineData(1, 12, false, false)]
    [InlineData(13, 12, false, false)]
    [InlineData(2, 12, false, true)]
    [InlineData(12, 12, false, true)]
    [InlineData(14, 12, false, true)]
    [InlineData(25, 12, true, false)]
    [InlineData(1, 15, false, false)]
    [InlineData(16, 15, false, false)]
    [InlineData(17, 15, false, true)]
    public void Buy_phase_rule_excludes_each_half_opener_and_all_overtime(
        int round,
        int regulationRoundsPerHalf,
        bool overtime,
        bool expected)
    {
        Assert.Equal(expected, BalanceRoleHandler.ShouldSetBuyMoney(
            new(round, regulationRoundsPerHalf, overtime)));
    }

    [Fact]
    public void Same_round_buy_event_sets_3850_once_then_allows_normal_changes()
    {
        var handler = new BalanceRoleHandler();
        var player = Player("A1", "A", "balance", money: 900);
        var context = new BalanceRoundContext(2, 12, false);

        Assert.True(handler.TryApplyBuyPhase(player, context));
        Assert.Equal(3_850, player.Money);
        player.Money -= 500;
        Assert.False(handler.TryApplyBuyPhase(player, context));
        Assert.Equal(3_350, player.Money);
    }

    [Fact]
    public void Ultimate_clamps_enemy_base_health_and_adds_independent_temporary_health_without_damage()
    {
        var handler = new BalanceRoleHandler();
        var caster = Player("A1", "A", "balance", health: 250, maximum: 100);
        var tank = Player("B1", "B", "tank", health: 50, maximum: 200);
        var medic = Player("B2", "B", "medic", health: 80, maximum: 100);
        var dead = Player("B3", "B", "commander", health: 0, maximum: 100, alive: false);

        var result = handler.ApplyUltimate(caster, [caster, tank, medic, dead], Now);

        Assert.True(result.Ok);
        Assert.False(result.EmptyCast);
        Assert.Equal(200, tank.BaseHealth);
        Assert.Equal(100, medic.BaseHealth);
        Assert.Equal(40, tank.TemporaryHealth);
        Assert.Equal(40, medic.TemporaryHealth);
        Assert.Equal(0, dead.TemporaryHealth);
        Assert.Equal(0, result.GeneratedDamageEvents);
    }

    [Fact]
    public void Ultimate_uses_one_as_lower_bound_and_allows_no_enemy_empty_cast()
    {
        var handler = new BalanceRoleHandler();
        var caster = Player("A1", "A", "balance", health: 0, maximum: 100);
        var enemy = Player("B1", "B", "medic", health: 80, maximum: 100);

        var applied = handler.ApplyUltimate(caster, [caster, enemy], Now);
        var empty = handler.ApplyUltimate(caster, [caster], Now);

        Assert.Equal(1, enemy.BaseHealth);
        Assert.True(applied.Ok);
        Assert.True(empty.Ok);
        Assert.True(empty.EmptyCast);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Temporary_health_absorbs_normal_and_true_damage_before_base_health(bool trueDamage)
    {
        var handler = new BalanceRoleHandler();
        var player = Player("B1", "B", "tank", health: 100, maximum: 200);
        handler.GrantTemporaryHealth(player, 40, Now);

        var first = handler.AbsorbDamage(player, 25, trueDamage);
        var second = handler.AbsorbDamage(player, 30, trueDamage);

        Assert.Equal(0, first.RemainingLifeDamage);
        Assert.Equal(25, first.AbsorbedTemporaryHealth);
        Assert.Equal(15, second.AbsorbedTemporaryHealth);
        Assert.Equal(15, second.RemainingLifeDamage);
        Assert.Equal(100, player.BaseHealth);
        Assert.Equal(0, player.TemporaryHealth);
    }

    [Fact]
    public void Decay_uses_persisted_cadence_and_never_damages_base_health()
    {
        var handler = new BalanceRoleHandler();
        var seat = Seat("B1", "B", "tank");
        var player = new AbilityRolePlayer(seat) { IsAlive = true, BaseHealth = 100, RoleMaxHealth = 200 };
        handler.GrantTemporaryHealth(player, 40, Now);

        var restored = new AbilityRolePlayer(seat) { IsAlive = true, BaseHealth = 100, RoleMaxHealth = 200 };
        Assert.True(handler.RestoreTemporaryHealth(restored));
        handler.TickDecay(restored, Now.AddSeconds(3.2));

        Assert.Equal(37, restored.TemporaryHealth);
        Assert.Equal(100, restored.BaseHealth);
        Assert.Equal(
            Now.AddSeconds(4).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            seat.RoundTemporaryState[BalanceRoleHandler.NextDecayStateKey]);
    }

    [Fact]
    public void Death_round_end_and_mode_cleanup_remove_temporary_health()
    {
        var handler = new BalanceRoleHandler();
        var player = Player("B1", "B", "tank", health: 100, maximum: 200);
        handler.GrantTemporaryHealth(player, 40, Now);

        handler.ClearTemporaryHealth(player);

        Assert.Equal(0, player.TemporaryHealth);
        Assert.False(player.Seat.RoundTemporaryState.ContainsKey(BalanceRoleHandler.TemporaryHealthStateKey));
        Assert.False(player.Seat.RoundTemporaryState.ContainsKey(BalanceRoleHandler.NextDecayStateKey));
    }

    private static AbilityRolePlayer Player(
        string id,
        string team,
        string ability,
        int money = 0,
        int health = 100,
        int maximum = 100,
        bool alive = true) => new(Seat(id, team, ability))
        {
            Money = money,
            BaseHealth = health,
            RoleMaxHealth = maximum,
            IsAlive = alive,
        };

    private static AbilitySeatState Seat(string id, string team, string ability) => new(
        new AbilitySyncSeat
        {
            PlayerId = id,
            SteamId = $"7656119800003{id[^1]}01",
            RosterTeam = team,
            InitialSide = team == "A" ? "CT" : "T",
            AbilityId = ability,
        },
        AbilityDefinitionCatalog.CreateProduction().Get(ability));
}
