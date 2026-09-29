using CaorenCup.Gamemodes.Competitive;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CompetitiveModeTests
{
    [Fact]
    public void CompetitiveRestoresCaorenRulesAfterLeavingMatchZyPractice()
    {
        Assert.Equal(
            ["css_reset_plu", "css_exitprac", "exec grass/default"],
            CompetitiveModePlan.StartCommands);
    }
}
