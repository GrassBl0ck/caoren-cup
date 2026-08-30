using Xunit;

namespace CaorenCupPlugin.Tests;

public class PluginTelemetryBatchTests
{
    [Fact]
    public void Batch_buffer_accepts_only_high_frequency_telemetry_and_preserves_order()
    {
        var buffer = new PluginTelemetryBatchBuffer(8);

        Assert.True(buffer.TryAdd("weapon_fire", new { value = 1 }));
        Assert.True(buffer.TryAdd("player_hurt", new { value = 2 }));
        Assert.True(buffer.TryAdd("player_jump", new { value = 3 }));
        Assert.True(buffer.TryAdd("player_crouch_sample", new { value = 4 }));
        Assert.False(buffer.TryAdd("player_death", new { value = 5 }));

        var events = buffer.Drain();
        Assert.Equal(
            new[] { "weapon_fire", "player_hurt", "player_jump", "player_crouch_sample" },
            events.Select(item => item.Type));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Batch_buffer_is_bounded_and_can_continue_after_drain()
    {
        var buffer = new PluginTelemetryBatchBuffer(2);

        Assert.True(buffer.TryAdd("weapon_fire", new { value = 1 }));
        Assert.True(buffer.TryAdd("player_jump", new { value = 2 }));
        Assert.False(buffer.TryAdd("player_hurt", new { value = 3 }));
        Assert.Equal(2, buffer.Count);

        Assert.Equal(2, buffer.Drain().Count);
        Assert.True(buffer.TryAdd("player_hurt", new { value = 4 }));
    }

    [Fact]
    public void Outbound_policy_and_batch_message_keep_memory_bounded()
    {
        Assert.Equal(256, PluginTelemetryPolicy.OutboundQueueCapacity);
        Assert.Equal(2048, PluginTelemetryPolicy.MaxBufferedEvents);

        var events = new[] { new PluginTelemetryEvent("weapon_fire", new { value = 1 }) };
        var message = PluginOutboundMessage.ForEventBatch(
            events,
            "match-1",
            9,
            DateTimeOffset.Parse("2026-08-25T00:00:00Z"));

        Assert.Equal(PluginOutboundKind.EventBatch, message.Kind);
        Assert.Equal("match-1", message.MatchId);
        Assert.Same(events, message.Body);
    }

    [Fact]
    public void Failed_queue_write_restores_older_events_without_exceeding_capacity()
    {
        var buffer = new PluginTelemetryBatchBuffer(2);
        Assert.True(buffer.TryAdd("weapon_fire", new { value = 1 }));
        Assert.True(buffer.TryAdd("player_jump", new { value = 2 }));
        var older = buffer.Drain();
        Assert.True(buffer.TryAdd("player_hurt", new { value = 3 }));

        var dropped = buffer.RestoreOlder(older);
        var restored = buffer.Drain();

        Assert.Equal(1, dropped);
        Assert.Equal(new[] { "weapon_fire", "player_jump" }, restored.Select(item => item.Type));
    }
}
