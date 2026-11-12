using System.Globalization;
namespace CaorenCupPlugin.MatchRuntime;
internal static class RuntimeCvarValue {
    public static bool TryNormalizeSafeValue(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(raw) || raw.Contains(';') || raw.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "1";
            return true;
        }

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "0";
            return true;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            return false;
        }

        normalized = raw;
        return true;
    }
}
