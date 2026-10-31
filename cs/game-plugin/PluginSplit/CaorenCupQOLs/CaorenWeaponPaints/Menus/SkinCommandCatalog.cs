namespace CaorenCup.WeaponPaints.Menus;

public enum SkinCommandTarget
{
    Main,
    Refresh,
    Weapons,
    Knife,
    Gloves,
    Agents,
    Music,
    Pins,
    StatTrak
}

public static class SkinCommandCatalog
{
    public static readonly IReadOnlyDictionary<string, SkinCommandTarget> PlayerCommands =
        new Dictionary<string, SkinCommandTarget>(StringComparer.OrdinalIgnoreCase)
        {
            ["skin"] = SkinCommandTarget.Main,
            ["ws"] = SkinCommandTarget.Main,
            ["wp"] = SkinCommandTarget.Refresh,
            ["skins"] = SkinCommandTarget.Weapons,
            ["knife"] = SkinCommandTarget.Knife,
            ["gloves"] = SkinCommandTarget.Gloves,
            ["agents"] = SkinCommandTarget.Agents,
            ["music"] = SkinCommandTarget.Music,
            ["pin"] = SkinCommandTarget.Pins,
            ["pins"] = SkinCommandTarget.Pins,
            ["coin"] = SkinCommandTarget.Pins,
            ["coins"] = SkinCommandTarget.Pins,
            ["stattrak"] = SkinCommandTarget.StatTrak,
            ["st"] = SkinCommandTarget.StatTrak
        };
}
