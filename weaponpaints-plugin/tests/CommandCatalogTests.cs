using CaorenCup.WeaponPaints.Menus;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void PlayerAliases_KeepApprovedLegacyCommandsAndExcludeKill()
    {
        Assert.Contains("skin", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("wp", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("ws", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("knife", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("gloves", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("agents", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("music", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("pin", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("pins", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("coin", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("coins", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("stattrak", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.Contains("st", SkinCommandCatalog.PlayerCommands.Keys);
        Assert.DoesNotContain("kill", SkinCommandCatalog.PlayerCommands.Keys);
    }
}
