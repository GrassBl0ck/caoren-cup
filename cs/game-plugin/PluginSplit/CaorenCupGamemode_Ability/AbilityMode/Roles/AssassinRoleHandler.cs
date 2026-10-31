namespace Caoren.AbilityMode;

public sealed class AssassinRoleHandler
{
    public const string ActiveStateKey = "assassin.active";
    public bool IsHidden(AbilitySeatState seat, double speed, bool active) => seat.AbilityId == "assassin" && (active || speed <= 0.01);
    public double GetDamageMultiplier(AbilitySeatState seat, double signedFrontAngleDegrees) =>
        seat.AbilityId == "assassin" && Math.Abs(signedFrontAngleDegrees) >= 135 ? 1.5 : 1;
    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int charge)
    {
        if (charge < 8) return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "刺客主动至少需要 8 点充能。");
        if (IsActive(seat, DateTimeOffset.UtcNow)) return AbilityEffectResult.Failed("ALREADY_ACTIVE", "刺客主动本回合已使用。");
        seat.RoundTemporaryState[ActiveStateKey] = "1";
        seat.RecoverableUntil[ActiveStateKey] = DateTimeOffset.UtcNow.AddSeconds(3);
        return AbilityEffectResult.Succeeded();
    }
    public bool IsActive(AbilitySeatState seat, DateTimeOffset now) => seat.RoundTemporaryState.ContainsKey(ActiveStateKey)
        && (!seat.RecoverableUntil.TryGetValue(ActiveStateKey, out var until) || until > now);
    public void Clear(AbilitySeatState seat) { seat.RoundTemporaryState.Remove(ActiveStateKey); seat.RecoverableUntil.Remove(ActiveStateKey); }
}
