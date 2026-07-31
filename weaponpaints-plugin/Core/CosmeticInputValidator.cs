using System.Globalization;

namespace CaorenCup.WeaponPaints.Core;

public static class CosmeticInputValidator
{
    public const int MaximumNameTagLength = 128;

    public static bool TryParseWear(string input, out float wear, out string error)
    {
        if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out wear) ||
            float.IsNaN(wear) || float.IsInfinity(wear) || wear is < 0 or > 1)
        {
            wear = 0;
            error = "磨损值必须是 0 到 1 之间的数字，例如 0.12。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryParseSeed(string input, out uint seed, out string error)
    {
        if (!uint.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out seed) || seed > 1000)
        {
            seed = 0;
            error = "Seed 必须是 0 到 1000 之间的整数。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateNameTag(string input, out string nameTag, out string error)
    {
        nameTag = input.Trim();
        if (nameTag.Length > MaximumNameTagLength)
        {
            error = $"名称标签不能超过 {MaximumNameTagLength} 个字符。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
