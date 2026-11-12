using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CaorenCupPlugin.MatchRuntime;

namespace CaorenCupPlugin;

public sealed class AbilityRuntimeProtocolException(string message) : Exception(message);

public static class AbilityRuntimeProtocol
{
    public const string WebCommandType = "ABILITY_RUNTIME";
    public const string StartCommandName = "css_ability_runtime_start";
    public const string RoundStartCommandName = "css_ability_runtime_round_start";
    public const string StopCommandName = "css_ability_runtime_stop";
    public const string SubstituteCommandName = "css_ability_runtime_substitute";
    public const string RoundControlCommandName = "css_ability_runtime_round_control";
    public const string RestoreCommandName = "css_ability_runtime_restore";
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
            "round_start" when !string.IsNullOrWhiteSpace(envelope.RoundKey) => RoundStartCommandName,
            "stop" => StopCommandName,
            "substitute" when !string.IsNullOrWhiteSpace(envelope.SeatId)
                && Regex.IsMatch(envelope.SteamId ?? string.Empty, "^7656119[0-9]{10}$") => SubstituteCommandName,
            "round_control" when !string.IsNullOrWhiteSpace(envelope.RoundKey)
                && RoundControlOutcomes.Contains(envelope.Outcome ?? string.Empty) => RoundControlCommandName,
            "restore" when !string.IsNullOrWhiteSpace(envelope.RoundKey)
                && IsSafeCheckpointId(envelope.CheckpointId) => RestoreCommandName,
            _ => throw new AbilityRuntimeProtocolException("ABILITY_RUNTIME action or fields are invalid."),
        };
        return new AbilitySyncInternalCommand(commandName, AbilitySyncProtocol.Encode(envelope));
    }

    public static AbilitySyncInternalCommand BuildAuthoritativeRoundCommand(
        AbilityRuntimeEnvelope confirmed,
        MatchRuntimeSnapshot snapshot,
        bool firstRound)
    {
        if (snapshot.RuntimeEpoch <= 0
            || string.IsNullOrWhiteSpace(snapshot.CurrentRoundKey)
            || !string.Equals(confirmed.MatchId, snapshot.MatchId, StringComparison.Ordinal)
            || confirmed.Revision != snapshot.Revision)
            throw new AbilityRuntimeProtocolException("Authoritative match round identity is invalid.");
        var envelope = confirmed with
        {
            Action = firstRound ? "start" : "round_start",
            RuntimeEpoch = snapshot.RuntimeEpoch,
            RoundKey = snapshot.CurrentRoundKey,
            SeatId = null,
            SteamId = null,
            Outcome = null,
            CheckpointId = null,
        };
        ValidateIdentity(envelope);
        return new AbilitySyncInternalCommand(
            firstRound ? StartCommandName : RoundStartCommandName,
            AbilitySyncProtocol.Encode(envelope));
    }

    public static bool ShouldDispatchAuthoritativeReady(
        AbilityRuntimeEnvelope? confirmed,
        MatchRuntimeSnapshot? snapshot,
        string? matchController) =>
        confirmed is not null
        && snapshot is not null
        && string.Equals(matchController, "caoren", StringComparison.OrdinalIgnoreCase)
        && snapshot.Stage is not MatchStage.Disabled and not MatchStage.Completed and not MatchStage.Aborted
        && snapshot.RuntimeEpoch > 0
        && !string.IsNullOrWhiteSpace(snapshot.CurrentRoundKey)
        && string.Equals(confirmed.MatchId, snapshot.MatchId, StringComparison.Ordinal)
        && confirmed.Revision == snapshot.Revision;

    private static void ValidateIdentity(AbilityRuntimeEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.MatchId) || envelope.Revision <= 0 || envelope.RuntimeEpoch <= 0
            || !Regex.IsMatch(envelope.ContentDigest ?? string.Empty, "^[a-f0-9]{64}$"))
            throw new AbilityRuntimeProtocolException("ABILITY_RUNTIME identity is invalid.");
    }

    private static bool IsSafeCheckpointId(string? checkpointId) =>
        !string.IsNullOrWhiteSpace(checkpointId)
        && checkpointId.Length <= 64
        && !checkpointId.Contains("..", StringComparison.Ordinal)
        && Regex.IsMatch(checkpointId, "^[a-zA-Z0-9][a-zA-Z0-9._-]*$");

    public sealed record AbilityRuntimeEnvelope(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("matchId")] string MatchId,
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("contentDigest")] string ContentDigest,
        [property: JsonPropertyName("runtimeEpoch")] long RuntimeEpoch,
        [property: JsonPropertyName("roundKey")] string? RoundKey = null,
        [property: JsonPropertyName("seatId")] string? SeatId = null,
        [property: JsonPropertyName("steamId")] string? SteamId = null,
        [property: JsonPropertyName("outcome")] string? Outcome = null,
        [property: JsonPropertyName("checkpointId")] string? CheckpointId = null);
}
