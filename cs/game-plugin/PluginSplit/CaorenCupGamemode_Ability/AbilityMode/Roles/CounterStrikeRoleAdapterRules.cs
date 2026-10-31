using System.Numerics;

namespace Caoren.AbilityMode;

public static class CounterStrikeRoleAdapterRules
{
    public static RoleDamageKind ClassifyDamage(string designerName)
    {
        var name = designerName.ToLowerInvariant();
        if (name.Contains("inferno") || name.Contains("burn")) return RoleDamageKind.DamageOverTime;
        if (name.Contains("knife") || name.Contains("bayonet")) return RoleDamageKind.Knife;
        if (name.Contains("grenade") || name.Contains("molotov") || name.Contains("incendiary")
            || name.Contains("flashbang") || name.Contains("smoke") || name.Contains("decoy"))
            return RoleDamageKind.Grenade;
        if (name.Contains("weapon_") && !name.Contains("taser")) return RoleDamageKind.Firearm;
        return RoleDamageKind.Other;
    }

    public static double CalculateFrontAngle(Vector2 victimPosition, double victimYawDegrees, Vector2 attackerPosition)
    {
        var offset = attackerPosition - victimPosition;
        var length = offset.Length();
        if (length <= 0.001f) return 0;
        var yaw = victimYawDegrees * Math.PI / 180d;
        var dot = (Math.Cos(yaw) * offset.X + Math.Sin(yaw) * offset.Y) / length;
        return Math.Acos(Math.Clamp(dot, -1, 1)) * 180d / Math.PI;
    }

    public static int ApplyValidEntities<T>(
        IEnumerable<T> entities,
        Func<T, bool> isValid,
        Action<T> apply)
    {
        var applied = 0;
        foreach (var entity in entities)
        {
            if (!isValid(entity)) continue;
            try
            {
                apply(entity);
                applied++;
            }
            catch
            {
                // 单个实体在更新期间失效时继续处理其余玩家。
            }
        }
        return applied;
    }
}
