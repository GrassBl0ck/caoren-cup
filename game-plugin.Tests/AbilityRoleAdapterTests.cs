using Caoren.AbilityMode;
using System.Numerics;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRoleAdapterTests
{
    [Theory]
    [InlineData("weapon_ak47", RoleDamageKind.Firearm)]
    [InlineData("weapon_knife", RoleDamageKind.Knife)]
    [InlineData("weapon_hegrenade", RoleDamageKind.Grenade)]
    [InlineData("inferno", RoleDamageKind.DamageOverTime)]
    [InlineData("weapon_taser", RoleDamageKind.Other)]
    public void CounterStrike_weapon_names_map_to_role_damage_kinds(string name, RoleDamageKind expected)
    {
        Assert.Equal(expected, CounterStrikeRoleAdapterRules.ClassifyDamage(name));
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(5, 8.660254f, 60)]
    [InlineData(-10, 0, 180)]
    public void Front_angle_uses_victim_yaw_and_attacker_position(float attackerX, float attackerY, double expected)
    {
        var angle = CounterStrikeRoleAdapterRules.CalculateFrontAngle(
            Vector2.Zero,
            victimYawDegrees: 0,
            new(attackerX, attackerY));

        Assert.Equal(expected, angle, 2);
    }

    [Fact]
    public void Invalid_or_throwing_entity_is_skipped_without_stopping_global_update()
    {
        var updated = new List<int>();

        var count = CounterStrikeRoleAdapterRules.ApplyValidEntities(
            new[] { 1, 2, 3, 4 },
            item => item != 2,
            item =>
            {
                if (item == 3) throw new InvalidOperationException("entity expired");
                updated.Add(item);
            });

        Assert.Equal(2, count);
        Assert.Equal([1, 4], updated);
    }
}
