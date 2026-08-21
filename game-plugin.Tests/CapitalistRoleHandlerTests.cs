using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CapitalistRoleHandlerTests
{
    [Theory]
    [InlineData(EconomySource.KillReward)]
    [InlineData(EconomySource.RoundReward)]
    [InlineData(EconomySource.BombPlantReward)]
    [InlineData(EconomySource.BombDefuseReward)]
    [InlineData(EconomySource.ObjectiveReward)]
    public void Normal_match_rewards_are_doubled_without_recursion(EconomySource source)
    {
        var player = Player("A1", "A", "capitalist", 1_000, connected: true);
        var handler = new CapitalistRoleHandler();

        var credited = handler.ApplyReward(player, 300, source, maximumMoney: 16_000);

        Assert.Equal(600, credited);
        Assert.Equal(1_600, player.Money);
    }

    [Theory]
    [InlineData(EconomySource.Refund)]
    [InlineData(EconomySource.AdminGrant)]
    [InlineData(EconomySource.ChargeRefund)]
    [InlineData(EconomySource.RoleTransfer)]
    [InlineData(EconomySource.CapitalistUltimate)]
    public void Non_normal_rewards_are_never_doubled(EconomySource source)
    {
        var player = Player("A1", "A", "capitalist", 1_000, true);

        var credited = new CapitalistRoleHandler().ApplyReward(player, 300, source, 16_000);

        Assert.Equal(300, credited);
        Assert.Equal(1_300, player.Money);
    }

    [Fact]
    public void Reward_overflow_disappears_at_server_money_cap()
    {
        var player = Player("A1", "A", "capitalist", 15_900, true);

        Assert.Equal(100, new CapitalistRoleHandler().ApplyReward(player, 300, EconomySource.KillReward, 16_000));
        Assert.Equal(16_000, player.Money);
    }

    [Fact]
    public void Adapter_observed_normal_reward_adds_only_the_matching_bonus()
    {
        var player = Player("A1", "A", "capitalist", 1_300, true);

        var bonus = new CapitalistRoleHandler().ApplyObservedRewardBonus(
            player,
            previousConfirmedBalance: 1_000,
            EconomySource.KillReward,
            maximumMoney: 16_000);

        Assert.Equal(300, bonus);
        Assert.Equal(1_600, player.Money);
    }

    [Theory]
    [InlineData(8, 0.20)]
    [InlineData(9, 0.2333333333)]
    [InlineData(11, 0.30)]
    [InlineData(14, 0.40)]
    public void Ultimate_ratio_is_linear_between_confirmed_charge_points(int charge, double expected)
    {
        Assert.Equal(expected, CapitalistRoleHandler.GetPlunderRatio(charge), 8);
    }

    [Fact]
    public void Plan_includes_alive_dead_and_disconnected_seats_with_per_enemy_and_per_recipient_flooring()
    {
        var caster = Player("A1", "A", "capitalist", 1_000, true);
        var deadAlly = Player("A2", "A", "medic", 15_950, true, alive: false);
        var disconnectedAlly = Player("A3", "A", "tank", 100, false);
        var enemyOne = Player("B1", "B", "tank", 1_001, true);
        var enemyOffline = Player("B2", "B", "balance", 2_002, false);
        var players = new[] { caster, deadAlly, disconnectedAlly, enemyOne, enemyOffline };
        var handler = new CapitalistRoleHandler();

        var plan = handler.BuildPlunderPlan(caster.Seat, players, charge: 11, maximumMoney: 16_000);

        Assert.True(plan.Ok);
        Assert.Equal(900, plan.TotalPlundered); // floor(1001*30%) + floor(2002*30%)
        Assert.Equal(300, plan.SharePerFriendlySeat);
        Assert.True(handler.TryApplyPlan(plan, players, "match-1:round-2:A1", 16_000));
        Assert.Equal(1_300, caster.Money);
        Assert.Equal(16_000, deadAlly.Money); // 250 溢出消失，不重新分配
        Assert.Equal(400, disconnectedAlly.Money);
        Assert.Equal(701, enemyOne.Money);
        Assert.Equal(1_402, enemyOffline.Money);
    }

    [Fact]
    public void Zero_plunder_fails_without_consumption_plan()
    {
        var caster = Player("A1", "A", "capitalist", 0, true);
        var enemy = Player("B1", "B", "tank", 0, false);

        var plan = new CapitalistRoleHandler().BuildPlunderPlan(caster.Seat, [caster, enemy], 14, 16_000);

        Assert.False(plan.Ok);
        Assert.Equal("NO_PLUNDERABLE_MONEY", plan.Code);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Plan_application_is_all_or_nothing_and_operation_id_is_idempotent()
    {
        var caster = Player("A1", "A", "capitalist", 1_000, true);
        var enemy = Player("B1", "B", "tank", 1_000, true);
        var players = new[] { caster, enemy };
        var handler = new CapitalistRoleHandler();
        var plan = handler.BuildPlunderPlan(caster.Seat, players, 14, 16_000);
        enemy.Money = 999;

        Assert.False(handler.TryApplyPlan(plan, players, "operation-1", 16_000));
        Assert.Equal(1_000, caster.Money);
        Assert.Equal(999, enemy.Money);

        enemy.Money = 1_000;
        Assert.True(handler.TryApplyPlan(plan, players, "operation-1", 16_000));
        var after = players.Select(player => player.Money).ToArray();
        Assert.False(handler.TryApplyPlan(plan, players, "operation-1", 16_000));
        Assert.Equal(after, players.Select(player => player.Money));
    }

    private static AbilityRolePlayer Player(
        string id,
        string team,
        string ability,
        int money,
        bool connected,
        bool alive = true) => new(Seat(id, team, ability))
        {
            Money = money,
            IsConnected = connected,
            IsAlive = alive,
        };

    private static AbilitySeatState Seat(string id, string team, string ability) => new(
        new AbilitySyncSeat
        {
            PlayerId = id,
            SteamId = $"7656119800002{id[^1]}01",
            RosterTeam = team,
            InitialSide = team == "A" ? "CT" : "T",
            AbilityId = ability,
        },
        AbilityDefinitionCatalog.CreateProduction().Get(ability));
}
