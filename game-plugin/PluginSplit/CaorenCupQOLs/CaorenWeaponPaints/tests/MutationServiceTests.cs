using CaorenCup.WeaponPaints.Core;
using CaorenCup.WeaponPaints.Persistence;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class MutationServiceTests
{
    [Fact]
    public async Task SaveWeapon_UpdatesCacheOnlyAfterDatabaseSuccess()
    {
        var repository = new FakeRepository();
        var cache = new PlayerLoadoutCache();
        var service = new LoadoutMutationService(repository, cache);
        var selection = new WeaponSelection(7, 801);

        await service.SaveWeaponAsync("76561198000000001", TeamSide.Terrorist, selection);

        Assert.Equal((uint)801, cache.Get("76561198000000001")!.GetWeapon(TeamSide.Terrorist, 7)!.PaintId);
    }

    [Fact]
    public async Task SaveWeapon_DoesNotChangeCacheWhenDatabaseFails()
    {
        var repository = new FakeRepository { Failure = new InvalidOperationException("database offline") };
        var cache = new PlayerLoadoutCache();
        var existing = new PlayerLoadout("76561198000000001");
        existing.SetWeapon(TeamSide.Terrorist, new WeaponSelection(7, 302));
        cache.Set(existing);
        var service = new LoadoutMutationService(repository, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveWeaponAsync("76561198000000001", TeamSide.Terrorist, new WeaponSelection(7, 801)));

        Assert.Equal((uint)302, cache.Get("76561198000000001")!.GetWeapon(TeamSide.Terrorist, 7)!.PaintId);
    }

    [Fact]
    public async Task Reload_ReplacesCachedLoadoutWithLatestDatabaseState()
    {
        var repository = new FakeRepository { LoadedPaintId = 801 };
        var cache = new PlayerLoadoutCache();
        var old = new PlayerLoadout("76561198000000001");
        old.SetWeapon(TeamSide.Terrorist, new WeaponSelection(7, 302));
        cache.Set(old);
        var service = new LoadoutReloadService(repository, cache);

        await service.ReloadAsync("76561198000000001");

        Assert.Equal((uint)801, cache.Get("76561198000000001")!.GetWeapon(TeamSide.Terrorist, 7)!.PaintId);
    }

    private sealed class FakeRepository : ILoadoutRepository
    {
        public Exception? Failure { get; init; }
        public uint LoadedPaintId { get; init; }

        public Task SaveWeaponAsync(string steamId, TeamSide team, WeaponSelection selection, CancellationToken cancellationToken = default)
        {
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task<PlayerLoadout> LoadAsync(string steamId, CancellationToken cancellationToken = default)
        {
            var loadout = new PlayerLoadout(steamId);
            if (LoadedPaintId != 0)
            {
                loadout.SetWeapon(TeamSide.Terrorist, new WeaponSelection(7, LoadedPaintId));
            }
            return Task.FromResult(loadout);
        }
    }
}
