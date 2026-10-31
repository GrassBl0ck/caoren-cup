using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCup.Features;

public sealed class ManagedCvarScope
{
    private readonly Dictionary<string, ManagedCvar> _managed = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _managed.Count;

    public void Set(string owner, string name, string value, string fallbackDefault)
    {
        if (!_managed.ContainsKey(name))
        {
            string restoreValue = ReadCurrentValue(name) ?? fallbackDefault;
            _managed[name] = new ManagedCvar(name, restoreValue, owner);
        }

        Server.ExecuteCommand($"{name} {value}");
    }

    public void ResetOwner(string owner)
    {
        foreach (ManagedCvar item in _managed.Values
                     .Where(item => item.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            Server.ExecuteCommand($"{item.Name} {item.RestoreValue}");
            _managed.Remove(item.Name);
        }
    }

    public void ResetAll()
    {
        foreach (ManagedCvar item in _managed.Values.ToList())
        {
            Server.ExecuteCommand($"{item.Name} {item.RestoreValue}");
        }

        _managed.Clear();
    }

    private static string? ReadCurrentValue(string name)
    {
        try
        {
            ConVar? cvar = ConVar.Find(name);
            return cvar?.GetPrimitiveValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private sealed record ManagedCvar(string Name, string RestoreValue, string Owner);
}
