namespace Caoren.AbilityMode;

public sealed record SnowGolemDamageResult(int LifeDamage, int Knockback);

public sealed class SnowGolemRoleHandler
{
    public const string UltimateStateKey = "snow_golem.damage";
    public SnowGolemDamageResult ResolveFirearmDamage(AbilitySeatState seat, int lifeDamage, int knockback) =>
        new(seat.AbilityId == "snow_golem" && IsUltimateActive(seat, DateTimeOffset.UtcNow) ? Math.Max(0, lifeDamage) : 0, Math.Max(0, knockback));
    public int ResolveLifeDamage(AbilitySeatState seat, int damage, bool isTrueDamage) =>
        seat.AbilityId == "snow_golem" && !isTrueDamage && !IsUltimateActive(seat, DateTimeOffset.UtcNow) ? 0 : Math.Max(0, damage);
    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int charge)
    {
        if (charge < 6) return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "雪傀儡主动至少需要 6 点充能。");
        if (IsUltimateActive(seat, DateTimeOffset.UtcNow)) return AbilityEffectResult.Failed("ALREADY_ACTIVE", "雪傀儡主动本回合已使用。");
        seat.RoundTemporaryState[UltimateStateKey] = "1";
        seat.RecoverableUntil[UltimateStateKey] = DateTimeOffset.UtcNow.AddSeconds(5);
        return AbilityEffectResult.Succeeded();
    }
    public bool IsUltimateActive(AbilitySeatState seat, DateTimeOffset now) => seat.RoundTemporaryState.ContainsKey(UltimateStateKey)
        && (!seat.RecoverableUntil.TryGetValue(UltimateStateKey, out var until) || until > now);
    public void Clear(AbilitySeatState seat) { seat.RoundTemporaryState.Remove(UltimateStateKey); seat.RecoverableUntil.Remove(UltimateStateKey); }
}
