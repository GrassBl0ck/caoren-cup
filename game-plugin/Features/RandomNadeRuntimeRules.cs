using System.Numerics;

namespace CaorenCup.Features;

public enum RandomNadeDetonationKind
{
    Native,
    AfterDelay,
    WhenStopped
}

public readonly record struct RandomNadeRuntimePlan(
    string ProjectileDesignerName,
    string? ActionWeaponDesignerName,
    RandomNadeDetonationKind DetonationKind);

public static class RandomNadeRuntimeRules
{
    public const float ProjectileSpeed = 675f;
    public const float UpwardAngleDegrees = 12f;
    public const float ForcedDetonationDelaySeconds = 1.5f;
    public const float StoppedSpeedThreshold = 5f;
    public const float MinimumStoppedSampleAgeSeconds = 0.1f;
    public const float ForcedDetonationFailsafeSeconds = 5f;

    public static RandomNadeRuntimePlan GetPlan(RandomNadeType type) => type switch
    {
        RandomNadeType.Smoke => new(
            "smokegrenade_projectile",
            "weapon_smokegrenade",
            RandomNadeDetonationKind.WhenStopped),
        RandomNadeType.Fire => new(
            "molotov_projectile",
            "weapon_molotov",
            RandomNadeDetonationKind.AfterDelay),
        RandomNadeType.HighExplosive => new(
            "hegrenade_projectile",
            "weapon_hegrenade",
            RandomNadeDetonationKind.AfterDelay),
        RandomNadeType.Flash => new(
            "flashbang_projectile",
            null,
            RandomNadeDetonationKind.Native),
        RandomNadeType.Decoy => new(
            "decoy_projectile",
            "weapon_decoy",
            RandomNadeDetonationKind.WhenStopped),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static Vector3 ComputeLaunchVelocity(
        double pitchDegrees,
        double yawDegrees,
        Vector3 playerVelocity)
    {
        var pitch = DegreesToRadians(pitchDegrees - UpwardAngleDegrees);
        var yaw = DegreesToRadians(yawDegrees);
        var cosPitch = Math.Cos(pitch);

        var forward = new Vector3(
            (float)(cosPitch * Math.Cos(yaw)),
            (float)(cosPitch * Math.Sin(yaw)),
            (float)-Math.Sin(pitch));

        return forward * ProjectileSpeed + playerVelocity;
    }

    public static bool IsReadyToForceDetonate(
        RandomNadeRuntimePlan plan,
        double ageSeconds,
        double speed)
    {
        if (plan.DetonationKind == RandomNadeDetonationKind.Native) return false;
        if (ageSeconds >= ForcedDetonationFailsafeSeconds) return true;

        return plan.DetonationKind switch
        {
            RandomNadeDetonationKind.AfterDelay => ageSeconds >= ForcedDetonationDelaySeconds,
            RandomNadeDetonationKind.WhenStopped =>
                ageSeconds >= MinimumStoppedSampleAgeSeconds && speed < StoppedSpeedThreshold,
            _ => false
        };
    }

    public static bool IsFirearm(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName)) return false;
        var weapon = weaponName.ToLowerInvariant();
        return !weapon.Contains("knife", StringComparison.Ordinal) &&
               !weapon.Contains("bayonet", StringComparison.Ordinal) &&
               !weapon.Contains("grenade", StringComparison.Ordinal) &&
               !weapon.Contains("flashbang", StringComparison.Ordinal) &&
               !weapon.Contains("molotov", StringComparison.Ordinal) &&
               !weapon.Contains("decoy", StringComparison.Ordinal) &&
               !weapon.Contains("c4", StringComparison.Ordinal) &&
               !weapon.Contains("taser", StringComparison.Ordinal);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
