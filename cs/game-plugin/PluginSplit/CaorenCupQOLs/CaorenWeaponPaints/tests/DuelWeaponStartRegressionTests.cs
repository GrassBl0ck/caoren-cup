using CaorenCup.WeaponPaints.Core;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class DuelWeaponStartRegressionTests
{
    [Theory]
    [InlineData("weapon_usp_silencer")]
    [InlineData("weapon_ak47")]
    [InlineData("weapon_awp")]
    [InlineData("weapon_knife")]
    [InlineData("weapon_smokegrenade")]
    public void Delayed_refresh_uses_the_production_executor_without_changing_equipment_or_selection(string active)
    {
        var weapons = new[] { new TestWeapon("weapon_knife", 0), new TestWeapon(active, 17) };
        var before = weapons.ToArray();
        var selectedWeapon = weapons[1];
        WeaponRefreshWorkflow.Apply(weapons, weapon => weapon.PaintApplications++);
        Assert.Equal(before, weapons);
        Assert.Same(weapons[1], selectedWeapon);
        Assert.Equal(17, weapons[1].Ammo);
        Assert.All(weapons, weapon => Assert.Equal(1, weapon.PaintApplications));
    }

    [Fact]
    public void Refresh_must_not_add_a_knife_that_the_game_mode_did_not_issue()
    {
        var weapons = new List<TestWeapon> { new("weapon_ak47", 17) };
        WeaponRefreshWorkflow.Apply(weapons, weapon => weapon.PaintApplications++);
        Assert.Single(weapons);
        Assert.DoesNotContain(weapons, weapon => weapon.Name.Contains("knife", StringComparison.Ordinal));
        Assert.Equal(17, weapons[0].Ammo);
        Assert.Equal(1, weapons[0].PaintApplications);
    }

    [Fact]
    public void Matching_identity_is_allowed() => Assert.True(WeaponRefreshWorkflow.CanApply(Identity, Identity));

    [Theory]
    [InlineData("steam")]
    [InlineData("connection")]
    [InlineData("respawn")]
    [InlineData("team")]
    [InlineData("round-map-stop")]
    public void Old_refresh_identity_is_rejected(string changed)
    {
        var current = changed switch
        {
            "steam" => Identity with { SteamId = Identity.SteamId + 1 },
            "connection" => Identity with { Controller = Identity.Controller + 32768 },
            "respawn" => Identity with { Pawn = Identity.Pawn + 32768 },
            "team" => Identity with { Team = 3 },
            "round-map-stop" => Identity with { Generation = Identity.Generation + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        Assert.False(WeaponRefreshWorkflow.CanApply(Identity, current));
    }

    [Fact]
    public void Invalid_entity_identity_is_rejected()
    {
        foreach (var identity in new[] {
            Identity with { Pawn = 0 }, Identity with { Pawn = uint.MaxValue },
            Identity with { Controller = 0 }, Identity with { Team = 1 } })
        {
            Assert.False(WeaponRefreshWorkflow.CanApply(identity, identity));
        }
    }

    [Fact]
    public void Production_refresh_and_gloves_have_no_inventory_recreation_or_weapon_selection()
    {
        var source = ReadSource("Game/WeaponAction.Upstream.cs");
        var refresh = Slice(source, "private void RefreshWeapons(", "private void GivePlayerGloves(");
        var gloves = Slice(source, "private void GivePlayerGloves(", "private static int GetRandomPaint(");
        Assert.Contains("WeaponRefreshWorkflow.Apply(", refresh);
        Assert.DoesNotContain("GiveNamedItem(", refresh);
        Assert.DoesNotContain("AddEntityIOEvent(", refresh);
        Assert.DoesNotContain("ExecuteClientCommand(", refresh + gloves);
        Assert.Contains("CanRefreshPlayer(player, identity)", gloves);

        var runtime = ReadSource("PluginRuntime.cs");
        var spawn = Slice(runtime, "private HookResult OnPlayerSpawn(", "private HookResult OnRoundStart(");
        Assert.True(spawn.IndexOf("CaptureRefreshIdentity(player)", StringComparison.Ordinal) <
                    spawn.IndexOf("AddTimer(0.2f", StringComparison.Ordinal));
        Assert.Contains("ApplyAll(player, identity)", spawn);
        Assert.Contains("player.EntityHandle.Raw, player.PlayerPawn.Raw", runtime);
        Assert.Contains("WeaponRefreshWorkflow.CanApply(identity, CaptureRefreshIdentity(player))", runtime);
        Assert.Contains("_runtimeActive && _gameDataReady", runtime);
    }

    private static readonly WeaponRefreshIdentity Identity = new(76561198000000001, 32769, 32770, 2, 4);

    private sealed class TestWeapon(string name, int ammo)
    {
        public string Name { get; } = name;
        public int Ammo { get; } = ammo;
        public int PaintApplications { get; set; }
    }

    private static string ReadSource(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var root = Path.Combine(dir.FullName, "game-plugin", "PluginSplit", "CaorenCupQOLs", "CaorenWeaponPaints");
            var file = Path.Combine(root, relative);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(relative);
    }

    private static string Slice(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first);
        return source[first..last];
    }
}