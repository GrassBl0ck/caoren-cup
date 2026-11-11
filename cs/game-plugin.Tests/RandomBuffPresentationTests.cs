using CaorenCup.Gamemodes.Random;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class RandomBuffPresentationTests
{
    [Theory]
    [InlineData(RandomCatalog.Reload, 1, 15, "恢复护甲")]
    [InlineData(RandomCatalog.Reload, 2, 20, "恢复较多护甲")]
    [InlineData(RandomCatalog.Reload, 3, 25, "恢复大量护甲")]
    [InlineData(RandomCatalog.Medkit, 1, 15, "回复生命")]
    [InlineData(RandomCatalog.Medkit, 2, 20, "回复较多生命")]
    [InlineData(RandomCatalog.Medkit, 3, 25, "回复大量生命")]
    public void Each_level_describes_only_its_effect_without_an_upgrade_appendix(string key, int level, int amount, string simpleEffect)
    {
        var definition = RandomCatalog.Cards.Single(card => card.Key == key);
        var view = definition.View(level);

        Assert.Equal(definition.Name, view.Name);
        Assert.Equal(level, view.Level);
        Assert.Equal(3, view.MaxLevel);
        Assert.Equal(definition.Cost, view.Cost);
        Assert.Equal("D", view.Quality);
        Assert.Contains(simpleEffect, view.SimpleDescription);
        Assert.DoesNotMatch("[0-9]", view.SimpleDescription);
        Assert.DoesNotContain("升级", view.SimpleDescription);
        Assert.DoesNotContain("升级", view.DetailedDescription);
        Assert.DoesNotContain("1/2/3", view.DetailedDescription);
        Assert.Contains($"{amount}点", view.DetailedDescription);
        foreach (var otherAmount in new[] { 15, 20, 25 }.Where(value => value != amount))
            Assert.DoesNotContain($"{otherAmount}点", view.DetailedDescription);

        if (key == RandomCatalog.Reload)
        {
            Assert.Contains("每回合首次有效换弹", view.DetailedDescription);
            Assert.Contains("最高不超过100", view.DetailedDescription);
            Assert.Contains("实际从备用弹药装入弹匣", view.DetailedDescription);
            Assert.Contains("直接补弹不触发", view.DetailedDescription);
        }
        else
        {
            Assert.Contains("受到敌方伤害后，若仍存活且生命偏低", view.SimpleDescription);
            Assert.Contains("每回合一次", view.DetailedDescription);
            Assert.Contains("受到敌方伤害后仍存活且生命低于50", view.DetailedDescription);
            Assert.Contains("不超过自身生命上限", view.DetailedDescription);
        }
    }

    [Fact]
    public void Unique_card_keeps_its_single_level_and_full_trigger_conditions()
    {
        var definition = RandomCatalog.Cards.Single(card => card.Key == RandomCatalog.Ghost);
        var view = definition.View(1);

        Assert.Equal("幽灵快递", view.Name);
        Assert.Equal(1, view.MaxLevel);
        Assert.Equal(definition.Cost, view.Cost);
        Assert.DoesNotMatch("[0-9]", view.SimpleDescription);
        Assert.DoesNotContain("升级", view.SimpleDescription);
        Assert.Contains("每回合首次被敌方直接击杀", view.DetailedDescription);
        Assert.Contains("从死亡位置抛出1颗普通诱饵弹", view.DetailedDescription);
        Assert.Contains("方向随机，不造成额外伤害", view.DetailedDescription);
    }

    [Theory]
    [InlineData(RandomCatalog.Reload)]
    [InlineData(RandomCatalog.Medkit)]
    public void Offers_use_target_level_while_owned_and_admin_snapshots_use_current_level(string key)
    {
        var definition = RandomCatalog.Cards.Single(card => card.Key == key);
        var match = new RandomMatch([definition], shuffle: _ => { }, roundCoupons: definition.Cost);
        match.Start();
        match.ObservePlayer(new RandomPlayerIdentity("p", "玩家", 2));

        for (var level = 1; level <= 3; level++)
        {
            Assert.True(match.BeginRound(level, ["p"]));
            var before = match.View("p", true)!;
            var offer = before.Offers.Single(card => card.Key == key);
            Assert.Equal(level, offer.Level);
            Assert.Contains($"{10 + level * 5}点", offer.DetailedDescription);
            if (level > 1)
            {
                var owned = Assert.Single(before.Owned);
                Assert.Equal(level - 1, owned.Level);
                Assert.Contains($"{10 + (level - 1) * 5}点", owned.DetailedDescription);
                Assert.NotEqual(owned.SimpleDescription, offer.SimpleDescription);
            }
            Assert.True(match.Purchase("p", key, before.Revision, true).Success);
            var after = match.View("p", true)!;
            Assert.Equal(0, after.Coupons);
            Assert.Equal(offer, Assert.Single(after.Owned));
            Assert.Equal(offer, Assert.Single(Assert.Single(match.AdminSnapshot([]).Players).Owned));
            Assert.True(match.EndRound(level));
        }

        Assert.True(match.BeginRound(4, ["p"]));
        Assert.DoesNotContain(match.View("p", true)!.Offers, card => card.Key == key);
    }

    [Fact]
    public void Definitions_without_level_presentations_keep_their_supplied_descriptions()
    {
        var definition = new BuffDefinition("custom", "D-9", "测试卡", "防御", "简洁", "详细", 3);
        var view = definition.View(2);
        Assert.Equal("简洁", view.SimpleDescription);
        Assert.Equal("详细", view.DetailedDescription);
        Assert.Equal("测试卡", view.Name);
        Assert.Equal(2, view.Level);
    }
}
