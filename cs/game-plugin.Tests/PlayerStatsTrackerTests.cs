using Xunit;
using CaorenCup.Features.InGameMenu;

namespace CaorenCup.GamePlugin.Tests;

public sealed class PlayerStatsTrackerTests
{
    [Fact]
    public void Accumulates_total_and_utility_damage()
    {
        var tracker = new PlayerStatsTracker();
        tracker.RegisterPlayer("victim", "Victim", 2);

        tracker.RecordDamage("victim", 100, "weapon_ak47", null, 2, 0);
        tracker.RecordDamage("victim", 40, "weapon_hegrenade", null, 2, 0);

        var row = Assert.Single(tracker.Snapshot());
        Assert.Equal(140, row.TotalDamage);
        Assert.Equal(40, row.UtilityDamage);
    }

    [Fact]
    public void Counts_all_damage_sources_but_only_enemy_bullet_hits()
    {
        var tracker = new PlayerStatsTracker();
        tracker.RegisterPlayer("victim", "Victim", 2);
        tracker.RegisterPlayer("attacker", "Attacker", 3);

        tracker.RecordDamage("victim", 10, "world", null, 2, 0);
        tracker.RecordDamage("victim", 20, "fall", "victim", 2, 2);
        tracker.RecordDamage("victim", 30, "weapon_glock", "attacker", 2, 3);
        tracker.RecordDamage("victim", 20, "weapon_knife", "attacker", 2, 3);

        var victim = tracker.Snapshot().Single(row => row.SteamId == "victim");
        var attacker = tracker.Snapshot().Single(row => row.SteamId == "attacker");
        Assert.Equal(80, victim.TotalDamage);
        Assert.Equal(1, attacker.Hits);
    }

    [Fact]
    public void Counts_zero_damage_rounds_in_average()
    {
        var tracker = new PlayerStatsTracker();
        tracker.BeginRound(new[] { new PlayerStatsParticipant("p1", "Player", 2) });
        tracker.RecordDamage("p1", 90, "weapon_m4a1", null, 2, 0);
        tracker.BeginRound(new[] { new PlayerStatsParticipant("p1", "Player", 2) });

        var row = Assert.Single(tracker.Snapshot());
        Assert.Equal(2, row.RoundsParticipated);
        Assert.Equal(45d, row.AverageDamagePerRound);
    }

    [Fact]
    public void Filters_shots_and_calculates_hit_percentage()
    {
        var tracker = new PlayerStatsTracker();
        tracker.RegisterPlayer("p1", "Player", 2);

        tracker.RecordShot("p1", "weapon_ak47");
        tracker.RecordShot("p1", "weapon_knife");
        tracker.RecordShot("p1", "weapon_hegrenade");
        tracker.RecordShot("enemy", "weapon_ak47");
        tracker.RecordDamage("p1", 10, "weapon_ak47", "enemy", 2, 3);
        tracker.RecordDamage("p1", 10, "weapon_ak47", "enemy", 2, 3);

        var row = tracker.Snapshot().Single(row => row.SteamId == "p1");
        var enemy = tracker.Snapshot().Single(row => row.SteamId == "enemy");
        Assert.Equal(1, row.Shots);
        Assert.Equal(0, row.Hits);
        Assert.Equal(2, enemy.Hits);
        Assert.Equal(200d, enemy.HitPercentage);
    }

    [Fact]
    public void Keeps_disconnected_players_until_reset()
    {
        var tracker = new PlayerStatsTracker();
        tracker.RegisterPlayer("p1", "Player", 2);
        tracker.RecordDamage("p1", 10, "weapon_ak47", null, 2, 0);
        tracker.MarkDisconnected("p1");

        var row = Assert.Single(tracker.Snapshot());
        Assert.False(row.Connected);
        Assert.Equal(10, row.TotalDamage);

        tracker.Reset();
        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void Raises_change_notifications_for_live_menu_refresh()
    {
        var tracker = new PlayerStatsTracker();
        var changes = 0;
        tracker.Changed += () => changes++;

        tracker.RegisterPlayer("p1", "Player", 2);
        tracker.RecordShot("p1", "weapon_ak47");
        tracker.MarkDisconnected("p1");

        Assert.Equal(3, changes);
    }
    [Fact]
    public void Formats_average_and_empty_hit_percentage_for_the_menu()
    {
        var tracker = new PlayerStatsTracker();
        tracker.BeginRound(new[] { new PlayerStatsParticipant("p1", "Player", 2) });
        var row = Assert.Single(tracker.Snapshot());

        Assert.Equal("0.0", row.FormatAverageDamage());
        Assert.Equal("—", row.FormatHitPercentage());
    }


    [Theory]
    [InlineData("ak47", true)]
    [InlineData("hkp2000", true)]
    [InlineData("m4a1_silencer", true)]
    [InlineData("weapon_awp", true)]
    [InlineData("weapon_nova", true)]
    [InlineData("weapon_knife_karambit", false)]
    [InlineData("bayonet", false)]
    [InlineData("hegrenade", false)]
    [InlineData("weapon_taser", false)]
    [InlineData("weapon_unknown", false)]
    public void Classifies_game_event_names_and_designer_names(string weapon, bool expected)
        => Assert.Equal(expected, PlayerStatsTracker.IsBulletWeapon(weapon));

    [Theory]
    [InlineData("hegrenade")]
    [InlineData("weapon_hegrenade")]
    [InlineData("inferno")]
    [InlineData("molotov")]
    [InlineData("incgrenade")]
    [InlineData("smokegrenade")]
    [InlineData("flashbang")]
    [InlineData("decoy")]
    public void Classifies_utility_damage_event_names(string weapon)
        => Assert.True(PlayerStatsTracker.IsUtilityDamageWeapon(weapon));

    [Fact]
    public void Repeated_metadata_updates_do_not_emit_duplicate_notifications()
    {
        var tracker = new PlayerStatsTracker();
        var count = 0;
        tracker.Changed += () => count++;
        tracker.RegisterPlayer("p1", "Player", 2);
        tracker.RegisterPlayer("p1", "Player", 2);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Enemy_hits_accept_actual_event_weapon_names_and_ignore_teammates()
    {
        var tracker = new PlayerStatsTracker();
        tracker.RegisterPlayer("shooter", "Shooter", 2);
        tracker.RegisterPlayer("enemy", "Enemy", 3);
        tracker.RecordShot("shooter", "ak47");
        tracker.RecordDamage("enemy", 10, "ak47", "shooter", 3, 2);
        tracker.RecordDamage("enemy", 10, "ak47", "shooter", 3, 2);
        tracker.RecordDamage("enemy", 10, "ak47", "shooter", 3, 3);
        var shooter = tracker.Snapshot().Single(row => row.SteamId == "shooter");
        Assert.Equal(1, shooter.Shots);
        Assert.Equal(2, shooter.Hits);
        Assert.Equal("200.0%", shooter.FormatHitPercentage());
    }
    [Fact]
    public void Sorts_rows_by_team_then_name_and_keeps_last_known_name()
    {
        var tracker = new PlayerStatsTracker();
        tracker.RegisterPlayer("ct-b", "Bravo", 3);
        tracker.RegisterPlayer("t-b", "Bravo", 2);
        tracker.RegisterPlayer("t-a", "Alpha", 2);
        tracker.RegisterPlayer("spec", "Spectator", 1);
        tracker.MarkDisconnected("ct-b");

        Assert.Equal(new[] { "spec", "t-a", "t-b", "ct-b" }, tracker.Snapshot().Select(row => row.SteamId));
        Assert.Equal("Bravo", tracker.Snapshot().Single(row => row.SteamId == "ct-b").Name);
        Assert.False(tracker.Snapshot().Single(row => row.SteamId == "ct-b").Connected);
    }

}
