using CaorenCup.Gamemodes.Competitive;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CompetitiveModeTests
{
    [Fact]
    public void CompetitiveDoesNotInvokeMatchZyForFormalMode()
    {
        Assert.Equal(
            ["css_reset_plu"],
            CompetitiveModePlan.StartCommands);
    }
}
