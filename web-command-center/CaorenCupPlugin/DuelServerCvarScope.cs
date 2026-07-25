using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCupPlugin;

public sealed class DuelServerCvarScope
{
    private readonly Dictionary<string, string> _restore = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value, string fallback)
    {
        if (!TryNormalizeSafeValue(value, out var normalizedValue))
        {
            throw new ArgumentException("Duel cvar values must be a single invariant numeric or boolean token.", nameof(value));
        }

        if (!TryNormalizeSafeValue(fallback, out var normalizedFallback))
        {
            throw new ArgumentException("Duel cvar fallbacks must be a single invariant numeric or boolean token.", nameof(fallback));
        }

        if (!_restore.ContainsKey(name))
        {
            _restore[name] = ReadCurrentValue(name, normalizedFallback);
        }

        Server.ExecuteCommand($"{name} {normalizedValue}");
    }

    public void RestoreAll()
    {
        List<Exception>? failures = null;
        try
        {
            foreach (var item in _restore)
            {
                if (!TryNormalizeSafeValue(item.Value, out var normalizedValue))
                {
                    failures ??= [];
                    failures.Add(new InvalidOperationException($"Refused to restore unsafe cvar value for {item.Key}."));
                    continue;
                }

                try
                {
                    Server.ExecuteCommand($"{item.Key} {normalizedValue}");
                }
                catch (Exception ex)
                {
                    failures ??= [];
                    failures.Add(new InvalidOperationException($"Failed to restore cvar {item.Key}.", ex));
                }
            }
        }
        finally
        {
            _restore.Clear();
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("Failed to restore one or more duel cvars.", failures);
        }
    }

    private static string ReadCurrentValue(string name, string fallback)
    {
        try
        {
            var current = ConVar.Find(name)?.StringValue;
            return TryNormalizeSafeValue(current, out var normalized) ? normalized : fallback;
        }
        catch
        {
            return fallback;
        }
    }

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
