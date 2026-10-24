namespace Caoren.AbilityMode;

public sealed record MedicShotResult(
    bool ReplacesFriendlyDamage,
    bool Triggered,
    int Healing,
    double SpeedMultiplier,
    TimeSpan SpeedDuration);

public sealed class MedicRoleHandler
{
    public const string RecentDamageStateKey = "medic.recent-actual-damage";
    public const string CooldownStateKey = "medic.target-cooldown";
    public const string SpeedStateKey = "医师加速";
    public const string SpeedMultiplierStateKey = "medic.speed.multiplier";
    public const string MovementSource = "role.medic.speed";

    public bool RecordActualHealthLoss(AbilitySeatState target, DateTimeOffset now, int actualHealthLoss)
    {
        if (actualHealthLoss <= 0) return false;
        SetInternalUntil(target, RecentDamageStateKey, now.AddSeconds(1));
        return true;
    }

    public MedicShotResult ResolveFriendlyShot(
        AbilitySeatState attacker,
        AbilitySeatState target,
        DateTimeOffset now,
        bool isFirearm,
        int baseHealth,
        int roleMaxHealth)
    {
        if (!isFirearm || attacker.AbilityId != "medic" || attacker.SeatId == target.SeatId
            || attacker.RosterTeam != target.RosterTeam)
            return new(false, false, 0, 1, TimeSpan.Zero);

        if (IsInternalActive(target, CooldownStateKey, now))
            return new(true, false, 0, 1, TimeSpan.Zero);

        var recentlyDamaged = IsInternalActive(target, RecentDamageStateKey, now);
        var rawHealing = recentlyDamaged ? 5 : 10;
        var healing = Math.Clamp(roleMaxHealth - Math.Max(0, baseHealth), 0, rawHealing);
        SetInternalUntil(target, CooldownStateKey, now.AddSeconds(0.5));
        return new(true, true, healing, recentlyDamaged ? 1.05 : 1.10, TimeSpan.FromSeconds(2));
    }

    public void ApplySpeed(
        MovementModifierService movement,
        AbilitySeatState target,
        MedicShotResult result,
        DateTimeOffset now)
    {
        if (!result.Triggered) return;
        movement.Set(target.SeatId, MovementSource, result.SpeedMultiplier);
        target.RoundTemporaryState[SpeedMultiplierStateKey] =
            result.SpeedMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        target.RecoverableUntil[SpeedStateKey] = now + result.SpeedDuration;
    }

    public bool RestoreSpeed(MovementModifierService movement, AbilitySeatState target, DateTimeOffset now)
    {
        if (!target.RecoverableUntil.TryGetValue(SpeedStateKey, out var until) || until <= now
            || !target.RoundTemporaryState.TryGetValue(SpeedMultiplierStateKey, out var raw)
            || !double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var multiplier))
            return false;
        movement.Set(target.SeatId, MovementSource, multiplier);
        return true;
    }

    public AbilityEffectResult ApplyUltimate(AbilitySeatState caster, IEnumerable<AbilityRolePlayer> players)
    {
        var changed = false;
        foreach (var player in players)
        {
            if (!player.IsAlive || player.Seat.RosterTeam != caster.RosterTeam) continue;
            var clamped = Math.Max(1, player.RoleMaxHealth);
            if (player.BaseHealth != clamped) changed = true;
            player.BaseHealth = clamped;
        }
        return AbilityEffectResult.Succeeded(emptyCast: !changed);
    }

    private static void SetInternalUntil(AbilitySeatState seat, string key, DateTimeOffset until) =>
        seat.RoundTemporaryState[key] = until.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsInternalActive(AbilitySeatState seat, string key, DateTimeOffset now) =>
        seat.RoundTemporaryState.TryGetValue(key, out var raw)
        && DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var until)
        && until > now;
}
