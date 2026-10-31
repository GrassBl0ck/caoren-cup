namespace Caoren.AbilityMode;

public sealed class SkyCourierRoleHandler
{
    private const string ActiveKey = "sky_courier.active";
    public bool CanAirJump(AbilitySeatState seat) => seat.AbilityId == "sky_courier";
    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int charge)
    {
        if (charge < 8) return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "超级飞侠主动至少需要 8 点充能。");
        seat.RoundTemporaryState[ActiveKey] = "1";
        return AbilityEffectResult.Succeeded();
    }
    public AbilityPrototypeResult TryTeleport(AbilitySeatState seat, bool isValidEntity, bool isSafePoint) =>
        !isValidEntity || !isSafePoint ? new(false, "SAFE_TELEPORT_REJECTED", "传送实体或安全落点无效。") : new(true);
    public void Clear(AbilitySeatState seat) => seat.RoundTemporaryState.Remove(ActiveKey);
}
