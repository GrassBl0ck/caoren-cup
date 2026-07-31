using CaorenCup.WeaponPaints.Core;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class CoreBehaviorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("safe", true)]
    [InlineData("SAFE", true)]
    public void RefreshCommandMode_ParsesSafeReload(string? input, bool expected)
    {
        Assert.Equal(expected, RefreshCommandMode.IsSafe(input));
    }

    [Fact]
    public void TeamLoadouts_AreIndependent()
    {
        var loadout = new PlayerLoadout("76561198000000001");

        loadout.SetWeapon(TeamSide.Terrorist, new WeaponSelection(7, 801));
        loadout.SetWeapon(TeamSide.CounterTerrorist, new WeaponSelection(7, 302));

        Assert.Equal((uint)801, loadout.GetWeapon(TeamSide.Terrorist, 7)!.PaintId);
        Assert.Equal((uint)302, loadout.GetWeapon(TeamSide.CounterTerrorist, 7)!.PaintId);
    }

    [Theory]
    [InlineData("0", 0f)]
    [InlineData("0.123456", 0.123456f)]
    [InlineData("1", 1f)]
    public void InputValidator_AcceptsWearRange(string input, float expected)
    {
        Assert.True(CosmeticInputValidator.TryParseWear(input, out var actual, out _));
        Assert.Equal(expected, actual, 6);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("NaN")]
    [InlineData("abc")]
    public void InputValidator_RejectsInvalidWear(string input)
    {
        Assert.False(CosmeticInputValidator.TryParseWear(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("0", 0u)]
    [InlineData("1000", 1000u)]
    public void InputValidator_AcceptsSeedRange(string input, uint expected)
    {
        Assert.True(CosmeticInputValidator.TryParseSeed(input, out var actual, out _));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1001")]
    [InlineData("1.5")]
    public void InputValidator_RejectsInvalidSeed(string input)
    {
        Assert.False(CosmeticInputValidator.TryParseSeed(input, out _, out _));
    }

    [Fact]
    public void Search_UsesChineseAndEnglishFallbackNames()
    {
        var items = new[]
        {
            new CatalogItem("ak47-801", "AK-47 | 二西莫夫", "AK-47 | Asiimov"),
            new CatalogItem("awp-344", "AWP | 巨龙传说", "AWP | Dragon Lore")
        };

        Assert.Equal("ak47-801", CatalogSearch.Find(items, "asiimov", 10).Single().Key);
        Assert.Equal("awp-344", CatalogSearch.Find(items, "巨龙", 10).Single().Key);
    }

    [Theory]
    [InlineData(true, true, true, true, false, RefreshDecision.ApplyNow)]
    [InlineData(true, true, true, false, true, RefreshDecision.ApplyNow)]
    [InlineData(true, true, true, false, false, RefreshDecision.QueueForSpawn)]
    [InlineData(true, true, false, false, false, RefreshDecision.QueueForSpawn)]
    [InlineData(false, true, true, true, false, RefreshDecision.Reject)]
    [InlineData(true, false, true, true, false, RefreshDecision.Reject)]
    public void RefreshPolicy_ProtectsLivePlayers(
        bool enabled,
        bool dataLoaded,
        bool alive,
        bool warmup,
        bool force,
        RefreshDecision expected)
    {
        var context = new RefreshContext(enabled, dataLoaded, alive, warmup, isOfficialRound: true, force);
        Assert.Equal(expected, RefreshPolicy.Decide(context));
    }

    [Fact]
    public void HealthReport_DistinguishesDisabledAndDegraded()
    {
        Assert.Equal(HealthLevel.Disabled, PluginHealth.Disabled().Level);

        var health = new PluginHealth(
            enabled: true,
            databaseConnected: false,
            schemaReady: true,
            catalogReady: true,
            gameDataReady: true);

        Assert.Equal(HealthLevel.Degraded, health.Level);
        Assert.Contains("数据库", health.ToChineseSummary());
    }
}
