using CaorenCup.Features;
using Xunit;

namespace CaorenCup.GamePlugin.Tests.RandomNade;

public sealed class RandomNadeRulesTests
{
    [Fact]
    public void Resolve_DefaultWeights_RemainsUnchanged()
    {
        var effective = RandomNadeRules.ResolveEffectiveWeights(new RandomNadeWeights(8, 40, 40, 2, 10));

        AssertWeights(effective, smoke: 8, fire: 40, he: 40, flash: 2, decoy: 10);
    }

    [Fact]
    public void Resolve_UnderOneHundred_DistributesRemainderByDefaultRatio()
    {
        var effective = RandomNadeRules.ResolveEffectiveWeights(new RandomNadeWeights(0, 0, 40, 2, 10));

        AssertWeights(effective, smoke: 3.84, fire: 19.2, he: 59.2, flash: 2.96, decoy: 14.8);
    }

    [Fact]
    public void Resolve_OverOneHundred_TruncatesByConfiguredPriority()
    {
        var effective = RandomNadeRules.ResolveEffectiveWeights(new RandomNadeWeights(30, 40, 40, 0, 0));

        AssertWeights(effective, smoke: 20, fire: 40, he: 40, flash: 0, decoy: 0);
    }

    [Fact]
    public void ApplyArguments_TotalChanceOnly_KeepsAllGrenadeWeights()
    {
        var current = new RandomNadeCommandState(
            Enabled: false,
            TotalChance: 100,
            Weights: new RandomNadeWeights(8, 40, 40, 2, 10));

        var result = RandomNadeRules.ApplyArguments(current, ["90"]);

        Assert.True(result.Success);
        Assert.True(result.State.Enabled);
        Assert.Equal(90, result.State.TotalChance, 6);
        Assert.Equal(current.Weights, result.State.Weights);
    }

    [Fact]
    public void ApplyArguments_Zero_DisablesWithoutChangingConfiguration()
    {
        var current = new RandomNadeCommandState(
            Enabled: true,
            TotalChance: 75,
            Weights: new RandomNadeWeights(1, 2, 3, 4, 5));

        var result = RandomNadeRules.ApplyArguments(current, ["0"]);

        Assert.True(result.Success);
        Assert.False(result.State.Enabled);
        Assert.Equal(current.TotalChance, result.State.TotalChance);
        Assert.Equal(current.Weights, result.State.Weights);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("not-a-number")]
    [InlineData("NaN")]
    public void ApplyArguments_InvalidPercentage_IsRejected(string value)
    {
        var current = RandomNadeCommandState.Default;

        var result = RandomNadeRules.ApplyArguments(current, ["100", value]);

        Assert.False(result.Success);
        Assert.Equal(current, result.State);
    }

    [Fact]
    public void ApplyArguments_AnyPositiveTotalChance_EnablesFeature()
    {
        var result = RandomNadeRules.ApplyArguments(RandomNadeCommandState.Default, ["0.000001"]);

        Assert.True(result.Success);
        Assert.True(result.State.Enabled);
        Assert.Equal(0.000001, result.State.TotalChance, 9);
    }

    [Theory]
    [InlineData(0, RandomNadeType.Smoke)]
    [InlineData(7.999, RandomNadeType.Smoke)]
    [InlineData(8, RandomNadeType.Fire)]
    [InlineData(47.999, RandomNadeType.Fire)]
    [InlineData(48, RandomNadeType.HighExplosive)]
    [InlineData(87.999, RandomNadeType.HighExplosive)]
    [InlineData(88, RandomNadeType.Flash)]
    [InlineData(90, RandomNadeType.Decoy)]
    [InlineData(99.999, RandomNadeType.Decoy)]
    public void Select_UsesResolvedPercentageRanges(double roll, RandomNadeType expected)
    {
        var selected = RandomNadeRules.Select(RandomNadeWeights.Default, roll);

        Assert.Equal(expected, selected);
    }

    private static void AssertWeights(
        RandomNadeWeights actual,
        double smoke,
        double fire,
        double he,
        double flash,
        double decoy)
    {
        Assert.Equal(smoke, actual.Smoke, 6);
        Assert.Equal(fire, actual.Fire, 6);
        Assert.Equal(he, actual.HighExplosive, 6);
        Assert.Equal(flash, actual.Flash, 6);
        Assert.Equal(decoy, actual.Decoy, 6);
        Assert.Equal(100, actual.Total, 6);
    }
}
