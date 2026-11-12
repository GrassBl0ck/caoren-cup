using System.Text.RegularExpressions;

namespace CaorenCupPlugin.MatchRuntime;

public sealed record MatchCvarSetting(string Name, string Value, string Fallback);

public sealed class MatchServerCvarScope
{
    private static readonly Regex SafeName = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly Dictionary<string, string> _restore = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string, string> _readCurrentValue;
    private readonly Action<string> _executeCommand;

    public MatchServerCvarScope(Func<string, string, string> readCurrentValue, Action<string> executeCommand)
    {
        _readCurrentValue = readCurrentValue ?? throw new ArgumentNullException(nameof(readCurrentValue));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
    }

    public bool IsReadyForNewMatch => _restore.Count == 0;

    public IReadOnlyCollection<string> PendingRestoreNames => _restore.Keys.ToArray();

    public void Apply(IEnumerable<MatchCvarSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var setting in settings) Set(setting.Name, setting.Value, setting.Fallback);
    }

    public void Set(string name, string value, string fallback)
    {
        if (!SafeName.IsMatch(name)) throw new ArgumentException("Match cvar name is unsafe.", nameof(name));
        if (!global::CaorenCupPlugin.MatchRuntime.RuntimeCvarValue.TryNormalizeSafeValue(value, out var normalizedValue))
            throw new ArgumentException("Match cvar value is unsafe.", nameof(value));
        if (!global::CaorenCupPlugin.MatchRuntime.RuntimeCvarValue.TryNormalizeSafeValue(fallback, out var normalizedFallback))
            throw new ArgumentException("Match cvar fallback is unsafe.", nameof(fallback));

        if (!_restore.ContainsKey(name))
        {
            var current = _readCurrentValue(name, normalizedFallback);
            _restore[name] = global::CaorenCupPlugin.MatchRuntime.RuntimeCvarValue.TryNormalizeSafeValue(current, out var normalizedCurrent)
                ? normalizedCurrent
                : normalizedFallback;
        }
        _executeCommand($"{name} {normalizedValue}");
    }

    public void RestoreAll()
    {
        List<Exception>? failures = null;
        foreach (var item in _restore.ToArray())
        {
            try
            {
                _executeCommand($"{item.Key} {item.Value}");
                _restore.Remove(item.Key);
            }
            catch (Exception ex)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException($"Failed to restore match cvar {item.Key}.", ex));
            }
        }
        if (failures is { Count: > 0 }) throw new AggregateException("Failed to restore match cvars.", failures);
    }

    public bool TryRestoreAll(int maxAttempts)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        for (var attempt = 0; attempt < maxAttempts && _restore.Count > 0; attempt++)
        {
            try
            {
                RestoreAll();
            }
            catch (AggregateException)
            {
                // Pending entries remain available for the next safe retry.
            }
        }
        return _restore.Count == 0;
    }
}
