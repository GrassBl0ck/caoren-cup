using CaorenCupPlugin.MatchRuntime;
using CaorenCupPlugin.Practice;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class PracticeRuntimeTests
{
    [Fact]
    public void Practice_is_mutually_exclusive_with_formal_match_runtime()
    {
        var runtime = CreateRuntime(out _);

        var blocked = runtime.Start("de_mirage", formalMatchActive: true);
        var started = runtime.Start("de_mirage", formalMatchActive: false);
        var duplicate = runtime.Start("de_mirage", formalMatchActive: false);

        Assert.Equal("FORMAL_MATCH_ACTIVE", blocked.Code);
        Assert.True(started.Ok);
        Assert.Equal(PracticeStage.Active, runtime.Stage);
        Assert.Equal("ALREADY_ACTIVE", duplicate.Code);
    }

    [Fact]
    public void Practice_captures_cvars_once_and_restores_them_on_stop()
    {
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sv_cheats"] = "0",
            ["sv_infinite_ammo"] = "0",
            ["mp_roundtime"] = "1.92",
        };
        var commands = new List<string>();
        var runtime = new PracticeRuntime(
            (name, fallback) => current.GetValueOrDefault(name, fallback),
            command => commands.Add(command));

        runtime.Start("de_mirage", formalMatchActive: false);
        var stopped = runtime.Stop();

        Assert.True(stopped.Ok);
        Assert.Contains("sv_cheats 1", commands);
        Assert.Contains("sv_infinite_ammo 1", commands);
        Assert.Contains("mp_roundtime 60", commands);
        Assert.Contains("mp_warmup_end", commands);
        Assert.Contains("mp_restartgame 1", commands);
        Assert.Contains("sv_cheats 0", commands);
        Assert.Contains("sv_infinite_ammo 0", commands);
        Assert.Contains("mp_roundtime 1.92", commands);
        Assert.Equal(PracticeStage.Inactive, runtime.Stage);
    }

    [Fact]
    public void Practice_actions_emit_only_fixed_server_commands()
    {
        var runtime = CreateRuntime(out var commands);
        runtime.Start("de_mirage", formalMatchActive: false);

        Assert.True(runtime.RethrowLastGrenade().Ok);
        Assert.True(runtime.ClearGrenades().Ok);
        Assert.True(runtime.AddBot(crouched: true).Ok);
        Assert.True(runtime.RemoveBots().Ok);
        Assert.True(runtime.TeleportToSpawn(PracticeSpawnSide.CounterTerrorist).Ok);
        Assert.True(runtime.SetGodMode(enabled: true).Ok);
        Assert.True(runtime.SetImpacts(enabled: true).Ok);
        Assert.True(runtime.SetTrajectory(enabled: false).Ok);

        Assert.Contains("sv_rethrow_last_grenade", commands);
        Assert.Contains("ent_fire smokegrenade_projectile kill", commands);
        Assert.Contains("bot_add", commands);
        Assert.Contains("bot_crouch 1", commands);
        Assert.Contains("bot_kick", commands);
        Assert.Contains("setpos_player @me ct", commands);
        Assert.Contains("god 1", commands);
        Assert.Contains("sv_showimpacts 1", commands);
        Assert.Contains("sv_grenade_trajectory_prac_pipreview 0", commands);
        Assert.DoesNotContain(commands, command => command.Contains(';') || command.Contains('\n'));
    }

    [Fact]
    public void Practice_actions_are_rejected_when_runtime_is_inactive()
    {
        var runtime = CreateRuntime(out _);

        Assert.Equal("PRACTICE_INACTIVE", runtime.RethrowLastGrenade().Code);
        Assert.Equal("PRACTICE_INACTIVE", runtime.AddBot(crouched: false).Code);
    }

    [Fact]
    public async Task Lineups_are_utf8_and_isolated_by_steam_and_map()
    {
        using var temp = new TemporaryDirectory();
        var store = new PracticeLineupStore(temp.Path);
        var lineup = new PracticeLineup("smoke-a", "A点烟", "封警家", "smokegrenade", 1, 2, 3, 4, 5);

        await store.SaveAsync("76561198000000001", "de_mirage", lineup);

        Assert.Equal(lineup, Assert.Single(await store.ListAsync("76561198000000001", "de_mirage")));
        Assert.Empty(await store.ListAsync("76561198000000002", "de_mirage"));
        Assert.Empty(await store.ListAsync("76561198000000001", "de_inferno"));
        Assert.Contains("A点烟", File.ReadAllText(Assert.Single(Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories))));
    }

    [Fact]
    public async Task Corrupt_lineup_primary_falls_back_to_previous_copy()
    {
        using var temp = new TemporaryDirectory();
        var store = new PracticeLineupStore(temp.Path);
        var first = new PracticeLineup("first", "第一版", null, "flashbang", 1, 2, 3, 4, 5);
        var second = first with { Name = "第二版" };
        await store.SaveAsync("76561198000000001", "de_mirage", first);
        await store.SaveAsync("76561198000000001", "de_mirage", second);
        var primary = Assert.Single(
            Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories),
            path => !path.EndsWith(".previous.json", StringComparison.Ordinal));
        File.WriteAllText(primary, "{broken");

        var restored = await store.ListAsync("76561198000000001", "de_mirage");

        Assert.Equal("第一版", Assert.Single(restored).Name);
    }

    [Theory]
    [InlineData("../outside", "de_mirage")]
    [InlineData("76561198000000001", "../de_mirage")]
    [InlineData("bad", "de_mirage")]
    public async Task Lineup_paths_reject_untrusted_steam_or_map_values(string steamId, string mapName)
    {
        using var temp = new TemporaryDirectory();
        var store = new PracticeLineupStore(temp.Path);

        await Assert.ThrowsAsync<ArgumentException>(() => store.ListAsync(steamId, mapName));
    }

    [Theory]
    [InlineData(".prac", PracticeCommandKind.Start)]
    [InlineData(".exitprac", PracticeCommandKind.Stop)]
    [InlineData(".rethrow", PracticeCommandKind.Rethrow)]
    [InlineData(".last", PracticeCommandKind.Last)]
    [InlineData(".clear", PracticeCommandKind.Clear)]
    [InlineData(".bot", PracticeCommandKind.AddBot)]
    [InlineData(".cbot", PracticeCommandKind.AddCrouchedBot)]
    [InlineData(".nobots", PracticeCommandKind.RemoveBots)]
    [InlineData(".ctspawn", PracticeCommandKind.CounterTerroristSpawn)]
    [InlineData(".tspawn", PracticeCommandKind.TerroristSpawn)]
    [InlineData(".god", PracticeCommandKind.God)]
    [InlineData(".impacts", PracticeCommandKind.Impacts)]
    [InlineData(".traj", PracticeCommandKind.Trajectory)]
    public void Practice_parser_keeps_legacy_commands(string text, PracticeCommandKind expected)
    {
        var parsed = PracticeCommandParser.Parse(text);

        Assert.True(parsed.Ok);
        Assert.Equal(expected, parsed.Command?.Kind);
    }

    [Fact]
    public void Practice_parser_supports_lineup_crud_without_free_form_server_commands()
    {
        Assert.Equal(PracticeCommandKind.SaveLineup, PracticeCommandParser.Parse(".savenade A点烟").Command?.Kind);
        Assert.Equal(PracticeCommandKind.LoadLineup, PracticeCommandParser.Parse(".loadnade smoke-a").Command?.Kind);
        Assert.Equal(PracticeCommandKind.DeleteLineup, PracticeCommandParser.Parse(".deletenade smoke-a").Command?.Kind);
        Assert.Equal("UNSAFE_COMMAND", PracticeCommandParser.Parse(".loadnade x;quit").Code);
        Assert.Equal("UNKNOWN_COMMAND", PracticeCommandParser.Parse(".rcon quit").Code);
    }

    private static PracticeRuntime CreateRuntime(out List<string> commands)
    {
        commands = [];
        var output = commands;
        return new PracticeRuntime((_, fallback) => fallback, command => output.Add(command));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caoren-practice-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
