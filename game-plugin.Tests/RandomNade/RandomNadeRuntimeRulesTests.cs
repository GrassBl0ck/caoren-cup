using System.Numerics;
using CaorenCup.Features;
using Xunit;

namespace CaorenCup.GamePlugin.Tests.RandomNade;

public sealed class RandomNadeRuntimeRulesTests
{
    [Theory]
    [InlineData(RandomNadeType.Smoke, "smokegrenade_projectile", "weapon_smokegrenade", RandomNadeDetonationKind.WhenStopped)]
    [InlineData(RandomNadeType.Fire, "molotov_projectile", "weapon_molotov", RandomNadeDetonationKind.AfterDelay)]
    [InlineData(RandomNadeType.HighExplosive, "hegrenade_projectile", "weapon_hegrenade", RandomNadeDetonationKind.AfterDelay)]
    [InlineData(RandomNadeType.Flash, "flashbang_projectile", null, RandomNadeDetonationKind.Native)]
    [InlineData(RandomNadeType.Decoy, "decoy_projectile", "weapon_decoy", RandomNadeDetonationKind.WhenStopped)]
    public void GetPlan_MapsProjectileAndDetonationFallback(
        RandomNadeType type,
        string projectile,
        string? actionWeapon,
        RandomNadeDetonationKind detonationKind)
    {
        var plan = RandomNadeRuntimeRules.GetPlan(type);

        Assert.Equal(projectile, plan.ProjectileDesignerName);
        Assert.Equal(actionWeapon, plan.ActionWeaponDesignerName);
        Assert.Equal(detonationKind, plan.DetonationKind);
    }

    [Fact]
    public void ComputeLaunchVelocity_LevelAim_AddsTwelveDegreeUpwardArcAndPlayerVelocity()
    {
        var velocity = RandomNadeRuntimeRules.ComputeLaunchVelocity(
            pitchDegrees: 0,
            yawDegrees: 0,
            playerVelocity: new Vector3(10, 20, 30));

        Assert.Equal(10 + 675 * Math.Cos(12 * Math.PI / 180), velocity.X, 3);
        Assert.Equal(20, velocity.Y, 3);
        Assert.Equal(30 + 675 * Math.Sin(12 * Math.PI / 180), velocity.Z, 3);
    }

    [Fact]
    public void IsReadyToForceDetonate_DelayedType_WaitsOnePointFiveSeconds()
    {
        var plan = RandomNadeRuntimeRules.GetPlan(RandomNadeType.HighExplosive);

        Assert.False(RandomNadeRuntimeRules.IsReadyToForceDetonate(plan, ageSeconds: 1.49, speed: 500));
        Assert.True(RandomNadeRuntimeRules.IsReadyToForceDetonate(plan, ageSeconds: 1.5, speed: 500));
    }

    [Fact]
    public void IsReadyToForceDetonate_StopType_WaitsForLowSpeedButHasFailsafe()
    {
        var plan = RandomNadeRuntimeRules.GetPlan(RandomNadeType.Smoke);

        Assert.False(RandomNadeRuntimeRules.IsReadyToForceDetonate(plan, ageSeconds: 0, speed: 0));
        Assert.False(RandomNadeRuntimeRules.IsReadyToForceDetonate(plan, ageSeconds: 1, speed: 5));
        Assert.True(RandomNadeRuntimeRules.IsReadyToForceDetonate(plan, ageSeconds: 1, speed: 4.99));
        Assert.True(RandomNadeRuntimeRules.IsReadyToForceDetonate(plan, ageSeconds: 5, speed: 500));
    }

    [Theory]
    [InlineData("ak47", true)]
    [InlineData("weapon_awp", true)]
    [InlineData("hegrenade", false)]
    [InlineData("smokegrenade", false)]
    [InlineData("molotov", false)]
    [InlineData("flashbang", false)]
    [InlineData("decoy", false)]
    [InlineData("knife", false)]
    [InlineData("weapon_c4", false)]
    [InlineData("taser", false)]
    public void IsFirearm_ExcludesItemsThatMustNotRetrigger(string weaponName, bool expected)
    {
        Assert.Equal(expected, RandomNadeRuntimeRules.IsFirearm(weaponName));
    }
}
