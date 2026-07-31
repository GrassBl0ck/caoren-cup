using CaorenCup.WeaponPaints.Core;

namespace CaorenCup.WeaponPaints.Persistence;

public interface ILoadoutRepository
{
    Task<PlayerLoadout> LoadAsync(string steamId, CancellationToken cancellationToken = default);

    Task SaveWeaponAsync(
        string steamId,
        TeamSide team,
        WeaponSelection selection,
        CancellationToken cancellationToken = default);

    Task SaveCosmeticAsync(
        string steamId,
        TeamSide team,
        CosmeticKind kind,
        string itemKey,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
