using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCupPlugin;

public sealed class DuelServerCvarScope
{
    private readonly Dictionary<string, string> _restore = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value, string fallback)
    {
        if (!IsSafeNumericToken(value))
        {
            throw new ArgumentException("Duel cvar values must be a single invariant numeric token.", nameof(value));
        }

        if (!IsSafeNumericToken(fallback))
        {
            throw new ArgumentException("Duel cvar fallbacks must be a single invariant numeric token.", nameof(fallback));
        }

        if (!_restore.ContainsKey(name))
        {
            _restore[name] = ReadCurrentValue(name, fallback);
        }

        Server.ExecuteCommand($"{name} {value}");
    }

    public void RestoreAll()
    {
        List<Exception>? failures = null;
        try
        {
            foreach (var item in _restore)
            {
                if (!IsSafeNumericToken(item.Value))
                {
                    failures ??= [];
                    failures.Add(new InvalidOperationException($"Refused to restore unsafe cvar value for {item.Key}."));
                    continue;
                }

                try
                {
                    Server.ExecuteCommand($"{item.Key} {item.Value}");
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
            return IsSafeNumericToken(current) ? current! : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static bool IsSafeNumericToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains(';') || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
               double.IsFinite(parsed);
    }
}
