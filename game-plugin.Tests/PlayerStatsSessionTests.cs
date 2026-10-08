using CaorenCup.Contracts;
using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class PlayerStatsMatchSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ignores_warmup_and_starts_only_when_runtime_is_live()
    {
        var session = new PlayerStatsMatchSession();

        Assert.Null(session.Observe(State("competitive", GameModeLifecycleState.Warmup), "de_dust2", Start));
        Assert.False(session.IsActive);

        Assert.Null(session.Observe(State("competitive", GameModeLifecycleState.Live), "de_dust2", Start.AddSeconds(15)));
        Assert.True(session.IsActive);
    }

    [Fact]
    public void Stops_live_session_and_captures_rows_once()
    {
        var session = new PlayerStatsMatchSession();
        session.Observe(State("competitive", GameModeLifecycleState.Live), "de_dust2", Start);
        session.Tracker.RegisterPlayer("p1", "Player", 2);
        session.Tracker.BeginRound(new[] { new PlayerStatsParticipant("p1", "Player", 2) });
        session.Tracker.RecordDamage("p1", 90, "weapon_ak47", null, 2, 0);

        var export = session.Observe(State("competitive", GameModeLifecycleState.Stopped), "de_dust2", Start.AddMinutes(20));

        Assert.NotNull(export);
        Assert.False(session.IsActive);
        Assert.Equal("competitive", export!.ModeId);
        Assert.Equal("de_dust2", export.MapName);
        Assert.Equal("stopped", export.EndReason);
        Assert.Equal(90, Assert.Single(export.Rows).TotalDamage);
        Assert.Null(session.Observe(State("competitive", GameModeLifecycleState.Stopped), "de_dust2", Start.AddMinutes(21)));
    }

    [Fact]
    public void Map_change_can_finish_an_active_session_without_duplicate_export()
    {
        var session = new PlayerStatsMatchSession();
        session.Observe(State("rush", GameModeLifecycleState.Live), "rush_001", Start);
        session.Tracker.RegisterPlayer("p1", "Player", 3);
        session.Tracker.RecordShot("p1", "weapon_ak47");

        var export = session.End("map-change", Start.AddMinutes(5));

        Assert.NotNull(export);
        Assert.Equal("map-change", export!.EndReason);
        Assert.False(session.IsActive);
        Assert.Null(session.End("map-change", Start.AddMinutes(6)));
    }

    [Fact]
    public void Map_change_does_not_restart_statistics_while_runtime_still_reports_live()
    {
        var session = new PlayerStatsMatchSession();
        session.Observe(State("competitive", GameModeLifecycleState.Live), "de_dust2", Start);
        session.End("map-change", Start.AddMinutes(1));
        session.SuppressLiveUntilLifecycleLeavesLive();

        Assert.Null(session.Observe(State("competitive", GameModeLifecycleState.Live), "de_nuke", Start.AddMinutes(2)));
        Assert.False(session.IsActive);
        Assert.Null(session.Observe(State("competitive", GameModeLifecycleState.Warmup), "de_nuke", Start.AddMinutes(3)));
        Assert.Null(session.Observe(State("competitive", GameModeLifecycleState.Live), "de_nuke", Start.AddMinutes(4)));
        Assert.True(session.IsActive);
    }

    [Fact]
    public void Starting_a_new_live_mode_resets_previous_session_data_after_stop()
    {
        var session = new PlayerStatsMatchSession();
        session.Observe(State("competitive", GameModeLifecycleState.Live), "de_dust2", Start);
        session.Tracker.RegisterPlayer("old", "Old", 2);
        session.End("stopped", Start.AddMinutes(1));

        session.Observe(State("rush", GameModeLifecycleState.Live), "rush_001", Start.AddMinutes(2));

        Assert.Empty(session.Tracker.Snapshot());
        Assert.Equal("rush", session.Current!.ModeId);
        Assert.Equal("rush_001", session.Current.MapName);
    }

    private static ActiveGameModeState State(string mode, GameModeLifecycleState lifecycle) =>
        new(mode, lifecycle, 15, 1);
}

public sealed class PlayerStatsCsvExporterTests
{
    [Fact]
    public void Writes_utf8_bom_csv_with_session_filename_and_escaped_player_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "caoren-player-stats-tests", Guid.NewGuid().ToString("N"));
        var export = new PlayerStatsMatchExport(
            "competitive",
            "de_dust2",
            new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 12, 20, 0, TimeSpan.Zero),
            "stopped",
            new[]
            {
                new PlayerStatsRow("765", "Name, \"quoted\"", 2, false, 90, 20, 2, 10, 3),
            });

        try
        {
            var path = PlayerStatsCsvExporter.Export(export, root);
            var bytes = File.ReadAllBytes(path);
            var text = File.ReadAllText(path);

            Assert.EndsWith("player-stats_20261008_120000_competitive_de_dust2.csv", path);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
            Assert.Contains("SteamId,PlayerName,Team,RoundsParticipated,TotalDamage,AverageDamagePerRound,UtilityDamage,Shots,Hits,HitPercentage", text);
            Assert.Contains("765,\"Name, \"\"quoted\"\"\",2,2,90,45.0,20,10,3,30.0", text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
