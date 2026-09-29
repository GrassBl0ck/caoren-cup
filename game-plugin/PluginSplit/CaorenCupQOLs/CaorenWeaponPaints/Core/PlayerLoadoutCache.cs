using System.Collections.Concurrent;

namespace CaorenCup.WeaponPaints.Core;

public sealed class PlayerLoadoutCache
{
    private readonly ConcurrentDictionary<string, PlayerLoadout> _loadouts =
        new(StringComparer.Ordinal);

    public PlayerLoadout? Get(string steamId)
    {
        return _loadouts.TryGetValue(steamId, out var loadout) ? loadout : null;
    }

    public PlayerLoadout GetOrCreate(string steamId)
    {
        return _loadouts.GetOrAdd(steamId, id => new PlayerLoadout(id));
    }

    public void Set(PlayerLoadout loadout)
    {
        _loadouts[loadout.SteamId] = loadout;
    }

    public bool Remove(string steamId)
    {
        return _loadouts.TryRemove(steamId, out _);
    }

    public void Clear() => _loadouts.Clear();
}
