using System.Globalization;

namespace Caoren.AbilityMode;

public enum EconomySource
{
    KillReward,
    RoundReward,
    BombPlantReward,
    BombDefuseReward,
    ObjectiveReward,
    Refund,
    AdminGrant,
    ChargeRefund,
    RoleTransfer,
    CapitalistUltimate,
}

public sealed record EconomyBalanceChange(string SeatId, int OriginalBalance, int FinalBalance);

public sealed record CapitalistTransferPlan(
    bool Ok,
    string CasterSeatId,
    int TotalPlundered,
    int SharePerFriendlySeat,
    IReadOnlyList<EconomyBalanceChange> Changes,
    string Code = "OK");

public sealed class SeatEconomyLedger
{
    public const string BalanceStateKey = "economy.balance";
    public const string MaximumStateKey = "economy.maximum";

    public void Synchronize(AbilityRolePlayer player, int maximumMoney)
    {
        Write(player.Seat, player.Money, maximumMoney);
    }

    public bool TryRestore(AbilityRolePlayer player)
    {
        if (!player.Seat.CrossRoundState.TryGetValue(BalanceStateKey, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var balance))
            return false;
        player.Money = Math.Max(0, balance);
        return true;
    }

    public void Write(AbilitySeatState seat, int balance, int maximumMoney)
    {
        seat.CrossRoundState[BalanceStateKey] = Math.Clamp(balance, 0, maximumMoney).ToString(CultureInfo.InvariantCulture);
        seat.CrossRoundState[MaximumStateKey] = maximumMoney.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class CapitalistRoleHandler
{
    private const string OperationPrefix = "capitalist.operation.";
    private readonly SeatEconomyLedger _ledger = new();

    public static double GetPlunderRatio(int charge) =>
        0.20 + (Math.Clamp(charge, 8, 14) - 8) * (0.20 / 6);

    public int ApplyReward(
        AbilityRolePlayer player,
        int amount,
        EconomySource source,
        int maximumMoney)
    {
        if (amount <= 0) return 0;
        var multiplier = player.Seat.AbilityId == "capitalist" && IsNormalReward(source) ? 2 : 1;
        var credited = Math.Min(amount * multiplier, Math.Max(0, maximumMoney - player.Money));
        player.Money += credited;
        _ledger.Write(player.Seat, player.Money, maximumMoney);
        return credited;
    }

    public int ApplyObservedRewardBonus(
        AbilityRolePlayer player,
        int previousConfirmedBalance,
        EconomySource source,
        int maximumMoney)
    {
        if (player.Seat.AbilityId != "capitalist" || !IsNormalReward(source)) return 0;
        var observedReward = Math.Max(0, player.Money - Math.Max(0, previousConfirmedBalance));
        var bonus = Math.Min(observedReward, Math.Max(0, maximumMoney - player.Money));
        player.Money += bonus;
        _ledger.Write(player.Seat, player.Money, maximumMoney);
        return bonus;
    }

    public CapitalistTransferPlan BuildPlunderPlan(
        AbilitySeatState caster,
        IReadOnlyCollection<AbilityRolePlayer> players,
        int charge,
        int maximumMoney)
    {
        if (charge < 8)
            return new(false, caster.SeatId, 0, 0, [], "INSUFFICIENT_CHARGE");
        var ratio = GetPlunderRatio(charge);
        var enemies = players.Where(player => player.Seat.RosterTeam != caster.RosterTeam).ToArray();
        var allies = players.Where(player => player.Seat.RosterTeam == caster.RosterTeam).ToArray();
        if (allies.Length == 0)
            return new(false, caster.SeatId, 0, 0, [], "NO_FRIENDLY_SEATS");

        var deductions = enemies.ToDictionary(
            player => player.Seat.SeatId,
            player => (int)Math.Floor(Math.Max(0, player.Money) * ratio),
            StringComparer.Ordinal);
        var total = deductions.Values.Sum();
        if (total <= 0)
            return new(false, caster.SeatId, 0, 0, [], "NO_PLUNDERABLE_MONEY");
        var share = total / allies.Length;
        var changes = new List<EconomyBalanceChange>(players.Count);
        foreach (var player in players)
        {
            var final = player.Money;
            if (deductions.TryGetValue(player.Seat.SeatId, out var deduction)) final -= deduction;
            if (player.Seat.RosterTeam == caster.RosterTeam)
                final = Math.Min(maximumMoney, final + share);
            changes.Add(new(player.Seat.SeatId, player.Money, Math.Clamp(final, 0, maximumMoney)));
        }
        return new(true, caster.SeatId, total, share, changes);
    }

    public bool TryApplyPlan(
        CapitalistTransferPlan plan,
        IReadOnlyCollection<AbilityRolePlayer> players,
        string operationId,
        int maximumMoney)
    {
        if (!plan.Ok || string.IsNullOrWhiteSpace(operationId)) return false;
        var bySeat = players.ToDictionary(player => player.Seat.SeatId, StringComparer.Ordinal);
        if (!bySeat.TryGetValue(plan.CasterSeatId, out var caster)) return false;
        var operationKey = OperationPrefix + operationId;
        if (caster.Seat.CrossRoundState.ContainsKey(operationKey)) return false;
        foreach (var change in plan.Changes)
        {
            if (!bySeat.TryGetValue(change.SeatId, out var player)
                || player.Money != change.OriginalBalance
                || change.FinalBalance < 0
                || change.FinalBalance > maximumMoney)
                return false;
        }

        foreach (var change in plan.Changes)
        {
            var player = bySeat[change.SeatId];
            player.Money = change.FinalBalance;
            _ledger.Write(player.Seat, player.Money, maximumMoney);
        }
        caster.Seat.CrossRoundState[operationKey] = "1";
        return true;
    }

    private static bool IsNormalReward(EconomySource source) => source is
        EconomySource.KillReward or EconomySource.RoundReward or EconomySource.BombPlantReward
        or EconomySource.BombDefuseReward or EconomySource.ObjectiveReward;
}
