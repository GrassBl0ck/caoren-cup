using System.Globalization;

namespace CaorenCup.FunCommands;

public enum HeadSizeMode { Off, Automatic, Manual }

public sealed record HeadSizeCommand(HeadSizeMode Mode, double Strength = 1, string Target = "", float Scale = 1);

public static class HeadSizeRules
{
    public const string Usage = "用法：/hd 0 关闭；/hd 1 [强度] 自动（默认 1）；/hd 2 <玩家名片段|t|ct> <倍率> 手动。";

    public static float ScaleAt(long netKills, double strength)
    {
        if (!double.IsFinite(strength) || strength <= 0) throw new ArgumentOutOfRangeException(nameof(strength));
        if (netKills == 0) return 1;

        // 与 3 / (1 + 2 * 2^(-strength*x/5)) 等价，指数始终非正，极值不会溢出。
        var exponent = -strength * (netKills / 5d);
        var small = Math.Pow(2, -Math.Abs(exponent));
        var scale = exponent >= 0 ? 3 * small / (small + 2) : 3 / (1 + 2 * small);
        return Math.Clamp((float)scale, float.Epsilon, 3);
    }

    public static bool TryParse(IReadOnlyList<string> args, out HeadSizeCommand command, out string error)
    {
        command = new(HeadSizeMode.Off);
        error = Usage;
        if (args.Count == 0) return false;
        switch (args[0])
        {
            case "0" when args.Count == 1:
                break;
            case "1" when args.Count is 1 or 2:
                double strength = 1;
                if (args.Count == 2 && !TryPositiveNumber(args[1], out strength))
                {
                    error = "自动强度必须是有限正数，越大变化越快，例如 /hd 1 1 或 /hd 1 0.5。";
                    return false;
                }
                command = new(HeadSizeMode.Automatic, strength);
                break;
            case "2" when args.Count >= 3:
                if (!TryPositiveNumber(args[^1], out var scale) || scale > 20 || (float)scale <= 0)
                {
                    error = "手动倍率必须大于 0 且小于等于 20，允许小数，不能小到无法表示。例如 /hd 2 ct 1.5。";
                    return false;
                }
                var target = string.Join(" ", args.Skip(1).Take(args.Count - 2)).Trim();
                if (target.Length == 0)
                {
                    error = "请填写玩家名字片段、t 或 ct。";
                    return false;
                }
                command = new(HeadSizeMode.Manual, Target: target, Scale: (float)scale);
                break;
            default:
                return false;
        }
        error = "";
        return true;
    }

    private static bool TryPositiveNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value > 0;
}
