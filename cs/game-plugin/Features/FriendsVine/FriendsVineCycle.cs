using System.Globalization;

namespace CaorenCup.FunCommands;

public sealed record FriendsVineCycle(float Min, float Max, double PeriodSeconds)
{
    public static bool TryParse(string minText, string maxText, string periodText,
        out FriendsVineCycle? cycle)
    {
        cycle = null;
        if (!float.TryParse(minText, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) ||
            !float.TryParse(maxText, NumberStyles.Float, CultureInfo.InvariantCulture, out var max) ||
            !double.TryParse(periodText, NumberStyles.Float, CultureInfo.InvariantCulture, out var period) ||
            !float.IsFinite(min) || !float.IsFinite(max) || !double.IsFinite(period) ||
            min <= 0 || max < min || period <= 0)
            return false;

        cycle = new(min, max, period);
        return true;
    }

    public float MultiplierAt(double elapsedSeconds)
    {
        // 余弦使两端平滑转向；先取余数，避免长时间运行后相位损失。
        var phase = Math.Max(0, elapsedSeconds) % PeriodSeconds / PeriodSeconds;
        var blend = (1 - Math.Cos(phase * Math.Tau)) / 2;
        return (float)(Min + ((double)Max - Min) * blend);
    }
}
