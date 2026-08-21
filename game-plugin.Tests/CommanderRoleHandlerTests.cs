using Caoren;
using Caoren.AbilityMode;
using System.Numerics;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CommanderRoleHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Fact]
    public void Passive_selects_nearest_alive_enemy_by_three_dimensional_rounded_distance_without_name_or_direction()
    {
        var handler = new CommanderRoleHandler();
        var commander = Player("A1", "A", "commander", "指挥官本人", new(0, 0, 0));
        var horizontal = Player("B1", "B", "assassin", "隐身刺客", new(3, 4, 0));
        var vertical = Player("B2", "B", "tank", "坦克", new(0, 0, 4.4f));
        var deadNear = Player("B3", "B", "medic", "死亡医师", new(0, 0, 1), alive: false);

        var hud = handler.BuildHud(commander, [commander, horizontal, vertical, deadNear], Now);

        Assert.Equal(1, hud.VisibleEnemyCount);
        Assert.Contains("4", hud.Text);
        Assert.DoesNotContain("坦克", hud.Text);
        Assert.DoesNotContain("隐身刺客", hud.Text);
        Assert.DoesNotContain("方向", hud.Text);
        Assert.DoesNotContain(",", hud.Text);
    }

    [Fact]
    public void Dead_commander_stops_displaying_passive_information()
    {
        var commander = Player("A1", "A", "commander", "指挥官", Vector3.Zero, alive: false);
        var enemy = Player("B1", "B", "tank", "坦克", new(1, 0, 0));

        Assert.Equal(string.Empty, new CommanderRoleHandler().BuildHud(commander, [commander, enemy], Now).Text);
    }

    [Fact]
    public void Ultimate_shows_all_alive_enemies_sorted_by_distance_with_names_only()
    {
        var handler = new CommanderRoleHandler();
        var commander = Player("A1", "A", "commander", "指挥官", Vector3.Zero);
        var far = Player("B1", "B", "assassin", "远方刺客", new(0, 0, 10));
        var near = Player("B2", "B", "tank", "近处坦克", new(3, 4, 0));
        handler.ActivateUltimate(commander.Seat, Now);

        var hud = handler.BuildHud(commander, [commander, far, near], Now.AddSeconds(1));

        Assert.True(hud.IsUltimateView);
        Assert.Equal(2, hud.VisibleEnemyCount);
        Assert.True(hud.Text.IndexOf("近处坦克", StringComparison.Ordinal) < hud.Text.IndexOf("远方刺客", StringComparison.Ordinal));
        Assert.Contains("5", hud.Text);
        Assert.Contains("10", hud.Text);
        Assert.DoesNotContain("(3", hud.Text);
        Assert.DoesNotContain("方向", hud.Text);
    }

    [Fact]
    public void Ultimate_allows_empty_cast_and_newly_alive_enemy_joins_next_shared_hud_refresh()
    {
        var handler = new CommanderRoleHandler();
        var commander = Player("A1", "A", "commander", "指挥官", Vector3.Zero);
        var enemy = Player("B1", "B", "tank", "坦克", new(5, 0, 0), alive: false);

        var result = handler.ActivateUltimate(commander.Seat, Now, hasAliveEnemies: false);
        Assert.True(result.Ok);
        Assert.True(result.EmptyCast);
        Assert.Equal(0, handler.BuildHud(commander, [commander, enemy], Now.AddSeconds(1)).VisibleEnemyCount);

        enemy.IsAlive = true;
        Assert.Equal(1, handler.BuildHud(commander, [commander, enemy], Now.AddSeconds(1.5)).VisibleEnemyCount);
    }

    [Fact]
    public void Ultimate_expires_at_ten_seconds_and_cleanup_removes_recoverable_state()
    {
        var handler = new CommanderRoleHandler();
        var commander = Player("A1", "A", "commander", "指挥官", Vector3.Zero);
        var enemy = Player("B1", "B", "tank", "坦克", new(5, 0, 0));
        handler.ActivateUltimate(commander.Seat, Now);

        Assert.True(handler.BuildHud(commander, [commander, enemy], Now.AddSeconds(9.9)).IsUltimateView);
        Assert.False(handler.BuildHud(commander, [commander, enemy], Now.AddSeconds(10)).IsUltimateView);

        handler.Clear(commander.Seat);
        Assert.False(commander.Seat.RecoverableUntil.ContainsKey(CommanderRoleHandler.UltimateStateKey));
    }

    private static AbilityRolePlayer Player(
        string id,
        string team,
        string ability,
        string name,
        Vector3 position,
        bool alive = true) => new(Seat(id, team, ability))
        {
            DisplayName = name,
            Position = position,
            IsAlive = alive,
        };

    private static AbilitySeatState Seat(string id, string team, string ability) => new(
        new AbilitySyncSeat
        {
            PlayerId = id,
            SteamId = $"7656119800004{id[^1]}01",
            RosterTeam = team,
            InitialSide = team == "A" ? "CT" : "T",
            AbilityId = ability,
        },
        AbilityDefinitionCatalog.CreateProduction().Get(ability));
}
