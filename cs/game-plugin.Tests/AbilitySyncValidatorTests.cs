using Caoren;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilitySyncValidatorTests
{
    private static AbilitySyncConfig ValidConfig() => new()
    {
        ProtocolVersion = 1,
        SyncId = "sync-1",
        MatchId = "match-1",
        Revision = 1,
        CatalogVersion = "ability-catalog-v1",
        AbilityModeEnabled = true,
        BannedAbilityIds = ["witch"],
        ContentDigest = "",
        Seats =
        [
            new() { PlayerId = "A1", SteamId = "76561198000000001", RosterTeam = "A", InitialSide = "CT", AbilityId = "medic" },
            new() { PlayerId = "A2", SteamId = "76561198000000002", RosterTeam = "A", InitialSide = "CT", AbilityId = "berserker" },
            new() { PlayerId = "B1", SteamId = "76561198000000003", RosterTeam = "B", InitialSide = "T", AbilityId = "medic" },
            new() { PlayerId = "B2", SteamId = "76561198000000004", RosterTeam = "B", InitialSide = "T", AbilityId = "tank" },
        ],
    };

    private static AbilitySyncValidationContext Context() => new()
    {
        ExpectedMatchId = "match-1",
        ExpectedRevision = 1,
        ExpectedCatalogVersion = "ability-catalog-v1",
        ExpectedSeatCount = 4,
        KnownAbilityIds = ["medic", "berserker", "tank", "istaru", "witch"],
    };

    [Fact]
    public void Accepts_valid_complete_configuration()
    {
        var config = ValidConfig();
        config.ContentDigest = AbilitySyncDigest.Compute(config);
        Assert.Equal("ac32f1ad66252e7db94ea1373461f14bc7aaadefc271415346acf6a8afbf9189", config.ContentDigest);

        var result = AbilitySyncValidator.Validate(config, Context());

        Assert.True(result.Ok);
    }

    [Fact]
    public void Rejects_duplicate_steam_id_as_a_whole_transaction()
    {
        var config = ValidConfig();
        config.Seats[1].SteamId = config.Seats[0].SteamId;
        config.ContentDigest = AbilitySyncDigest.Compute(config);

        var result = AbilitySyncValidator.Validate(config, Context());

        Assert.Equal("DUPLICATE_STEAM_ID", result.Code);
    }

    [Fact]
    public void Rejects_unknown_banned_duplicate_and_multiple_global_unique_abilities()
    {
        var unknown = ValidConfig();
        unknown.Seats[0].AbilityId = "unknown";
        unknown.ContentDigest = AbilitySyncDigest.Compute(unknown);
        Assert.Equal("UNKNOWN_ABILITY", AbilitySyncValidator.Validate(unknown, Context()).Code);

        var banned = ValidConfig();
        banned.Seats[0].AbilityId = "witch";
        banned.ContentDigest = AbilitySyncDigest.Compute(banned);
        Assert.Equal("BANNED_ABILITY", AbilitySyncValidator.Validate(banned, Context()).Code);

        var duplicateTeam = ValidConfig();
        duplicateTeam.Seats[1].AbilityId = duplicateTeam.Seats[0].AbilityId;
        duplicateTeam.ContentDigest = AbilitySyncDigest.Compute(duplicateTeam);
        Assert.Equal("DUPLICATE_TEAM_ABILITY", AbilitySyncValidator.Validate(duplicateTeam, Context()).Code);

        var istaru = ValidConfig();
        istaru.Seats[0].AbilityId = "istaru";
        istaru.Seats[2].AbilityId = "istaru";
        istaru.ContentDigest = AbilitySyncDigest.Compute(istaru);
        Assert.Equal("GLOBAL_UNIQUE_ABILITY", AbilitySyncValidator.Validate(istaru, Context()).Code);
    }

    [Fact]
    public void Same_sync_or_content_digest_is_idempotent()
    {
        var config = ValidConfig();
        config.ContentDigest = AbilitySyncDigest.Compute(config);
        var applier = new AbilitySyncApplier(Context());

        var first = applier.Apply(config);
        var sameId = applier.Apply(config with { });
        var sameDigest = applier.Apply(config with { SyncId = "sync-2" });

        Assert.True(first.Ok);
        Assert.True(first.Applied);
        Assert.True(sameId.Ok);
        Assert.False(sameId.Applied);
        Assert.True(sameDigest.Ok);
        Assert.False(sameDigest.Applied);
        Assert.Equal("sync-1", applier.Current?.SyncId);
    }
}
