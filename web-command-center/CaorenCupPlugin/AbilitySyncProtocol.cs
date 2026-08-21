using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaorenCupPlugin;

public sealed class AbilitySyncProtocolException(string message) : Exception(message);

public sealed record AbilitySyncInternalCommand(string Name, string Argument);

public static class AbilitySyncProtocol
{
    public const int ProtocolVersion = 1;
    public const string WebCommandType = "ABILITY_SYNC";
    public const string BeginCommandName = "css_ability_sync_begin";
    public const string SeatCommandName = "css_ability_sync_seat";
    public const string CommitCommandName = "css_ability_sync_commit";
    public const string ResetCommandName = "css_ability_sync_reset";
    public const string ClearCommandName = "css_ability_sync_clear";
    public const string AckCommandName = "css_ability_sync_ack";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<AbilitySyncInternalCommand> BuildInternalCommands(PluginCommand command)
    {
        if (!string.Equals(command.Type, WebCommandType, StringComparison.OrdinalIgnoreCase) ||
            command.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new AbilitySyncProtocolException("ABILITY_SYNC command payload is not a structured object.");
        }

        var config = JsonSerializer.Deserialize<AbilitySyncWebConfig>(command.Payload.GetRawText(), JsonOptions)
            ?? throw new AbilitySyncProtocolException("ABILITY_SYNC command payload is empty.");
        ValidateConfig(config);

        var commands = new List<AbilitySyncInternalCommand>
        {
            new(BeginCommandName, Encode(new AbilitySyncBeginEnvelope(
                config.ProtocolVersion,
                config.SyncId,
                config.MatchId,
                config.Revision,
                config.CatalogVersion,
                config.AbilityModeEnabled,
                config.BannedAbilityIds,
                config.Seats.Count,
                config.ContentDigest))),
        };
        commands.AddRange(config.Seats.Select(seat => new AbilitySyncInternalCommand(
            SeatCommandName,
            Encode(new AbilitySyncSeatEnvelope(
                config.SyncId,
                seat.PlayerId,
                seat.SteamId,
                seat.RosterTeam,
                seat.InitialSide,
                seat.AbilityId)))));
        commands.Add(new AbilitySyncInternalCommand(
            CommitCommandName,
            Encode(new AbilitySyncCommitEnvelope(config.SyncId, config.ContentDigest))));
        return commands;
    }

    public static AbilitySyncInternalCommand BuildResetCommand(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId)) throw new AbilitySyncProtocolException("Match ID is required to reset ability sync state.");
        return new AbilitySyncInternalCommand(ResetCommandName, Encode(new AbilitySyncResetEnvelope(matchId)));
    }

    public static string Encode<T>(T value) => Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    public static T Decode<T>(string encoded)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(Base64Url.Decode(encoded), JsonOptions)
                ?? throw new AbilitySyncProtocolException("Internal ability sync payload is empty.");
        }
        catch (AbilitySyncProtocolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AbilitySyncProtocolException($"Internal ability sync payload is invalid: {ex.Message}");
        }
    }

    private static void ValidateConfig(AbilitySyncWebConfig config)
    {
        if (config.ProtocolVersion != ProtocolVersion || string.IsNullOrWhiteSpace(config.SyncId)
            || string.IsNullOrWhiteSpace(config.MatchId) || config.Revision <= 0
            || string.IsNullOrWhiteSpace(config.CatalogVersion)
            || !System.Text.RegularExpressions.Regex.IsMatch(config.ContentDigest ?? string.Empty, "^[a-f0-9]{64}$")
            || config.Seats.Count == 0)
        {
            throw new AbilitySyncProtocolException("ABILITY_SYNC command metadata is incomplete.");
        }

        if (config.Seats.Any(seat => string.IsNullOrWhiteSpace(seat.PlayerId)
            || !System.Text.RegularExpressions.Regex.IsMatch(seat.SteamId ?? string.Empty, "^7656119[0-9]{10}$")
            || seat.RosterTeam is not ("A" or "B")
            || seat.InitialSide is not ("CT" or "T")
            || string.IsNullOrWhiteSpace(seat.AbilityId)))
        {
            throw new AbilitySyncProtocolException("ABILITY_SYNC contains an invalid seat.");
        }
    }

    public sealed record AbilitySyncWebConfig(
        [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
        [property: JsonPropertyName("syncId")] string SyncId,
        [property: JsonPropertyName("matchId")] string MatchId,
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("catalogVersion")] string CatalogVersion,
        [property: JsonPropertyName("abilityModeEnabled")] bool AbilityModeEnabled,
        [property: JsonPropertyName("bannedAbilityIds")] List<string> BannedAbilityIds,
        [property: JsonPropertyName("seats")] List<AbilitySyncWebSeat> Seats,
        [property: JsonPropertyName("contentDigest")] string ContentDigest);

    public sealed record AbilitySyncWebSeat(
        [property: JsonPropertyName("playerId")] string PlayerId,
        [property: JsonPropertyName("steamId")] string SteamId,
        [property: JsonPropertyName("rosterTeam")] string RosterTeam,
        [property: JsonPropertyName("initialSide")] string InitialSide,
        [property: JsonPropertyName("abilityId")] string AbilityId);

    public sealed record AbilitySyncBeginEnvelope(
        [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
        [property: JsonPropertyName("syncId")] string SyncId,
        [property: JsonPropertyName("matchId")] string MatchId,
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("catalogVersion")] string CatalogVersion,
        [property: JsonPropertyName("abilityModeEnabled")] bool AbilityModeEnabled,
        [property: JsonPropertyName("bannedAbilityIds")] List<string> BannedAbilityIds,
        [property: JsonPropertyName("expectedSeatCount")] int ExpectedSeatCount,
        [property: JsonPropertyName("contentDigest")] string ContentDigest);

    public sealed record AbilitySyncSeatEnvelope(
        [property: JsonPropertyName("syncId")] string SyncId,
        [property: JsonPropertyName("playerId")] string PlayerId,
        [property: JsonPropertyName("steamId")] string SteamId,
        [property: JsonPropertyName("rosterTeam")] string RosterTeam,
        [property: JsonPropertyName("initialSide")] string InitialSide,
        [property: JsonPropertyName("abilityId")] string AbilityId);

    public sealed record AbilitySyncCommitEnvelope(
        [property: JsonPropertyName("syncId")] string SyncId,
        [property: JsonPropertyName("contentDigest")] string ContentDigest);

    public sealed record AbilitySyncResetEnvelope(
        [property: JsonPropertyName("matchId")] string MatchId);

    private static class Base64Url
    {
        public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }
}
