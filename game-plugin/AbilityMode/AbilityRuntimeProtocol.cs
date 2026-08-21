using System.Text.Json.Serialization;

namespace Caoren.AbilityMode;

public sealed record AbilityRuntimeEnvelope(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("matchId")] string MatchId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("contentDigest")] string ContentDigest,
    [property: JsonPropertyName("roundKey")] string? RoundKey = null,
    [property: JsonPropertyName("seatId")] string? SeatId = null,
    [property: JsonPropertyName("steamId")] string? SteamId = null,
    [property: JsonPropertyName("outcome")] string? Outcome = null)
{
    public AbilityRuntimeIdentity Identity => new(MatchId, Revision, ContentDigest);
}
