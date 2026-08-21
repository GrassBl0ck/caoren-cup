using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CaorenCupPlugin;

public sealed class AbilityRuntimeProtocolException(string message) : Exception(message);

public static class AbilityRuntimeProtocol
{
    public const string WebCommandType = "ABILITY_RUNTIME";
    public const string StartCommandName = "css_ability_runtime_start";
    public const string StopCommandName = "css_ability_runtime_stop";
    public const string SubstituteCommandName = "css_ability_runtime_substitute";
    public const string RoundControlCommandName = "css_ability_runtime_round_control";
    public const string ReadyCommandName = "css_ability_runtime_ready";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> RoundControlOutcomes = new(StringComparer.Ordinal)
    {
        "draw", "restarted", "admin_cancelled",
    };

    public static AbilitySyncInternalCommand BuildInternalCommand(PluginCommand command)
    {
        if (!string.Equals(command.Type, WebCommandType, StringComparison.OrdinalIgnoreCase)
            || command.Payload.ValueKind != JsonValueKind.Object)
            throw new AbilityRuntimeProtocolException("ABILITY_RUNTIME payload is not a structured object.");

        var envelope = JsonSerializer.Deserialize<AbilityRuntimeEnvelope>(command.Payload.GetRawText(), JsonOptions)
            ?? throw new AbilityRuntimeProtocolException("ABILITY_RUNTIME payload is empty.");
        ValidateIdentity(envelope);
        var commandName = envelope.Action switch
        {
            "start" when !string.IsNullOrWhiteSpace(envelope.RoundKey) => StartCommandName,
            "stop" => StopCommandName,
            "substitute" when !string.IsNullOrWhiteSpace(envelope.SeatId)
                && Regex.IsMatch(envelope.SteamId ?? string.Empty, "^7656119[0-9]{10}$") => SubstituteCommandName,
            "round_control" when !string.IsNullOrWhiteSpace(envelope.RoundKey)
                && RoundControlOutcomes.Contains(envelope.Outcome ?? string.Empty) => RoundControlCommandName,
            _ => throw new AbilityRuntimeProtocolException("ABILITY_RUNTIME action or fields are invalid."),
        };
        return new AbilitySyncInternalCommand(commandName, AbilitySyncProtocol.Encode(envelope));
    }

    private static void ValidateIdentity(AbilityRuntimeEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.MatchId) || envelope.Revision <= 0
            || !Regex.IsMatch(envelope.ContentDigest ?? string.Empty, "^[a-f0-9]{64}$"))
            throw new AbilityRuntimeProtocolException("ABILITY_RUNTIME identity is invalid.");
    }

    public sealed record AbilityRuntimeEnvelope(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("matchId")] string MatchId,
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("contentDigest")] string ContentDigest,
        [property: JsonPropertyName("roundKey")] string? RoundKey = null,
        [property: JsonPropertyName("seatId")] string? SeatId = null,
        [property: JsonPropertyName("steamId")] string? SteamId = null,
        [property: JsonPropertyName("outcome")] string? Outcome = null);
}
