namespace Caoren.AbilityMode;

public sealed class GlassCannonRoleHandler
{
    private const string ArmedKey = "glass_cannon.armed";
    private const string TriggeredKey = "glass_cannon.triggered";
    public sealed record ExplosionPlan(int Damage, int Radius);
    public int GetRoleMaxHealth(AbilitySeatState seat, int original) => seat.AbilityId == "glass_cannon" ? Math.Min(original, 50) : original;
    public double GetFirearmMultiplier(AbilitySeatState seat) => seat.AbilityId == "glass_cannon" ? 1.25 : 1;
    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int charge)
    {
        if (charge < 10) return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "玻璃大炮主动至少需要 10 点充能。");
        if (seat.RoundTemporaryState.ContainsKey(ArmedKey)) return AbilityEffectResult.Failed("ALREADY_ACTIVE", "玻璃大炮主动本回合已使用。");
        seat.RoundTemporaryState[ArmedKey] = "1";
        return AbilityEffectResult.Succeeded();
    }
    public bool TryTriggerExplosion(AbilitySeatState seat)
    {
        if (!seat.RoundTemporaryState.ContainsKey(ArmedKey) || seat.RoundTemporaryState.ContainsKey(TriggeredKey)) return false;
        seat.RoundTemporaryState[TriggeredKey] = "1";
        return true;
    }
    public ExplosionPlan? TryBuildExplosion(AbilitySeatState seat, int damage, int radius)
    {
        if (!TryTriggerExplosion(seat)) return null;
        return new(Math.Max(0, damage), Math.Max(0, radius));
    }
    public void Clear(AbilitySeatState seat) { seat.RoundTemporaryState.Remove(ArmedKey); seat.RoundTemporaryState.Remove(TriggeredKey); }
}
