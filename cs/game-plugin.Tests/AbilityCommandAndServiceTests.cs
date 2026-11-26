using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityCommandAndServiceTests
{
    [Fact]
    public void Charge_is_capped_and_overflow_is_discarded()
    {
        var seat = Seat(new("test-a", "测试A", 12, AbilityReleaseModel.A));
        var charges = new ChargeService();

        Assert.Equal(12, charges.Add(seat, 20, ChargeReason.Test));
        Assert.Equal(0, charges.Add(seat, 1, ChargeReason.Test));
        Assert.Equal(12, seat.Charge);
    }

    [Fact]
    public void Buy_charge_checks_all_conditions_before_debiting()
    {
        var seat = Seat(new("test-a", "测试A", 12, AbilityReleaseModel.A));
        var money = new FakeMoney(1_000);
        var service = new AbilityCommandService(new ChargeService());

        var result = service.BuyCharge(seat, new(false, true, true, true, true), money, () => { });

        Assert.False(result.Ok);
        Assert.Equal("INVALID_MATCH", result.Code);
        Assert.Equal(1_000, money.Balance);
        Assert.Equal(0, seat.Charge);
        Assert.False(seat.ChargePurchasedThisRound);
    }

    [Fact]
    public void Buy_charge_succeeds_once_per_round_and_full_charge_never_costs_money()
    {
        var seat = Seat(new("test-a", "测试A", 2, AbilityReleaseModel.A));
        var charges = new ChargeService();
        var money = new FakeMoney(2_000);
        var service = new AbilityCommandService(charges);
        var valid = new ChargePurchaseContext(true, true, true, true, true);

        Assert.True(service.BuyCharge(seat, valid, money, () => { }).Ok);
        Assert.Equal(1_250, money.Balance);
        Assert.Equal(1, seat.Charge);
        Assert.Equal("ALREADY_PURCHASED", service.BuyCharge(seat, valid, money, () => { }).Code);

        var fullSeat = Seat(new("test-full", "测试满充", 2, AbilityReleaseModel.A));
        charges.Add(fullSeat, 10, ChargeReason.Test);
        Assert.Equal("CHARGE_FULL", service.BuyCharge(fullSeat, valid, money, () => { }).Code);
        Assert.Equal(1_250, money.Balance);
    }

    [Fact]
    public void Buy_charge_rolls_back_money_charge_and_round_usage_when_persistence_fails()
    {
        var seat = Seat(new("test-a", "测试A", 12, AbilityReleaseModel.A));
        var money = new FakeMoney(1_000);
        var service = new AbilityCommandService(new ChargeService());

        var result = service.BuyCharge(
            seat,
            new(true, true, true, true, true),
            money,
            () => throw new IOException("disk full"));

        Assert.False(result.Ok);
        Assert.Equal("PURCHASE_ROLLED_BACK", result.Code);
        Assert.Equal(1_000, money.Balance);
        Assert.Equal(0, seat.Charge);
        Assert.False(seat.ChargePurchasedThisRound);
    }

    [Fact]
    public void Failed_refund_is_reported_as_consistency_error_instead_of_silently_eating_money()
    {
        var seat = Seat(new("test-a", "测试A", 12, AbilityReleaseModel.A));
        var money = new FakeMoney(1_000) { RejectCredit = true };
        var service = new AbilityCommandService(new ChargeService());

        var result = service.BuyCharge(
            seat,
            new(true, true, true, true, true),
            money,
            () => throw new IOException("disk full"));

        Assert.Equal("PURCHASE_CONSISTENCY_ERROR", result.Code);
        Assert.Equal(250, money.Balance);
        Assert.Equal(0, seat.Charge);
        Assert.False(seat.ChargePurchasedThisRound);
    }

    [Theory]
    [InlineData(AbilityReleaseModel.A, 12, 0, 0, 12, 0)]
    [InlineData(AbilityReleaseModel.B, 12, 8, 0, 10, 2)]
    [InlineData(AbilityReleaseModel.C, 12, 0, 6, 9, 0)]
    public void Successful_ult_consumes_charge_according_to_model(
        AbilityReleaseModel model,
        int cap,
        int fixedCost,
        int minimum,
        int initial,
        int expectedRemaining)
    {
        var seat = Seat(new("test", "测试", cap, model, fixedCost, minimum));
        var charges = new ChargeService();
        charges.Add(seat, initial, ChargeReason.Test);
        var handler = new RecordingHandler(AbilityEffectResult.Succeeded());
        var service = new AbilityCommandService(charges, new Dictionary<string, IAbilityEffectHandler> { ["test"] = handler });

        var result = service.UseUltimate(seat, ValidUlt(), () => { });

        Assert.True(result.Ok);
        Assert.Equal(initial, handler.ReceivedCharge);
        Assert.Equal(expectedRemaining, seat.Charge);
        Assert.True(seat.AbilityUsedThisRound);
    }

    [Fact]
    public void Ult_failure_does_not_consume_charge_or_round_usage()
    {
        var seat = Seat(new("test", "测试", 10, AbilityReleaseModel.B, FixedCost: 5));
        var charges = new ChargeService();
        charges.Add(seat, 8, ChargeReason.Test);
        var handler = new RecordingHandler(AbilityEffectResult.Failed("NO_TARGET", "没有合法目标。"));
        var service = new AbilityCommandService(charges, new Dictionary<string, IAbilityEffectHandler> { ["test"] = handler });

        var result = service.UseUltimate(seat, ValidUlt(), () => { });

        Assert.Equal("NO_TARGET", result.Code);
        Assert.Equal(8, seat.Charge);
        Assert.False(seat.AbilityUsedThisRound);
    }

    [Fact]
    public void Ult_checks_missing_handler_and_charge_before_creating_effect()
    {
        var seat = Seat(new("test", "测试", 12, AbilityReleaseModel.A));
        var charges = new ChargeService();
        var handler = new RecordingHandler(AbilityEffectResult.Succeeded());

        var missing = new AbilityCommandService(charges).UseUltimate(seat, ValidUlt(), () => { });
        Assert.Equal("NO_ACTIVE_HANDLER", missing.Code);

        var service = new AbilityCommandService(charges, new Dictionary<string, IAbilityEffectHandler> { ["test"] = handler });
        var insufficient = service.UseUltimate(seat, ValidUlt(), () => { });
        Assert.Equal("INSUFFICIENT_CHARGE", insufficient.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Successful_ult_can_only_be_used_once_per_round()
    {
        var seat = Seat(new("test", "测试", 5, AbilityReleaseModel.A));
        var charges = new ChargeService();
        charges.Add(seat, 5, ChargeReason.Test);
        var handler = new RecordingHandler(AbilityEffectResult.Succeeded());
        var service = new AbilityCommandService(charges, new Dictionary<string, IAbilityEffectHandler> { ["test"] = handler });

        Assert.True(service.UseUltimate(seat, ValidUlt(), () => { }).Ok);
        charges.Add(seat, 5, ChargeReason.Test);
        Assert.Equal("ALREADY_USED", service.UseUltimate(seat, ValidUlt(), () => { }).Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Empty_cast_succeeds_only_when_profession_metadata_explicitly_allows_it()
    {
        var deniedSeat = Seat(new("denied", "禁止空放", 5, AbilityReleaseModel.A));
        var allowedSeat = Seat(new("allowed", "允许空放", 5, AbilityReleaseModel.A, AllowsEmptyCast: true));
        var charges = new ChargeService();
        charges.Add(deniedSeat, 5, ChargeReason.Test);
        charges.Add(allowedSeat, 5, ChargeReason.Test);
        var handlers = new Dictionary<string, IAbilityEffectHandler>
        {
            ["denied"] = new RecordingHandler(AbilityEffectResult.Succeeded(emptyCast: true)),
            ["allowed"] = new RecordingHandler(AbilityEffectResult.Succeeded(emptyCast: true)),
        };
        var service = new AbilityCommandService(charges, handlers);

        var denied = service.UseUltimate(deniedSeat, ValidUlt(), () => { });
        var allowed = service.UseUltimate(allowedSeat, ValidUlt(), () => { });

        Assert.Equal("EMPTY_CAST_NOT_ALLOWED", denied.Code);
        Assert.Equal(5, deniedSeat.Charge);
        Assert.False(deniedSeat.AbilityUsedThisRound);
        Assert.True(allowed.Ok);
        Assert.Equal(0, allowedSeat.Charge);
        Assert.True(allowedSeat.AbilityUsedThisRound);
    }

    [Fact]
    public void Hud_contains_own_seat_state_and_expires_temporary_prompt()
    {
        var seat = Seat(new("test-c", "测试C", 12, AbilityReleaseModel.C, MinimumCharge: 6));
        var charges = new ChargeService();
        charges.Add(seat, 8, ChargeReason.Test);
        var hud = new AbilityHudService();
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        seat.RecoverableUntil["测试状态"] = now.AddSeconds(6);
        hud.Start();
        hud.SetPrompt(seat.SeatId, "击杀敌人，充能 +2", now);

        var active = hud.Render(seat, now.AddSeconds(1), true, charge => $"持续 {charge * 2} 秒");
        var expired = hud.Render(seat, now.AddSeconds(3), true, charge => $"持续 {charge * 2} 秒");

        Assert.Contains("测试C", active);
        Assert.Contains("8/12", active);
        Assert.Contains("持续 16 秒", active);
        Assert.Contains("测试状态剩余 5.0 秒", active);
        Assert.Contains("击杀敌人", active);
        Assert.DoesNotContain("击杀敌人", expired);
        hud.Stop();
        Assert.False(hud.IsRunning);
    }

    [Fact]
    public void Movement_sources_multiply_and_can_be_removed_independently()
    {
        var movement = new MovementModifierService();
        movement.Set("A1", "base", 0.9);
        movement.Set("A1", "medic", 1.1);
        movement.Set("A1", "witch", 0.85);

        Assert.Equal(0.8415, movement.GetCombined("A1"), 4);
        Assert.True(movement.Remove("A1", "medic"));
        Assert.Equal(0.765, movement.GetCombined("A1"), 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.Set("A1", "bad", 0));
        movement.ClearSeat("A1");
        Assert.Equal(1, movement.GetCombined("A1"));
    }

    [Fact]
    public void Production_c_model_metadata_exposes_confirmed_hud_previews_without_handlers()
    {
        var catalog = AbilityDefinitionCatalog.CreateProduction();

        Assert.Contains("25%", catalog.Get("berserker").CModelPreview!(10));
        Assert.Contains("30%", catalog.Get("capitalist").CModelPreview!(11));
        Assert.Contains("15", catalog.Get("snow_golem").CModelPreview!(9));
    }

    private static AbilityUseContext ValidUlt() => new(true, true, true, true, true);

    private static AbilitySeatState Seat(AbilityDefinition definition) => new(
        new AbilitySyncSeat
        {
            PlayerId = "A1",
            SteamId = "76561198000000001",
            RosterTeam = "A",
            InitialSide = "CT",
            AbilityId = definition.Id,
        },
        definition);

    private sealed class FakeMoney(int balance) : IMoneyAccount
    {
        public int Balance { get; private set; } = balance;
        public bool RejectCredit { get; init; }
        public bool TryDebit(int amount)
        {
            if (Balance < amount) return false;
            Balance -= amount;
            return true;
        }
        public bool TryCredit(int amount)
        {
            if (RejectCredit) return false;
            Balance += amount;
            return true;
        }
    }

    private sealed class RecordingHandler(AbilityEffectResult result) : IAbilityEffectHandler
    {
        public int Calls { get; private set; }
        public int ReceivedCharge { get; private set; }
        public AbilityEffectResult TryCreateEffect(AbilitySeatState seat, int currentCharge)
        {
            Calls++;
            ReceivedCharge = currentCharge;
            return result;
        }
    }
}
