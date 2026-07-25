using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCupPlugin;

public sealed class DuelServerCvarScope
{
    private readonly Dictionary<string, string> _restore = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value, string fallback)
    {
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
            return ConVar.Find(name)?.GetPrimitiveValue<string>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
