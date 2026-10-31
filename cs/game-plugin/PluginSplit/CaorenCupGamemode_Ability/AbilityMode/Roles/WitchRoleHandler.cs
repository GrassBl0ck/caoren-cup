using System.Globalization;
using System.Numerics;

namespace Caoren.AbilityMode;

public sealed class WitchRoleHandler
{
    private const string DamageKey = "witch.poison.damage";
    private const string LifeKey = "witch.poison.life";
    private const string ActiveKey = "witch.ultimate";
    private const string UltimateLifeKey = "witch.ultimate.life";
    public sealed record WitchUltimateResult(int GeneratedDamageEvents, int TotalTrueDamage);
    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int charge)
    {
        if (charge < 10) return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "女巫主动至少需要 10 点充能。");
        if (!seat.RoundTemporaryState.TryAdd(ActiveKey, "1")) return AbilityEffectResult.Failed("ALREADY_ACTIVE", "女巫主动本回合已使用。");
        return AbilityEffectResult.Succeeded();
    }
    public bool ApplyPoison(AbilitySeatState target, int damage, string lifeKey)
    {
        if (target.RoundTemporaryState.TryGetValue(LifeKey, out var existing) && existing == lifeKey) return false;
        target.RoundTemporaryState[LifeKey] = lifeKey;
        target.RoundTemporaryState[DamageKey] = Math.Max(0, damage).ToString(CultureInfo.InvariantCulture);
        return true;
    }
    public int GetPoisonDamage(AbilitySeatState target) => target.RoundTemporaryState.TryGetValue(DamageKey, out var raw)
        && int.TryParse(raw, out var value) ? value : 0;

    public WitchUltimateResult ApplyUltimate(
        AbilitySeatState caster,
        IEnumerable<AbilitySeatState> targets,
        Vector3 casterPosition,
        float radius)
    {
        if (!caster.RoundTemporaryState.ContainsKey(ActiveKey)) return new(0, 0);
        var generated = 0;
        var total = 0;
        foreach (var target in targets)
        {
            if (target.RosterTeam == caster.RosterTeam || !target.RoundTemporaryState.ContainsKey(LifeKey)) continue;
            if (Vector3.Distance(casterPosition, ParsePosition(target)) > Math.Max(0, radius)) continue;
            var lifeKey = target.RoundTemporaryState[LifeKey];
            var operationKey = $"{caster.SeatId}:{lifeKey}";
            if (!caster.RoundTemporaryState.TryAdd(UltimateLifeKey + ":" + operationKey, "1")) continue;
            var damage = Math.Max(1, GetPoisonDamage(target));
            target.RoundTemporaryState[DamageKey] = checked(damage * 2).ToString(CultureInfo.InvariantCulture);
            generated++;
            total += damage;
        }
        return new(generated, total);
    }
    public void Clear(AbilitySeatState target)
    {
        target.RoundTemporaryState.Remove(DamageKey);
        target.RoundTemporaryState.Remove(LifeKey);
        target.RoundTemporaryState.Remove(ActiveKey);
        foreach (var key in target.RoundTemporaryState.Keys.Where(key => key.StartsWith(UltimateLifeKey + ":", StringComparison.Ordinal)).ToArray())
            target.RoundTemporaryState.Remove(key);
    }

    private static Vector3 ParsePosition(AbilitySeatState seat)
    {
        if (!seat.RoundTemporaryState.TryGetValue("witch.position", out var raw)) return Vector3.Zero;
        var parts = raw.Split(',');
        return parts.Length == 3 && float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y) && float.TryParse(parts[2], out var z)
            ? new(x, y, z)
            : Vector3.Zero;
    }
}
