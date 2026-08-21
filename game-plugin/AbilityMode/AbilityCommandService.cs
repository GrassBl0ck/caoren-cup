using System.Net;

namespace Caoren.AbilityMode;

public interface IMoneyAccount
{
    int Balance { get; }
    bool TryDebit(int amount);
    bool TryCredit(int amount);
}

public sealed record ChargePurchaseContext(
    bool IsValidMatch,
    bool IsSeatBound,
    bool IsAlive,
    bool IsBuyTime,
    bool IsInBuyZone);

public sealed record AbilityUseContext(
    bool IsValidMatch,
    bool IsFormalPhase,
    bool IsSeatBound,
    bool IsAlive,
    bool ProfessionConditionsValid);

public sealed record AbilityCommandResult(bool Ok, string Code, string Message, int ConsumedCharge = 0)
{
    public static AbilityCommandResult Success(int consumed = 0) => new(true, "OK", "成功。", consumed);
    public static AbilityCommandResult Fail(string code, string message) => new(false, code, message);
}

public sealed record AbilityEffectResult(bool Ok, string Code, string Message, bool EmptyCast = false)
{
    public static AbilityEffectResult Succeeded(bool emptyCast = false) => new(true, "OK", "技能效果已创建。", emptyCast);
    public static AbilityEffectResult Failed(string code, string message) => new(false, code, message);
}

public interface IAbilityEffectHandler
{
    AbilityEffectResult TryCreateEffect(AbilitySeatState seat, int currentCharge);
}

public sealed class AbilityCommandService
{
    public const int ChargePurchaseCost = 750;
    private readonly ChargeService _charges;
    private readonly IReadOnlyDictionary<string, IAbilityEffectHandler> _handlers;

    public AbilityCommandService(
        ChargeService charges,
        IReadOnlyDictionary<string, IAbilityEffectHandler>? handlers = null)
    {
        _charges = charges;
        _handlers = handlers ?? new Dictionary<string, IAbilityEffectHandler>(StringComparer.Ordinal);
    }

    public AbilityCommandResult BuyCharge(
        AbilitySeatState seat,
        ChargePurchaseContext context,
        IMoneyAccount money,
        Action persistImmediately)
    {
        if (!context.IsValidMatch) return AbilityCommandResult.Fail("INVALID_MATCH", "当前不是有效异能比赛。");
        if (!context.IsSeatBound) return AbilityCommandResult.Fail("SEAT_NOT_BOUND", "玩家未绑定合法比赛席位。");
        if (!context.IsAlive) return AbilityCommandResult.Fail("PLAYER_DEAD", "存活时才能购买充能。");
        if (!context.IsBuyTime) return AbilityCommandResult.Fail("OUTSIDE_BUY_TIME", "当前不在正常购买时间。");
        if (!context.IsInBuyZone) return AbilityCommandResult.Fail("OUTSIDE_BUY_ZONE", "玩家不在购买区。");
        if (seat.ChargePurchasedThisRound) return AbilityCommandResult.Fail("ALREADY_PURCHASED", "本回合已经购买过充能。");
        if (money.Balance < ChargePurchaseCost) return AbilityCommandResult.Fail("INSUFFICIENT_MONEY", "余额不足 750 美元。");
        if (seat.Charge >= seat.Definition.ChargeCap) return AbilityCommandResult.Fail("CHARGE_FULL", "充能已经达到上限。");
        if (!money.TryDebit(ChargePurchaseCost)) return AbilityCommandResult.Fail("DEBIT_FAILED", "扣款失败，未增加充能。");

        var previousCharge = seat.Charge;
        var previousPurchased = seat.ChargePurchasedThisRound;
        try
        {
            if (_charges.Add(seat, 1, ChargeReason.Purchase) != 1)
                throw new InvalidOperationException("购买后未能增加 1 点充能。");
            seat.MarkChargePurchased();
            persistImmediately();
            return AbilityCommandResult.Success();
        }
        catch
        {
            _charges.Restore(seat, previousCharge);
            seat.RestorePerRoundFlags(seat.AbilityUsedThisRound, previousPurchased);
            return money.TryCredit(ChargePurchaseCost)
                ? AbilityCommandResult.Fail("PURCHASE_ROLLED_BACK", "购买失败，扣款和充能已经回滚。")
                : AbilityCommandResult.Fail("PURCHASE_CONSISTENCY_ERROR", "购买失败且退款未确认，需要管理员检查经济状态。");
        }
    }

    public AbilityCommandResult UseUltimate(
        AbilitySeatState seat,
        AbilityUseContext context,
        Action persistImmediately)
    {
        if (!context.IsValidMatch) return AbilityCommandResult.Fail("INVALID_MATCH", "当前不是有效异能比赛。");
        if (!context.IsFormalPhase) return AbilityCommandResult.Fail("INVALID_PHASE", "当前比赛阶段不能使用主动技能。");
        if (!context.IsSeatBound) return AbilityCommandResult.Fail("SEAT_NOT_BOUND", "玩家未绑定合法比赛席位。");
        if (!context.IsAlive) return AbilityCommandResult.Fail("PLAYER_DEAD", "存活时才能使用主动技能。");
        if (seat.AbilityUsedThisRound) return AbilityCommandResult.Fail("ALREADY_USED", "本回合已经成功使用过主动技能。");
        if (!_handlers.TryGetValue(seat.AbilityId, out var handler))
            return AbilityCommandResult.Fail("NO_ACTIVE_HANDLER", "当前职业没有已启用的主动处理器。");

        var required = seat.Definition.ReleaseModel switch
        {
            AbilityReleaseModel.A => seat.Definition.ChargeCap,
            AbilityReleaseModel.B => seat.Definition.FixedCost,
            AbilityReleaseModel.C => seat.Definition.MinimumCharge,
            _ => int.MaxValue,
        };
        if (required <= 0 || seat.Charge < required)
            return AbilityCommandResult.Fail("INSUFFICIENT_CHARGE", "当前充能不足。");
        if (!context.ProfessionConditionsValid)
            return AbilityCommandResult.Fail("PROFESSION_CONDITION_FAILED", "职业专属条件不满足。");

        var effect = handler.TryCreateEffect(seat, seat.Charge);
        if (!effect.Ok) return AbilityCommandResult.Fail(effect.Code, effect.Message);
        if (effect.EmptyCast && !seat.Definition.AllowsEmptyCast)
            return AbilityCommandResult.Fail("EMPTY_CAST_NOT_ALLOWED", "当前职业不允许空放主动技能。");

        var consumed = seat.Definition.ReleaseModel switch
        {
            AbilityReleaseModel.A => seat.Charge,
            AbilityReleaseModel.B => seat.Definition.FixedCost,
            AbilityReleaseModel.C => seat.Charge,
            _ => 0,
        };
        if (!_charges.TrySpend(seat, consumed))
            return AbilityCommandResult.Fail("CHARGE_COMMIT_FAILED", "技能效果已创建，但充能提交失败，需要管理员检查状态。");
        seat.MarkAbilityUsed();
        try
        {
            persistImmediately();
        }
        catch
        {
            return AbilityCommandResult.Fail("PERSISTENCE_FAILED", "技能已生效，但运行时立即保存失败，将继续重试。");
        }
        return AbilityCommandResult.Success(consumed);
    }
}

public sealed class AbilityHudService
{
    private readonly Dictionary<string, HudPrompt> _prompts = new(StringComparer.Ordinal);
    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;
    public void Stop()
    {
        IsRunning = false;
        _prompts.Clear();
    }

    public void SetPrompt(string seatId, string text, DateTimeOffset now, TimeSpan? duration = null) =>
        _prompts[seatId] = new(text, now + (duration ?? TimeSpan.FromSeconds(2)));

    public string Render(
        AbilitySeatState seat,
        DateTimeOffset now,
        bool hasActiveHandler,
        Func<int, string>? cModelPreview = null,
        string? roleDetailsHtml = null)
    {
        var cap = seat.Definition.ChargeCap;
        var filled = cap == 0 ? 0 : (int)Math.Floor(seat.Charge * 10d / cap);
        var bar = new string('█', filled) + new string('░', 10 - filled);
        var required = seat.Definition.ReleaseModel switch
        {
            AbilityReleaseModel.A => cap,
            AbilityReleaseModel.B => seat.Definition.FixedCost,
            AbilityReleaseModel.C => seat.Definition.MinimumCharge,
            _ => cap,
        };
        var status = !hasActiveHandler ? "主动技能未启用"
            : seat.AbilityUsedThisRound ? "本回合已使用"
            : seat.Charge >= required ? "主动技能可用"
            : "主动技能充能中";
        var preview = seat.Definition.ReleaseModel == AbilityReleaseModel.C && cModelPreview is not null
            ? $"｜{WebUtility.HtmlEncode(cModelPreview(seat.Charge))}"
            : string.Empty;
        var durationParts = new List<string>();
        foreach (var pair in seat.RecoverableUntil.ToArray())
        {
            var remaining = pair.Value - now;
            if (remaining <= TimeSpan.Zero)
            {
                seat.RecoverableUntil.Remove(pair.Key);
                continue;
            }
            durationParts.Add($"{WebUtility.HtmlEncode(pair.Key)}剩余 {remaining.TotalSeconds:0.0} 秒");
        }
        var durations = durationParts.Count == 0 ? string.Empty : $"｜{string.Join("、", durationParts)}";
        var prompt = string.Empty;
        if (_prompts.TryGetValue(seat.SeatId, out var current))
        {
            if (current.ExpiresAt > now) prompt = WebUtility.HtmlEncode(current.Text);
            else _prompts.Remove(seat.SeatId);
        }
        var details = string.IsNullOrWhiteSpace(roleDetailsHtml) ? string.Empty : $"<br>{roleDetailsHtml}";
        return $"{WebUtility.HtmlEncode(seat.Definition.Name)} [{bar}] {seat.Charge}/{cap}<br>{status}{preview}{durations}<br>{prompt}{details}";
    }

    private sealed record HudPrompt(string Text, DateTimeOffset ExpiresAt);
}

public sealed class MovementModifierService
{
    private readonly Dictionary<string, Dictionary<string, double>> _sources = new(StringComparer.Ordinal);

    public void Set(string seatId, string source, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "移动倍率必须为有限正数。");
        if (!_sources.TryGetValue(seatId, out var values))
            _sources[seatId] = values = new Dictionary<string, double>(StringComparer.Ordinal);
        values[source] = multiplier;
    }

    public bool Remove(string seatId, string source) =>
        _sources.TryGetValue(seatId, out var values) && values.Remove(source);

    public double GetCombined(string seatId) => _sources.TryGetValue(seatId, out var values)
        ? values.Values.Aggregate(1d, (current, value) => current * value)
        : 1d;

    public void ClearSeat(string seatId) => _sources.Remove(seatId);
    public void ClearAll() => _sources.Clear();
}
