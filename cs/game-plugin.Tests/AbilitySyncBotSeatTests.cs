using System.Collections.Generic;
using Caoren;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

/// <summary>同步校验需要接受 bot 席位键，否则带 bot 的阵容无法启动异能模式。</summary>
public sealed class AbilitySyncBotSeatTests
{
    private static AbilitySyncConfig BuildConfig(TwoSeatLayout layout)
    {
        var config = new AbilitySyncConfig
        {
            ProtocolVersion = 1,
            SyncId = "sync-1",
            MatchId = "de_ancient-20261022",
            Revision = 1,
            CatalogVersion = "1.12.0",
            AbilityModeEnabled = true,
        };
        config.Seats.Add(new AbilitySyncSeat
        {
            PlayerId = layout.PlayerId,
            SteamId = layout.SteamId,
            RosterTeam = "A",
            InitialSide = "CT",
            AbilityId = "medic",
        });
        config.ContentDigest = AbilitySyncDigest.Compute(config);
        return config;
    }

    private static AbilitySyncValidationContext BuildContext(string playerId, string seatKey) => new()
    {
        ExpectedMatchId = "de_ancient-20261022",
        ExpectedRevision = 1,
        ExpectedCatalogVersion = "1.12.0",
        ExpectedSeatCount = 1,
        KnownAbilityIds = new HashSet<string>(System.StringComparer.Ordinal) { "medic" },
        ExpectedSeats = new Dictionary<string, AbilitySyncExpectedSeat>(System.StringComparer.Ordinal)
        {
            [playerId] = new(playerId, seatKey, "A", "CT"),
        },
    };

    public readonly record struct TwoSeatLayout(string PlayerId, string SteamId);

    [Fact]
    public void Bot_seat_key_passes_validation()
    {
        var layout = new TwoSeatLayout("bot:Alex", "bot:Alex");
        var result = AbilitySyncValidator.Validate(BuildConfig(layout), BuildContext(layout.PlayerId, layout.SteamId));
        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public void Human_seat_key_still_passes_validation()
    {
        var layout = new TwoSeatLayout("76561198000000001", "76561198000000001");
        var result = AbilitySyncValidator.Validate(BuildConfig(layout), BuildContext(layout.PlayerId, layout.SteamId));
        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public void Bot_key_without_name_is_rejected()
    {
        var layout = new TwoSeatLayout("bot:", "bot:");
        var result = AbilitySyncValidator.Validate(BuildConfig(layout), BuildContext(layout.PlayerId, layout.SteamId));
        Assert.False(result.Ok);
        Assert.Equal("INVALID_STEAM_ID", result.Code);
    }
}
