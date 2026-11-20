using System.Text.Json;

namespace CaorenCup.Contracts.MatchRuntime;

public sealed record MatchCombatEvent(string Type, JsonElement Payload);
public sealed record MatchCombatBatch(
    string MatchId, int Revision, long RuntimeEpoch, long Through, bool Incomplete,
    IReadOnlyList<MatchCombatEvent> Events);

internal sealed class MatchCombatOutbox
{
    private const int MaxRetainedEvents = 65_536;
    private const int MaxBatchSize = 128;
    private readonly Queue<(long Sequence, MatchCombatEvent Event)> _pending = new();
    private (string MatchId, int Revision, long RuntimeEpoch)? _identity;
    private bool _incomplete;

    public bool Enqueue(MatchRuntimeSnapshot snapshot, string type, JsonElement payload)
    {
        if (snapshot.CurrentRoundKey is null || snapshot.MatchId is null) return false;
        EnsureIdentity(snapshot);
        if (_pending.Count == MaxRetainedEvents)
        {
            _incomplete = true;
            return false;
        }
        _pending.Enqueue((payload.GetProperty("eventSequence").GetInt64(), new(type, payload)));
        return true;
    }

    public MatchCombatBatch? Read(MatchRuntimeSnapshot snapshot)
    {
        if (snapshot.MatchId is null) return null;
        EnsureIdentity(snapshot);
        var items = _pending.Take(MaxBatchSize).ToArray();
        return new(snapshot.MatchId, snapshot.Revision, snapshot.RuntimeEpoch,
            items.Length == 0 ? 0 : items[^1].Sequence, _incomplete, items.Select(item => item.Event).ToArray());
    }

    public void Acknowledge(MatchRuntimeSnapshot snapshot, MatchCombatBatch batch, long through)
    {
        if (through <= 0 || through != batch.Through
            || (snapshot.MatchId, snapshot.Revision, snapshot.RuntimeEpoch) != (batch.MatchId, batch.Revision, batch.RuntimeEpoch))
            return;
        EnsureIdentity(snapshot);
        while (_pending.TryPeek(out var first) && first.Sequence <= through) _pending.Dequeue();
    }

    private void EnsureIdentity(MatchRuntimeSnapshot snapshot)
    {
        var identity = (snapshot.MatchId!, snapshot.Revision, snapshot.RuntimeEpoch);
        if (_identity == identity) return;
        _identity = identity;
        _pending.Clear();
        _incomplete = false;
    }
}
