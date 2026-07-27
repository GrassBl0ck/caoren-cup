using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCupPlugin;

public sealed class DuelServerCvarScope
{
    private readonly Dictionary<string, string> _restore = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string, string> _readCurrentValue;
    private readonly Action<string> _executeCommand;

    public DuelServerCvarScope()
        : this(ReadCurrentValue, Server.ExecuteCommand)
    {
    }

    internal DuelServerCvarScope(
        Func<string, string, string> readCurrentValue,
        Action<string> executeCommand)
    {
        _readCurrentValue = readCurrentValue ?? throw new ArgumentNullException(nameof(readCurrentValue));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
    }

    public int PendingRestoreCount => _restore.Count;

    public IReadOnlyCollection<string> PendingRestoreNames => _restore.Keys.ToArray();

    public bool IsReadyForNewDuel => _restore.Count == 0;

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
            _restore[name] = _readCurrentValue(name, normalizedFallback);
        }

        _executeCommand($"{name} {normalizedValue}");
    }

    public void RestoreAll()
    {
        List<Exception>? failures = null;
        foreach (var item in _restore.ToArray())
        {
            if (!TryNormalizeSafeValue(item.Value, out var normalizedValue))
            {
                failures ??= [];
                failures.Add(new InvalidOperationException($"Refused to restore unsafe cvar value for {item.Key}."));
                continue;
            }

            try
            {
                _executeCommand($"{item.Key} {normalizedValue}");
                _restore.Remove(item.Key);
            }
            catch (Exception ex)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException($"Failed to restore cvar {item.Key}.", ex));
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("Failed to restore one or more duel cvars.", failures);
        }
    }

    public bool TryRestoreAll(int maxAttempts, Action<AggregateException>? onFailure = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Restore attempts must be at least one.");
        }

        for (var attempt = 0; attempt < maxAttempts && _restore.Count > 0; attempt++)
        {
            try
            {
                RestoreAll();
            }
            catch (AggregateException ex)
            {
                onFailure?.Invoke(ex);
            }
        }

        return _restore.Count == 0;
    }

    public bool RetryPendingAtSafePoint(Action<AggregateException>? onFailure = null) =>
        TryRestoreAll(1, onFailure);

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
