using CaorenCup;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class DashParameterTests
{
    [Fact]
    public void Dash_keeps_existing_float_value()
    {
        Assert.True(CaorenCupUtils.TryParseOptionalFloat("-", 1.25f, out var value));
        Assert.Equal(1.25f, value);
    }

    [Fact]
    public void Dash_keeps_existing_int_value()
    {
        Assert.True(CaorenCupUtils.TryParseOptionalInt("-", 150, out var value));
        Assert.Equal(150, value);
    }

    [Fact]
    public void Invalid_optional_value_is_rejected()
    {
        Assert.False(CaorenCupUtils.TryParseOptionalFloat("abc", 1, out _));
        Assert.False(CaorenCupUtils.TryParseOptionalInt("abc", 1, out _));
    }
}
