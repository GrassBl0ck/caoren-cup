using CaorenCup.Contracts;
using CaorenCup.Gamemodes.Random;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class RandomEconomyTests
{
    private static RandomMatch Start(IReadOnlyList<BuffDefinition>? cards = null, params string[] players)
    {
        var match = new RandomMatch(cards, shuffle: _ => { }, roll: () => 0);
        match.Start();
        foreach (var id in players) match.ObservePlayer(new(id, id, 2));
        match.BeginRound(1, players);
        return match;
    }

    private static BuffDefinition[] Pool(int count = 7) => Enumerable.Range(0, count)
        .Select(index => new BuffDefinition("card" + index, "D-" + index, "卡" + index, "战术", "简", "详", 2)).ToArray();

    [Fact]
    public void Approved_catalog_has_all_cards_and_levels_with_fixed_prices()
    {
        Assert.Equal(46, RandomCatalog.Cards.Count);
        Assert.Equal(66, RandomCatalog.Cards.Sum(card => card.MaxLevel));
        Assert.Equal(46, RandomCatalog.Cards.Select(card => card.Key).Distinct().Count());
        foreach (var card in RandomCatalog.Cards)
        {
            Assert.Equal(card.MaxLevel, card.LevelDescriptions.Count);
            for (var level = 1; level <= card.MaxLevel; level++)
            {
                Assert.Equal(card.Cost, card.View(level).Cost);
                Assert.False(string.IsNullOrWhiteSpace(card.View(level).DetailedDescription));
            }
        }
        Assert.Equal(0, RandomCatalog.Find("幽灵快递")!.Cost);
        Assert.Equal(15, RandomCatalog.Find("人形保险箱")!.Cost);
        Assert.Equal("团队", RandomCatalog.Find("队友倒下我站起")!.Scope);
    }

    [Fact]
    public void Performance_is_immediate_capped_and_deduplicated_with_partial_kill_reward()
    {
        var match = Start(null, "p");
        Assert.Equal(4, match.View("p", true)!.Coupons);
        Assert.Equal(new CouponAward(1, 1), match.AwardPerformance("p", RandomPerformanceEvent.Assist, "death1"));
        Assert.Equal(new CouponAward(0, 1), match.AwardPerformance("p", RandomPerformanceEvent.Assist, "death1"));
        match.AwardPerformance("p", RandomPerformanceEvent.Plant, "plant");
        match.AwardPerformance("p", RandomPerformanceEvent.Death, "death2");
        Assert.Equal(new CouponAward(1, 4), match.AwardPerformance("p", RandomPerformanceEvent.Kill, "death3"));
        Assert.Equal(new CouponAward(0, 4), match.AwardPerformance("p", RandomPerformanceEvent.Defuse, "defuse"));
        Assert.Equal(8, match.View("p", true)!.Coupons);
        match.ObservePlayer(new("p", "重连", 3));
        Assert.Equal(4, match.View("p", true)!.PerformanceCoupons);
        Assert.False(match.BeginRound(1, ["p"]));
        Assert.Equal(8, match.View("p", true)!.Coupons);
        match.EndRound(1);
        Assert.Equal(default, match.AwardPerformance("p", RandomPerformanceEvent.Kill, "after"));
        match.BeginRound(2, ["p"]);
        Assert.Equal(new CouponAward(2, 2), match.AwardPerformance("p", RandomPerformanceEvent.Kill, "death3"));
        Assert.Equal(14, match.View("p", true)!.Coupons);
    }

    [Fact]
    public void Free_card_consumes_purchase_and_retained_lock_survives_other_purchase_and_reroll()
    {
        var pool = Pool();
        pool[0] = pool[0] with { Cost = 0 };
        var match = Start(pool, "p");
        var view = match.View("p", true)!;
        Assert.True(match.SetLock("p", "card1", view.Revision, true).Success);
        view = match.View("p", true)!;
        Assert.True(match.Purchase("p", "card0", view.Revision, true).Success);
        Assert.Equal(4, match.View("p", true)!.Coupons);
        Assert.False(match.Purchase("p", "card1", match.View("p", true)!.Revision, true).Success);
        match.EndRound(1); match.BeginRound(2, ["p"]);
        view = match.View("p", true)!;
        Assert.Equal("card1", view.LockedKey);
        var previous = view.Offers.Select(card => card.Key).ToArray();
        Assert.True(match.Reroll("p", view.Revision, true).Success);
        view = match.View("p", true)!;
        Assert.Equal(["card1"], previous.Intersect(view.Offers.Select(card => card.Key)));
        Assert.True(match.Purchase("p", "card1", view.Revision, true).Success);
        Assert.Empty(match.View("p", true)!.LockedKey);
    }

    [Theory]
    [InlineData(1, 35, 40, 20, 4, 1)]
    [InlineData(8, 35, 40, 20, 4, 1)]
    [InlineData(9, 15, 30, 35, 15, 5)]
    [InlineData(16, 15, 30, 35, 15, 5)]
    [InlineData(17, 5, 15, 40, 25, 15)]
    [InlineData(24, 5, 15, 40, 25, 15)]
    public void Quality_weights_follow_total_rounds(int round, int d, int c, int b, int a, int s) =>
        Assert.Equal(new[] { d, c, b, a, s }, new[] { "D", "C", "B", "A", "S" }.Select(key => RandomMatch.QualityWeights(round)[key]));

    [Fact]
    public void Empty_quality_renormalizes_and_small_pool_cannot_charge_for_reroll()
    {
        var match = Start(Pool(4).Select(card => card with { Quality = "S" }).ToArray(), "p");
        var view = match.View("p", true)!;
        Assert.Equal(3, view.Offers.Count);
        Assert.All(view.Offers, card => Assert.Equal("S", card.Quality));
        Assert.False(view.CanReroll);
        Assert.False(match.Reroll("p", view.Revision, true).Success);
        Assert.Equal(4, match.View("p", true)!.Coupons);
    }

    [Fact]
    public void Team_upgrade_releases_maxed_locks_and_follows_halftime_but_not_voluntary_switch()
    {
        var card = Pool(1)[0] with { Key = "team", Scope = "团队" };
        var match = Start([card, .. Pool(5)], "p", "q");
        var view = match.View("q", true)!;
        Assert.True(match.SetLock("q", "team", view.Revision, true).Success);
        Assert.True(match.DebugChange(new("give", "p", "team", 2)).Success);
        Assert.Equal(2, match.Level("q", "team"));
        Assert.Empty(match.View("q", true)!.LockedKey);
        Assert.Equal(3, match.View("q", true)!.Offers.Count);
        match.EndRound(1);
        match.ObservePlayer(new("p", "p", 3));
        match.ObservePlayer(new("q", "q", 3));
        match.BeginRound(13, ["p", "q"]);
        Assert.Equal(2, match.Level("p", "team"));
        match.ObservePlayer(new("p", "p", 2));
        Assert.Equal(0, match.Level("p", "team"));
        Assert.Equal(2, match.Level("q", "team"));
    }

    [Fact]
    public void Reservations_commit_only_success_release_on_failure_exception_cancel_and_end_round()
    {
        var match = Start(null, "p");
        Assert.True(match.DebugChange(new("give", "p", "he_supply")).Success);
        Assert.True(match.TryReserve("p", "he_supply", 1, 6, 0, out var first));
        Assert.False(match.TryReserve("p", "he_supply", 1, 6, 0, out _));
        Assert.False(match.Complete(first, () => false, 0));
        Assert.Equal(0, match.Usage("p", "he_supply").Pending);
        Assert.True(match.TryReserve("p", "he_supply", 1, 6, 0, out first));
        Assert.Throws<InvalidOperationException>(() => match.Complete(first, () => throw new InvalidOperationException(), 0));
        Assert.True(match.TryReserve("p", "he_supply", 1, 6, 0, out first));
        match.EndRound(1);
        Assert.Equal(0, match.Usage("p", "he_supply").Pending);
        Assert.False(match.Complete(first, () => throw new Exception("must not execute"), 0));
        match.BeginRound(2, ["p"]);
        Assert.True(match.TryReserve("p", "he_supply", 3, 6, 0, out first));
        Assert.True(match.Complete(first, () => true, 1));
        Assert.Equal(1, match.Usage("p", "he_supply").Used);
        Assert.False(match.TryReserve("p", "he_supply", 3, 6, 6, out _));
        Assert.True(match.TryReserve("p", "he_supply", 3, 6, 7, out first));
        match.Cancel(first);
        Assert.Equal(1, match.Usage("p", "he_supply").Used);
    }

    [Fact]
    public void Debug_edits_preserve_used_quota_and_explicit_reset_cancels_pending_tasks()
    {
        var match = Start(null, "p");
        Assert.True(match.DebugChange(new("give", "p", RandomCatalog.Reload, 3)).Success);
        Assert.Equal(25, match.ReloadArmor("p", 0, true));
        Assert.True(match.DebugChange(new("set", "p", RandomCatalog.Reload, 1)).Success);
        Assert.Equal(0, match.ReloadArmor("p", 0, true));
        Assert.True(match.DebugChange(new("reset", "p", RandomCatalog.Reload)).Success);
        Assert.Equal(15, match.ReloadArmor("p", 0, true));
        Assert.True(match.DebugChange(new("give", "p", "smoke_train")).Success);
        Assert.True(match.TryReserve("p", "smoke_train", 1, 0, 0, out var pending));
        Assert.True(match.DebugChange(new("remove", "p", "smoke_train")).Success);
        Assert.False(match.IsPending(pending));
        var before = match.View("p", true)!;
        Assert.False(match.DebugChange(new("set", "p", RandomCatalog.Reload, 2, before.Revision - 1)).Success);
        Assert.Equal(4, match.View("p", true)!.Coupons);
        Assert.False(match.DebugChange(new("give", "missing", RandomCatalog.Reload)).Success);
    }

    [Fact]
    public void Supply_channels_are_independent_and_reset_releases_all_channels()
    {
        var match = Start(null, "p");
        Assert.True(match.DebugChange(new("give", "p", "arsenal")).Success);
        Assert.True(match.TryReserve("p", "arsenal", 1, 0, 0, out var round, channel: "round"));
        Assert.True(match.Complete(round, () => true, 0));
        Assert.False(match.TryReserve("p", "arsenal", 1, 0, 0, out _, channel: "round"));
        Assert.True(match.TryReserve("p", "arsenal", 1, 0, 0, out var empty, channel: "empty"));
        Assert.True(match.DebugChange(new("reset", "p", "arsenal")).Success);
        Assert.False(match.IsPending(empty));
        Assert.Equal(0, match.Usage("p", "arsenal", "round").Used);
        Assert.Equal(0, match.Usage("p", "arsenal", "empty").Pending);
    }

    [Fact]
    public void Shared_flash_quota_caps_both_cards_and_reset_restores_only_selected_consumption()
    {
        var match = Start(null, "p");
        match.DebugChange(new("give", "p", "flash_partner"));
        match.DebugChange(new("give", "p", "flash_chain"));
        Assert.True(match.TryUse("p", "flash_partner", 1, 0, 0));
        Assert.True(match.TryUse("p", "flash_chain", 2, 0, 0));
        Assert.True(match.TryReserve("p", "flash_chain", 2, 0, 0, out var pending));
        Assert.False(match.TryReserve("p", "flash_chain", 2, 0, 0, out _));
        match.ResetUsage("p", "flash_partner");
        Assert.True(match.IsPending(pending));
        Assert.True(match.TryUse("p", "flash_partner", 1, 0, 0));
        Assert.True(match.Complete(pending, () => true, 0));
        Assert.Equal(2, match.Usage("p", "flash_chain").Used);
    }

    [Fact]
    public void Pending_cooldown_effect_cannot_reserve_another_effect_before_first_completes()
    {
        var match = Start(null, "p");
        match.DebugChange(new("give", "p", "he_supply"));
        Assert.True(match.TryReserve("p", "he_supply", 3, 6, 0, out var pending));
        Assert.False(match.TryReserve("p", "he_supply", 3, 6, 0.1, out _));
        Assert.True(match.Complete(pending, () => true, 1));
        Assert.False(match.TryReserve("p", "he_supply", 3, 6, 6.9, out _));
        Assert.True(match.TryReserve("p", "he_supply", 3, 6, 7, out _));
    }

    [Fact]
    public void Extra_jump_usage_survives_reconnect_and_card_edits_but_landing_or_reset_restores_it()
    {
        var match = Start(null, "p");
        match.DebugChange(new("give", "p", "extra_jump", 3));
        match.SetExtraJumpsUsed("p", 2);
        match.ObservePlayer(new("p", "重连", 2));
        match.DebugChange(new("set", "p", "extra_jump", 1));
        Assert.Equal(2, match.ExtraJumpsUsed("p"));
        match.DebugChange(new("reset", "p", "extra_jump"));
        Assert.Equal(0, match.ExtraJumpsUsed("p"));
        match.SetExtraJumpsUsed("p", 1);
        match.SetExtraJumpsUsed("p", 0);
        Assert.Equal(0, match.ExtraJumpsUsed("p"));
    }

    [Fact]
    public void Tank_growth_caps_at_five_and_health_stays_within_total_limit()
    {
        var match = Start(null, "p");
        match.DebugChange(new("give", "p", RandomCatalog.Tank));
        for (var i = 0; i < 5; i++) Assert.True(match.AddGrowth("p", RandomCatalog.Tank));
        Assert.False(match.AddGrowth("p", RandomCatalog.Tank));
        Assert.Equal(200, RandomAttributes.MaxHealth(100, true, match.Growth("p", RandomCatalog.Tank)));
        Assert.Equal(150, RandomAttributes.MaxHealth(100, true, 0));
        Assert.Equal(100, RandomAttributes.MaxHealth(100, false, 5));
        match.DebugChange(new("remove", "p", RandomCatalog.Tank));
        Assert.Equal(0, match.Growth("p", RandomCatalog.Tank));
    }

    [Fact]
    public void Diagonal_flat_speed_is_added_after_percentage_and_then_clamped_to_held_weapon_cap()
    {
        Assert.Equal(290, RandomAttributes.Speed(250, false, false, 0, 0, false, 0, false, true), 3);
        Assert.Equal(270, RandomAttributes.Speed(250, true, false, 0, 0, false, 0, false, true), 3);
        Assert.Equal(375, RandomAttributes.Speed(250, false, true, 5, 2, true, 2, true, true), 3);
        Assert.Equal(150, RandomAttributes.Speed(100, false, true, 5, 2, true, 2, true, true), 3);
    }

    [Fact]
    public void Returning_in_new_round_resets_jumps_without_minting_historical_round_coupons()
    {
        var match = Start(null, "p");
        match.DebugChange(new("give", "p", "extra_jump", 3));
        match.SetExtraJumpsUsed("p", 2);
        match.EndRound(1); match.BeginRound(2, []);
        match.ObservePlayer(new("p", "重连", 2));
        Assert.Equal(0, match.ExtraJumpsUsed("p"));
        Assert.Equal(4, match.View("p", true)!.Coupons);
    }

    [Fact]
    public void Reading_usage_does_not_create_a_counter_or_change_revision()
    {
        var match = Start(null, "p");
        var before = match.View("p", true)!;
        Assert.Equal(default, match.Usage("p", "smoke_train"));
        Assert.Equal(before.Revision, match.View("p", true)!.Revision);
        match.DebugChange(new("give", "p", "smoke_train"));
        var generation = match.Usage("p", "smoke_train").Generation;
        Assert.True(match.TryReserve("p", "smoke_train", 1, 0, 0, out _));
        Assert.Equal(generation, match.Usage("p", "smoke_train").Generation);
    }
}
