namespace CaorenCup.WeaponPaints;

public static class WeaponPaintsPaths
{
    public static string GlobalGameDataPath(string moduleDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(moduleDirectory));
        while (directory != null)
        {
            if (directory.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase)
                && directory.Parent != null)
            {
                return Path.Combine(directory.Parent.FullName, "gamedata", "weaponpaints.json");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Cannot locate CounterStrikeSharp plugins directory from {moduleDirectory}");
    }
}
