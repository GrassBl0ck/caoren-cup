using System.Globalization;

namespace Caoren.AbilityMode;

public sealed record BalanceRoundContext(int RoundNumber, int RegulationRoundsPerHalf, bool IsOvertime);
public sealed record BalanceUltimateResult(bool Ok, bool EmptyCast, int GeneratedDamageEvents);
public sealed record TemporaryHealthDamageResult(int AbsorbedTemporaryHealth, int RemainingLifeDamage);

public sealed class BalanceRoleHandler
{
    public const string BuyRoundStateKey = "balance.buy-round";
    public const string TemporaryHealthStateKey = "balance.temporary-health";
    public const string NextDecayStateKey = "天平临时生命衰减";
    private readonly SeatEconomyLedger _ledger = new();

    public static bool ShouldSetBuyMoney(BalanceRoundContext context)
    {
        if (context.IsOvertime || context.RoundNumber <= 0 || context.RegulationRoundsPerHalf <= 0) return false;
        return context.RoundNumber != 1 && context.RoundNumber != context.RegulationRoundsPerHalf + 1;
    }

    public bool TryApplyBuyPhase(AbilityRolePlayer player, BalanceRoundContext context, int maximumMoney = 16_000)
    {
        if (player.Seat.AbilityId != "balance" || !ShouldSetBuyMoney(context)) return false;
        var roundKey = context.RoundNumber.ToString(CultureInfo.InvariantCulture);
        if (player.Seat.CrossRoundState.TryGetValue(BuyRoundStateKey, out var applied) && applied == roundKey)
            return false;
        player.Money = Math.Min(3_850, Math.Max(0, maximumMoney));
        player.Seat.CrossRoundState[BuyRoundStateKey] = roundKey;
        _ledger.Write(player.Seat, player.Money, Math.Max(0, maximumMoney));
        return true;
    }

    public BalanceUltimateResult ApplyUltimate(
        AbilityRolePlayer caster,
        IEnumerable<AbilityRolePlayer> players,
        DateTimeOffset now)
    {
        var affected = 0;
        foreach (var target in players)
        {
            if (!target.IsAlive || target.Seat.RosterTeam == caster.Seat.RosterTeam) continue;
            target.BaseHealth = Math.Clamp(caster.BaseHealth, 1, Math.Max(1, target.RoleMaxHealth));
            GrantTemporaryHealth(target, 40, now);
            affected++;
        }
        return new(true, affected == 0, 0);
    }

    public void GrantTemporaryHealth(AbilityRolePlayer player, int amount, DateTimeOffset now)
    {
        player.TemporaryHealth = Math.Max(0, amount);
        PersistTemporaryHealth(player);
        if (player.TemporaryHealth > 0)
            player.Seat.RoundTemporaryState[NextDecayStateKey] =
                now.AddSeconds(1).ToString("O", CultureInfo.InvariantCulture);
    }

    public bool RestoreTemporaryHealth(AbilityRolePlayer player)
    {
        if (!player.Seat.RoundTemporaryState.TryGetValue(TemporaryHealthStateKey, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;
        player.TemporaryHealth = Math.Max(0, value);
        return player.TemporaryHealth > 0;
    }

    public TemporaryHealthDamageResult AbsorbDamage(AbilityRolePlayer player, int incomingLifeDamage, bool isTrueDamage)
    {
        var incoming = Math.Max(0, incomingLifeDamage);
        var absorbed = Math.Min(player.TemporaryHealth, incoming);
        player.TemporaryHealth -= absorbed;
        PersistTemporaryHealth(player);
        if (player.TemporaryHealth == 0) player.Seat.RoundTemporaryState.Remove(NextDecayStateKey);
        return new(absorbed, incoming - absorbed);
    }

    public void TickDecay(AbilityRolePlayer player, DateTimeOffset now)
    {
        if (player.TemporaryHealth <= 0
            || !TryGetNextDecay(player.Seat, out var next)
            || now < next)
            return;
        var ticks = (int)Math.Floor((now - next).TotalSeconds) + 1;
        player.TemporaryHealth = Math.Max(0, player.TemporaryHealth - ticks);
        PersistTemporaryHealth(player);
        if (player.TemporaryHealth == 0)
            player.Seat.RoundTemporaryState.Remove(NextDecayStateKey);
        else
            player.Seat.RoundTemporaryState[NextDecayStateKey] =
                next.AddSeconds(ticks).ToString("O", CultureInfo.InvariantCulture);
    }

    public void ClearTemporaryHealth(AbilityRolePlayer player)
    {
        player.TemporaryHealth = 0;
        player.Seat.RoundTemporaryState.Remove(TemporaryHealthStateKey);
        player.Seat.RoundTemporaryState.Remove(NextDecayStateKey);
    }

    private static void PersistTemporaryHealth(AbilityRolePlayer player) =>
        player.Seat.RoundTemporaryState[TemporaryHealthStateKey] =
            player.TemporaryHealth.ToString(CultureInfo.InvariantCulture);

    private static bool TryGetNextDecay(AbilitySeatState seat, out DateTimeOffset next)
    {
        next = default;
        return seat.RoundTemporaryState.TryGetValue(NextDecayStateKey, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out next);
    }
}
