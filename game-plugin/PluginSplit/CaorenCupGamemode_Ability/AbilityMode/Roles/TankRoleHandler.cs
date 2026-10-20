namespace Caoren.AbilityMode;

public enum RoleDamageKind
{
    Firearm,
    Knife,
    Grenade,
    DamageOverTime,
    NonLifeKnockback,
    Other,
}

public sealed class TankRoleHandler
{
    public const string MovementSource = "role.tank.penalty";
    public const string UltimateStateKey = "坦克主动";
    public const string CurrentHealthStateKey = "tank.current-health";
    public const string OriginalMaxHealthStateKey = "tank.original-max-health";

    public void ApplySpawn(AbilityRolePlayer player, int originalMaxHealth)
    {
        if (player.Seat.AbilityId != "tank") return;
        player.RoleMaxHealth = Math.Max(1, originalMaxHealth) + 100;
        player.BaseHealth = player.RoleMaxHealth;
        PersistManagedHealth(player, originalMaxHealth);
    }

    public void PersistManagedHealth(AbilityRolePlayer player, int originalMaxHealth)
    {
        player.HasManagedHealthSnapshot = true;
        player.Seat.RoundTemporaryState[CurrentHealthStateKey] =
            Math.Max(0, player.BaseHealth).ToString(System.Globalization.CultureInfo.InvariantCulture);
        player.Seat.RoundTemporaryState[OriginalMaxHealthStateKey] =
            Math.Max(1, originalMaxHealth).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool RestoreManagedHealth(AbilityRolePlayer player)
    {
        if (!player.Seat.RoundTemporaryState.TryGetValue(CurrentHealthStateKey, out var healthRaw)
            || !player.Seat.RoundTemporaryState.TryGetValue(OriginalMaxHealthStateKey, out var maxRaw)
            || !int.TryParse(healthRaw, out var health)
            || !int.TryParse(maxRaw, out var originalMax))
            return false;
        player.RoleMaxHealth = Math.Max(1, originalMax) + 100;
        player.BaseHealth = Math.Clamp(health, 0, player.RoleMaxHealth);
        player.HasManagedHealthSnapshot = true;
        return true;
    }

    public void ApplyPassiveMovement(AbilitySeatState seat, MovementModifierService movement)
    {
        if (seat.AbilityId == "tank") movement.Set(seat.SeatId, MovementSource, 0.9);
    }

    public AbilityEffectResult ActivateUltimate(
        AbilitySeatState seat,
        MovementModifierService movement,
        DateTimeOffset now)
    {
        seat.RecoverableUntil[UltimateStateKey] = now.AddSeconds(5);
        movement.Remove(seat.SeatId, MovementSource);
        return AbilityEffectResult.Succeeded();
    }

    public bool IsUltimateActive(AbilitySeatState seat, DateTimeOffset now) =>
        seat.RecoverableUntil.TryGetValue(UltimateStateKey, out var until) && until > now;

    public double GetIncomingDamageMultiplier(
        AbilitySeatState seat,
        bool isTrueDamage,
        RoleDamageKind damageKind,
        double signedFrontAngleDegrees,
        DateTimeOffset now)
    {
        if (seat.AbilityId != "tank" || isTrueDamage) return 1;
        var mitigation = 0.20;
        var isFrontFirearm = damageKind == RoleDamageKind.Firearm
            && Math.Abs(signedFrontAngleDegrees) <= 60;
        if (isFrontFirearm)
        {
            mitigation += 0.20;
            if (IsUltimateActive(seat, now)) mitigation += 0.40;
        }
        return Math.Max(0, 1 - mitigation);
    }

    public double GetOutgoingLifeDamageMultiplier(
        AbilitySeatState seat,
        RoleDamageKind damageKind,
        DateTimeOffset now)
    {
        if (!IsUltimateActive(seat, now)) return 1;
        return damageKind is RoleDamageKind.Firearm or RoleDamageKind.Knife or RoleDamageKind.Grenade
            ? 0.25
            : 1;
    }

    public double GetOwnJumpMultiplier(AbilitySeatState seat, DateTimeOffset now) =>
        IsUltimateActive(seat, now) ? 1 : 0.9;

    public void EndUltimate(AbilitySeatState seat, MovementModifierService movement, bool restorePenalty)
    {
        seat.RecoverableUntil.Remove(UltimateStateKey);
        if (restorePenalty) ApplyPassiveMovement(seat, movement);
        else movement.Remove(seat.SeatId, MovementSource);
    }

    public void OnCleanup(AbilitySeatState seat, MovementModifierService movement)
    {
        seat.RecoverableUntil.Remove(UltimateStateKey);
        movement.Remove(seat.SeatId, MovementSource);
        seat.RoundTemporaryState.Remove(CurrentHealthStateKey);
        seat.RoundTemporaryState.Remove(OriginalMaxHealthStateKey);
    }
}
