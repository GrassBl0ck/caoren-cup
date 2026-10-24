using System.Globalization;

namespace Caoren.AbilityMode;

public enum WeaponUseKind
{
    MagazineFirearm,
    Knife,
    Taser,
    Grenade,
    Other,
}

public sealed class BerserkerRoleHandler
{
    public const string LayerStateKey = "berserker.layers";
    public const string UltimateRateStateKey = "berserker.ultimate.fire-rate";

    public bool OnConfirmedEnemyKill(
        AbilityRolePlayer killer,
        AbilityRolePlayer victim,
        bool finalDeathConfirmed)
    {
        if (!finalDeathConfirmed || !killer.IsAlive || killer.Seat.AbilityId != "berserker"
            || killer.Seat.RosterTeam == victim.Seat.RosterTeam)
            return false;

        killer.BaseHealth = Math.Max(1, killer.RoleMaxHealth);
        SetLayers(killer.Seat, Math.Min(5, GetLayers(killer.Seat) + 1));
        return true;
    }

    public int GetLayers(AbilitySeatState seat) =>
        seat.RoundTemporaryState.TryGetValue(LayerStateKey, out var raw)
        && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var layers)
            ? Math.Clamp(layers, 0, 5)
            : 0;

    public double GetIncomingDamageMultiplier(AbilitySeatState seat, bool isTrueDamage) =>
        isTrueDamage ? 1 : 1 - GetLayers(seat) * 0.05;

    public AbilityEffectResult ActivateUltimate(AbilitySeatState seat, int currentCharge)
    {
        if (currentCharge < 8)
            return AbilityEffectResult.Failed("INSUFFICIENT_CHARGE", "狂战士主动至少需要 8 点充能。");
        var rate = currentCharge >= 12 ? 1.30 : currentCharge >= 10 ? 1.25 : 1.20;
        seat.RoundTemporaryState[UltimateRateStateKey] = rate.ToString("R", CultureInfo.InvariantCulture);
        return AbilityEffectResult.Succeeded();
    }

    public bool IsUltimateActive(AbilitySeatState seat) =>
        seat.RoundTemporaryState.ContainsKey(UltimateRateStateKey);

    public double GetFireRateMultiplier(AbilitySeatState seat) =>
        seat.RoundTemporaryState.TryGetValue(UltimateRateStateKey, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : 1;

    public double GetPerShotRecoilMultiplier(AbilitySeatState seat) => 1 / GetFireRateMultiplier(seat);

    public bool ShouldPreserveAmmo(AbilitySeatState seat, WeaponUseKind weaponKind) =>
        IsUltimateActive(seat) && weaponKind == WeaponUseKind.MagazineFirearm;

    public void OnRoundEnded(AbilitySeatState seat)
    {
        seat.RoundTemporaryState.Remove(LayerStateKey);
        seat.RoundTemporaryState.Remove(UltimateRateStateKey);
    }

    private static void SetLayers(AbilitySeatState seat, int layers) =>
        seat.RoundTemporaryState[LayerStateKey] = layers.ToString(CultureInfo.InvariantCulture);
}
