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
    public void Engine_round_limit_allows_native_post_match_at_configured_total() =>
        Assert.Equal(36, DuelRuntimePolicy.EngineRoundLimit(36));

    [Fact]
    public void Cvar_plan_contains_native_post_match_and_duel_safety_settings()
    {
        var plan = DuelRuntimePolicy.BuildCvarPlan(new DuelGameConfig(8, 16, 12, 1.25, "random2"));
        Assert.Contains(plan, item => item is { Name: "mp_maxrounds", Value: "36" });
        Assert.Contains(plan, item => item is { Name: "mp_weapons_allow_map_placed", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_death_drop_gun", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_roundtime", Value: "1.25" });
        Assert.Contains(plan, item => item is { Name: "sv_showimpacts", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "sv_showimpacts_time", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_overtime_enable", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_endmatch_votenextmap", Value: "0" });
        Assert.Contains(plan, item => item is { Name: "mp_match_end_restart", Value: "1" });
    }

    [Fact]
    public void Web_managed_cvar_plan_keeps_the_reserved_round_without_native_post_match()
    {
        var method = typeof(DuelRuntimePolicy).GetMethod(
            "BuildWebManagedCvarPlan",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var plan = Assert.IsAssignableFrom<IReadOnlyList<DuelCvarSetting>>(
            method!.Invoke(null, [new DuelGameConfig(8, 16, 12, 1.25, "random2")]));
        Assert.Contains(plan, item => item is { Name: "mp_maxrounds", Value: "37" });
        Assert.DoesNotContain(plan, item => item.Name == "mp_match_end_restart");
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

    [Fact]
    public void BuildSteamBoundLoadoutPlans_preserves_each_players_weapons_independent_of_order()
    {
        var inputs = new[]
        {
            new DuelPlayerLoadoutInput("76561198000000002", "weapon_m4a1", "weapon_deagle"),
            new DuelPlayerLoadoutInput("76561198000000001", "weapon_ak47", "weapon_usp_silencer"),
        };

        var plans = DuelRuntimePolicy.BuildSteamBoundLoadoutPlans(DuelStage.Rifle, inputs.Reverse());

        Assert.Equal("weapon_ak47", plans["76561198000000001"].Primary);
        Assert.Equal("weapon_usp_silencer", plans["76561198000000001"].Secondary);
        Assert.Equal("weapon_m4a1", plans["76561198000000002"].Primary);
        Assert.Equal("weapon_deagle", plans["76561198000000002"].Secondary);
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
