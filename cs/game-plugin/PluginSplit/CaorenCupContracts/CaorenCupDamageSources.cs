namespace CaorenCup.Contracts;

public static class CaorenCupDamageSources
{
    private static readonly HashSet<string> Guns = new(StringComparer.Ordinal)
    {
        "ak47", "aug", "awp", "bizon", "cz75a", "deagle", "elite", "famas", "fiveseven", "g3sg1",
        "galilar", "glock", "hkp2000", "m249", "m4a1", "m4a1_silencer", "mac10", "mag7", "mp5sd",
        "mp7", "mp9", "negev", "nova", "p250", "p90", "revolver", "sawedoff", "scar20", "sg556",
        "ssg08", "tec9", "ump45", "usp_silencer", "xm1014"
    };

    public static bool IsGun(string? weapon) => weapon is not null &&
        Guns.Contains(weapon.StartsWith("weapon_", StringComparison.Ordinal) ? weapon[7..] : weapon);
}
