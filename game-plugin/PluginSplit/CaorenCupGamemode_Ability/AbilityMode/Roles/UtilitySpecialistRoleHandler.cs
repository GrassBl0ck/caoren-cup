namespace Caoren.AbilityMode;

public sealed class UtilitySpecialistRoleHandler
{
    private const string ActiveKey = "utility_specialist.active";
    private readonly HashSet<string> _refunded = new(StringComparer.Ordinal);
    public int GetGrenadeCapacity(AbilitySeatState seat, int baseCapacity) => seat.AbilityId == "utility_specialist" ? Math.Max(0, baseCapacity) + 2 : Math.Max(0, baseCapacity);
    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int charge)
    {
        if (charge < 8) return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "道具手主动至少需要 8 点充能。");
        if (!seat.RoundTemporaryState.TryAdd(ActiveKey, "1")) return AbilityEffectResult.Failed("ALREADY_ACTIVE", "道具手主动本回合已使用。");
        return AbilityEffectResult.Succeeded();
    }
    public bool TryRefund(AbilitySeatState seat, string throwId) => seat.RoundTemporaryState.ContainsKey(ActiveKey) && _refunded.Add($"{seat.SeatId}:{throwId}");
    public void Clear(AbilitySeatState seat) { seat.RoundTemporaryState.Remove(ActiveKey); _refunded.RemoveWhere(x => x.StartsWith(seat.SeatId + ":", StringComparison.Ordinal)); }
}
