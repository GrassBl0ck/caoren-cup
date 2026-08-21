using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityRuntimeControllerTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Hot_reload_restores_state_before_duplicate_confirmed_config_is_applied()
    {
        var path = RuntimePath();
        var catalog = Catalog();
        var original = new AbilityMatchRuntime(catalog);
        original.ApplyConfirmed(Config());
        original.Start(Identity());
        original.BeginRound("round-1");
        original.Charges.Add(original.State!.Seats["A1"], 6, ChargeReason.Test);
        original.State.Seats["A1"].MarkAbilityUsed();
        await using (var writer = new RuntimePersistence(path, catalog))
            await writer.SaveNowAsync(original.State);

        await using var persistence = new RuntimePersistence(path, catalog);
        var controller = new AbilityRuntimeController(catalog, persistence);
        var restored = await controller.InitializeFromConfirmedAsync(Config());
        var duplicate = await controller.InitializeFromConfirmedAsync(Config() with { SyncId = "retry" });

        Assert.True(restored.Restored);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(AbilityRuntimeLifecycle.Armed, controller.Runtime.State!.Lifecycle);
        Assert.False(controller.Hud.IsRunning);
        Assert.Equal(6, controller.Runtime.State!.Seats["A1"].Charge);
        Assert.True(controller.Runtime.State.Seats["A1"].AbilityUsedThisRound);
    }

    [Fact]
    public async Task Start_requires_matching_identity_and_starts_hud_at_supplied_round()
    {
        await using var persistence = new RuntimePersistence(RuntimePath(), Catalog());
        var controller = new AbilityRuntimeController(Catalog(), persistence);
        await controller.InitializeFromConfirmedAsync(Config());

        Assert.False((await controller.HandleAsync(Envelope("start") with { Revision = 99 })).Ok);
        Assert.True((await controller.HandleAsync(Envelope("start") with { RoundKey = "round-3" })).Ok);
        Assert.Equal(AbilityRuntimeLifecycle.Running, controller.Runtime.State!.Lifecycle);
        Assert.Equal("round-3", controller.Runtime.State.CurrentRoundKey);
        Assert.True(controller.Hud.IsRunning);
    }

    [Fact]
    public async Task Stop_clears_runtime_hud_movement_and_persisted_file()
    {
        var path = RuntimePath();
        await using var persistence = new RuntimePersistence(path, Catalog());
        var controller = new AbilityRuntimeController(Catalog(), persistence);
        await controller.InitializeFromConfirmedAsync(Config());
        await controller.HandleAsync(Envelope("start"));
        controller.Movement.Set("A1", "base", 0.9);

        Assert.True((await controller.HandleAsync(Envelope("stop"))).Ok);

        Assert.Null(controller.Runtime.State);
        Assert.False(controller.Hud.IsRunning);
        Assert.Equal(1, controller.Movement.GetCombined("A1"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Substitute_and_non_scoring_round_control_are_identity_checked_and_persisted()
    {
        await using var persistence = new RuntimePersistence(RuntimePath(), Catalog());
        var controller = new AbilityRuntimeController(Catalog(), persistence);
        await controller.InitializeFromConfirmedAsync(Config());
        await controller.HandleAsync(Envelope("start"));

        var substitute = await controller.HandleAsync(Envelope("substitute") with
        {
            SeatId = "A1",
            SteamId = "76561198000000999",
        });
        var control = await controller.HandleAsync(Envelope("round_control") with
        {
            RoundKey = "round-1",
            Outcome = "admin_cancelled",
        });
        await controller.OnRoundStartAsync("round-2");

        Assert.True(substitute.Ok);
        Assert.True(control.Ok);
        Assert.Equal("76561198000000999", controller.Runtime.State!.Seats["A1"].CurrentSteamId);
        Assert.All(controller.Runtime.State.Seats.Values, seat => Assert.Equal(0, seat.Charge));
    }

    [Fact]
    public async Task Confirmed_disabled_config_is_accepted_without_activating_runtime()
    {
        await using var persistence = new RuntimePersistence(RuntimePath(), Catalog());
        var controller = new AbilityRuntimeController(Catalog(), persistence);

        var result = await controller.InitializeFromConfirmedAsync(Config() with { AbilityModeEnabled = false });

        Assert.True(result.Ok);
        Assert.Null(controller.Runtime.State);
        Assert.False(controller.Hud.IsRunning);
    }

    private static string RuntimePath()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "runtime-controller-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "ability-runtime-state.json");
    }

    private static AbilityRuntimeEnvelope Envelope(string action) => new(
        action,
        "match-1",
        1,
        Digest,
        "round-1");

    private static AbilityRuntimeIdentity Identity() => new("match-1", 1, Digest);

    private static AbilityDefinitionCatalog Catalog() => AbilityDefinitionCatalog.CreateForRuntime([
        new("medic", "医师", 12, AbilityReleaseModel.A),
        new("tank", "坦克", 12, AbilityReleaseModel.B, FixedCost: 8),
    ]);

    private static AbilitySyncConfig Config() => new()
    {
        ProtocolVersion = 1,
        SyncId = "sync-1",
        MatchId = "match-1",
        Revision = 1,
        CatalogVersion = "ability-catalog-v1",
        AbilityModeEnabled = true,
        ContentDigest = Digest,
        Seats =
        [
            new() { PlayerId = "A1", SteamId = "76561198000000001", RosterTeam = "A", InitialSide = "CT", AbilityId = "medic" },
            new() { PlayerId = "B1", SteamId = "76561198000000002", RosterTeam = "B", InitialSide = "T", AbilityId = "tank" },
        ],
    };
}
