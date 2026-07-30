using CaorenCup.WeaponPaints.Core;

namespace CaorenCup.WeaponPaints.Persistence;

public sealed class LoadoutReloadService
{
    private readonly ILoadoutRepository _repository;
    private readonly PlayerLoadoutCache _cache;

    public LoadoutReloadService(ILoadoutRepository repository, PlayerLoadoutCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<PlayerLoadout> ReloadAsync(string steamId, CancellationToken cancellationToken = default)
    {
        var loadout = await _repository.LoadAsync(steamId, cancellationToken).ConfigureAwait(false);
        _cache.Set(loadout);
        return loadout;
    }
}
