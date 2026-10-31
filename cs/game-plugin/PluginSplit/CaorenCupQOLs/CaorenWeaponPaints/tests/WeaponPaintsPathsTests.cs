using CaorenCup.WeaponPaints;
using Xunit;

namespace CaorenWeaponPaints.Tests;

public class WeaponPaintsPathsTests
{
    [Fact]
    public void ResolvesGlobalGameDataFromNestedQolDirectory()
    {
        var css = Path.Combine(Path.GetTempPath(), "caorencup-test", "counterstrikesharp");
        var module = Path.Combine(css, "plugins", "CaorenCup", "CaorenCupQOLs", "CaorenWeaponPaints");

        Assert.Equal(
            Path.Combine(css, "gamedata", "weaponpaints.json"),
            WeaponPaintsPaths.GlobalGameDataPath(module));
    }

    [Fact]
    public void ResolvesGlobalGameDataFromOriginalTopLevelDirectory()
    {
        var css = Path.Combine(Path.GetTempPath(), "caorencup-test", "counterstrikesharp");
        var module = Path.Combine(css, "plugins", "CaorenWeaponPaints");

        Assert.Equal(
            Path.Combine(css, "gamedata", "weaponpaints.json"),
            WeaponPaintsPaths.GlobalGameDataPath(module));
    }
}
