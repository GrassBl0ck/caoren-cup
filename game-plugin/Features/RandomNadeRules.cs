using System.Globalization;

namespace CaorenCup.Features;

public enum RandomNadeType
{
    Smoke,
    Fire,
    HighExplosive,
    Flash,
    Decoy
}

public readonly record struct RandomNadeWeights(
    double Smoke,
    double Fire,
    double HighExplosive,
    double Flash,
    double Decoy)
{
    public static RandomNadeWeights Default => new(8, 40, 40, 2, 10);

    public double Total => Smoke + Fire + HighExplosive + Flash + Decoy;
}

public readonly record struct RandomNadeCommandState(
    bool Enabled,
    double TotalChance,
    RandomNadeWeights Weights)
{
    public static RandomNadeCommandState Default => new(false, 100, RandomNadeWeights.Default);
}

public readonly record struct RandomNadeCommandResult(
    bool Success,
    RandomNadeCommandState State,
    string? Error = null);

public static class RandomNadeRules
{
    private const double Epsilon = 0.000001;

    public static RandomNadeWeights ResolveEffectiveWeights(RandomNadeWeights configured)
    {
        var total = configured.Total;
        if (total <= 100 + Epsilon)
        {
            var remainder = Math.Max(0, 100 - total);
            var defaults = RandomNadeWeights.Default;
            return new RandomNadeWeights(
                configured.Smoke + remainder * defaults.Smoke / 100,
                configured.Fire + remainder * defaults.Fire / 100,
                configured.HighExplosive + remainder * defaults.HighExplosive / 100,
                configured.Flash + remainder * defaults.Flash / 100,
                configured.Decoy + remainder * defaults.Decoy / 100);
        }

        var remaining = 100d;
        var fire = Take(configured.Fire, ref remaining);
        var highExplosive = Take(configured.HighExplosive, ref remaining);
        var flash = Take(configured.Flash, ref remaining);
        var smoke = Take(configured.Smoke, ref remaining);
        var decoy = Take(configured.Decoy, ref remaining);
        return new RandomNadeWeights(smoke, fire, highExplosive, flash, decoy);
    }

    public static RandomNadeCommandResult ApplyArguments(
        RandomNadeCommandState current,
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count is < 1 or > 6)
        {
            return new RandomNadeCommandResult(false, current, "参数数量无效");
        }

        var values = new double[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!double.TryParse(
                    arguments[index],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]) ||
                !double.IsFinite(values[index]) ||
                values[index] < 0 ||
                values[index] > 100)
            {
                return new RandomNadeCommandResult(false, current, $"第 {index + 1} 个概率必须在 0～100 之间");
            }
        }

        if (values[0] == 0)
        {
            return new RandomNadeCommandResult(true, current with { Enabled = false });
        }

        var weights = current.Weights;
        if (values.Length >= 2) weights = weights with { Smoke = values[1] };
        if (values.Length >= 3) weights = weights with { Fire = values[2] };
        if (values.Length >= 4) weights = weights with { HighExplosive = values[3] };
        if (values.Length >= 5) weights = weights with { Flash = values[4] };
        if (values.Length >= 6) weights = weights with { Decoy = values[5] };

        return new RandomNadeCommandResult(
            true,
            new RandomNadeCommandState(true, values[0], weights));
    }

    public static RandomNadeType Select(RandomNadeWeights configured, double roll)
    {
        if (roll < 0 || roll >= 100)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), "随机值必须在 [0, 100) 范围内");
        }

        var effective = ResolveEffectiveWeights(configured);
        var cursor = effective.Smoke;
        if (roll < cursor) return RandomNadeType.Smoke;

        cursor += effective.Fire;
        if (roll < cursor) return RandomNadeType.Fire;

        cursor += effective.HighExplosive;
        if (roll < cursor) return RandomNadeType.HighExplosive;

        cursor += effective.Flash;
        if (roll < cursor) return RandomNadeType.Flash;

        return RandomNadeType.Decoy;
    }

    private static double Take(double requested, ref double remaining)
    {
        var accepted = Math.Min(Math.Max(0, requested), remaining);
        remaining -= accepted;
        return accepted;
    }
}
