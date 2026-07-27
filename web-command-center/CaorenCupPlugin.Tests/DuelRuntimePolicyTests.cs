using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class DuelRuntimePolicyTests
{
    [Theory]
    [InlineData("Pistol", "", "weapon_usp_silencer", "weapon_usp_silencer", "Secondary")]
    [InlineData("Rifle", "weapon_ak47", "weapon_deagle", "weapon_ak47", "Primary")]
    [InlineData("Sniper", "weapon_awp", "", "weapon_awp", "Primary")]
    public void BuildLoadoutRule_selects_stage_weapon(
        string stageName, string primary, string secondary, string preferred, string slotName)
    {
        var stage = Enum.Parse<DuelStage>(stageName);
        var slot = Enum.Parse<DuelPreferredWeaponSlot>(slotName);
        var rule = DuelRuntimePolicy.BuildLoadoutRule(stage, primary, secondary);
        Assert.Equal(preferred, rule.PreferredWeapon);
        Assert.Equal(slot, rule.PreferredSlot);
        Assert.Contains(preferred, rule.AllowedFirearms);
    }

    [Theory]
    [InlineData("weapon_ak47", true)]
    [InlineData("weapon_deagle", true)]
    [InlineData("weapon_knife", false)]
    [InlineData("weapon_flashbang", false)]
    [InlineData("item_assaultsuit", false)]
    public void ShouldBlockDrop_blocks_only_firearms(string item, bool expected) =>
        Assert.Equal(expected, DuelRuntimePolicy.ShouldBlockDrop(item));

    [Fact]
    public void Engine_round_limit_reserves_one_round() =>
        Assert.Equal(37, DuelRuntimePolicy.EngineRoundLimit(36));

    [Fact]
    public void Cvar_plan_contains_weapon_guards_and_reserved_round()
    {
        var plan = DuelRuntimePolicy.BuildCvarPlan(new DuelGameConfig(8, 16, 12, 1.25, "random2"));
        Assert.Contains(plan, item => item is { Name: "mp_maxrounds", Value: "37" });
        Assert.Contains(plan, item => item is { Name: "mp_weapons_allow_map_placed", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_death_drop_gun", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_roundtime", Value: "1.25" });
    }

    [Fact]
    public void BuildLoadoutRule_allows_only_the_configured_firearms()
    {
        var rule = DuelRuntimePolicy.BuildLoadoutRule(
            DuelStage.Rifle,
            "weapon_ak47",
            "weapon_deagle");

        Assert.Contains("weapon_ak47", rule.AllowedFirearms);
        Assert.Contains("weapon_deagle", rule.AllowedFirearms);
        Assert.DoesNotContain("weapon_awp", rule.AllowedFirearms);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void CanActivateRuntime_allows_active_scope_reuse_for_confirmed_takeover(
        bool cvarScopeReady,
        bool duelRuntimeActive,
        bool expected)
    {
        Assert.Equal(
            expected,
            DuelRuntimePolicy.CanActivateRuntime(cvarScopeReady, duelRuntimeActive));
    }
}
