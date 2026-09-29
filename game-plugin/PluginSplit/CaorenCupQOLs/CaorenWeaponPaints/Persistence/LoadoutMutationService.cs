using CaorenCup.WeaponPaints.Core;

namespace CaorenCup.WeaponPaints.Persistence;

public sealed class LoadoutMutationService
{
    private readonly ILoadoutRepository _repository;
    private readonly PlayerLoadoutCache _cache;

    public LoadoutMutationService(ILoadoutRepository repository, PlayerLoadoutCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task SaveWeaponAsync(
        string steamId,
        TeamSide team,
        WeaponSelection selection,
        CancellationToken cancellationToken = default)
    {
        await _repository.SaveWeaponAsync(steamId, team, selection, cancellationToken).ConfigureAwait(false);

        var loadout = _cache.GetOrCreate(steamId);
        lock (loadout)
        {
            loadout.SetWeapon(team, selection);
        }
    }

    public async Task SaveCosmeticAsync(
        string steamId,
        TeamSide team,
        CosmeticKind kind,
        string itemKey,
        CancellationToken cancellationToken = default)
    {
        await _repository.SaveCosmeticAsync(steamId, team, kind, itemKey, cancellationToken).ConfigureAwait(false);

        var loadout = _cache.GetOrCreate(steamId);
        lock (loadout)
        {
            loadout.SetCosmetic(team, kind, itemKey);
        }
    }
}
