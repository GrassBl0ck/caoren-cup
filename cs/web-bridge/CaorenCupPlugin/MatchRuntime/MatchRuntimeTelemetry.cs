using System.Text.Json;

namespace CaorenCupPlugin.MatchRuntime;

public static class MatchRuntimeTelemetry
{
    private static long _eventSequence;

    public static JsonElement AttachRoundIdentity(object payload, MatchRuntimeSnapshot snapshot, JsonSerializerOptions options)
    {
        var fields = JsonSerializer.SerializeToElement(payload, options).EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        fields["eventId"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"));
        fields["eventSequence"] = JsonSerializer.SerializeToElement(Interlocked.Increment(ref _eventSequence));
        fields["matchId"] = JsonSerializer.SerializeToElement(snapshot.MatchId);
        fields["revision"] = JsonSerializer.SerializeToElement(snapshot.Revision);
        fields["runtimeEpoch"] = JsonSerializer.SerializeToElement(snapshot.RuntimeEpoch);
        fields["roundKey"] = JsonSerializer.SerializeToElement(snapshot.CurrentRoundKey);
        fields["roundNumber"] = JsonSerializer.SerializeToElement(snapshot.CompletedRounds + 1);
        fields["round"] = fields["roundNumber"];
        fields["occurredAtUnixMs"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return JsonSerializer.SerializeToElement(fields, options);
    }
}
