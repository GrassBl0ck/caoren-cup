using Caoren;
using Caoren.AbilityMode;
using CaorenCup;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRoleRegistryTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Six_basic_role_switches_default_to_enabled()
    {
        var settings = new AbilityRoleSettings();

        Assert.True(settings.MedicEnabled);
        Assert.True(settings.BerserkerEnabled);
        Assert.True(settings.TankEnabled);
        Assert.True(settings.CapitalistEnabled);
        Assert.True(settings.BalanceEnabled);
        Assert.True(settings.CommanderEnabled);
    }

    [Fact]
    public void Missing_role_settings_object_uses_safe_enabled_defaults()
    {
        var registry = AbilityRoleRegistry.Create(null);

        Assert.All(registry.RegisteredRoleIds, roleId => Assert.True(registry.IsEnabled(roleId)));
    }

    [Fact]
    public void Registry_contains_only_the_six_phase_4a_roles()
    {
        var registry = AbilityRoleRegistry.Create(new AbilityRoleSettings());

        Assert.Equal(
            ["balance", "berserker", "capitalist", "commander", "medic", "tank"],
            registry.RegisteredRoleIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Selected_disabled_role_rejects_runtime_with_explicit_role_id()
    {
        var settings = new AbilityRoleSettings { MedicEnabled = false };
        var registry = AbilityRoleRegistry.Create(settings);

        var result = registry.ValidateSelectedRoles(Config("medic"));

        Assert.False(result.Ok);
        Assert.Equal("ROLE_DISABLED", result.Code);
        Assert.Contains("medic", result.Message);
    }

    [Fact]
    public void Selected_future_role_is_recognized_but_rejected_until_implemented()
    {
        var registry = AbilityRoleRegistry.Create(new AbilityRoleSettings());

        var result = registry.ValidateSelectedRoles(Config("assassin"));

        Assert.False(result.Ok);
        Assert.Equal("ROLE_NOT_IMPLEMENTED", result.Code);
        Assert.Contains("assassin", result.Message);
    }

    private static AbilitySyncConfig Config(string abilityId) => new()
    {
        ProtocolVersion = 1,
        SyncId = "sync-1",
        MatchId = "match-1",
        Revision = 1,
        CatalogVersion = "ability-catalog-v1",
        AbilityModeEnabled = true,
        ContentDigest = Digest,
        Seats = [new()
        {
            PlayerId = "A1",
            SteamId = "76561198000000001",
            RosterTeam = "A",
            InitialSide = "CT",
            AbilityId = abilityId,
        }],
    };
}
