namespace CaorenCupPlugin;

public static class PluginTelemetryPolicy
{
    public const int OutboundQueueCapacity = 256;
    public const int MaxBufferedEvents = 2048;

    public static bool IsBatchable(string? type) => type is
        "weapon_fire" or
        "player_hurt" or
        "player_jump" or
        "player_crouch_sample";
}

public sealed record PluginTelemetryEvent(string Type, object Payload);

public sealed class PluginTelemetryBatchBuffer
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly List<PluginTelemetryEvent> _events = new();

    public PluginTelemetryBatchBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count
    {
        get { lock (_gate) return _events.Count; }
    }

    public bool TryAdd(string type, object payload)
    {
        if (!PluginTelemetryPolicy.IsBatchable(type)) return false;
        lock (_gate)
        {
            if (_events.Count >= _capacity) return false;
            _events.Add(new PluginTelemetryEvent(type, payload));
            return true;
        }
    }

    public IReadOnlyList<PluginTelemetryEvent> Drain()
    {
        lock (_gate)
        {
            if (_events.Count == 0) return Array.Empty<PluginTelemetryEvent>();
            var drained = _events.ToArray();
            _events.Clear();
            return drained;
        }
    }

    public int RestoreOlder(IReadOnlyList<PluginTelemetryEvent> olderEvents)
    {
        if (olderEvents.Count == 0) return 0;
        lock (_gate)
        {
            var combined = olderEvents.Concat(_events).Take(_capacity).ToArray();
            var dropped = olderEvents.Count + _events.Count - combined.Length;
            _events.Clear();
            _events.AddRange(combined);
            return dropped;
        }
    }
}
