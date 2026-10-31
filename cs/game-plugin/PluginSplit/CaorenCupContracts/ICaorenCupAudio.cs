using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace CaorenCup.Contracts;

public enum CaorenAudioChannel { Effect, Music, Broadcast }
public enum CaorenAudioControl { Pause, Resume, Stop, LoopOn, LoopOff }
public sealed record CaorenAudioResult(bool Success, string Message, int Recipients = 0);
public sealed record CaorenAudioCapabilities(bool TargetedStop, bool PauseResume, bool Seek, bool Loop);
public sealed record CaorenAudioEvent(string Id, string DisplayName, string Source,
    bool NativeEvent = false, float DefaultVolume = 1, CaorenAudioChannel Channel = CaorenAudioChannel.Effect,
    bool Loop = false);

public interface ICaorenCupAudio
{
    CaorenAudioCapabilities Capabilities { get; }
    CaorenAudioResult Play(string eventId, IReadOnlyList<CCSPlayerController>? recipients = null, CBaseEntity? source = null);
    CaorenAudioResult Control(CaorenAudioChannel channel, CaorenAudioControl operation);
}

// 独立扩展，不修改既有 v1 接口或事件构造函数。
public sealed record CaorenAudioChannelStatus(CaorenAudioChannel Channel, string? EventId, string DisplayName,
    string State, double PositionSeconds, double DurationSeconds, bool LoopEnabled, bool FinishingCycle, int Recipients);
public interface ICaorenCupAudioStatus
{
    IReadOnlyCollection<CaorenAudioEvent> Events { get; }
    IReadOnlyList<CaorenAudioChannelStatus> GetChannels();
}

public static class CaorenCupAudioAccess
{
    private static readonly PluginCapability<ICaorenCupAudio?> Capability = new("caorencup:audio:v1");
    private static ICaorenCupAudio? _current;
    private static bool _registered;
    private static readonly Dictionary<string, string> LastFailures = new(StringComparer.Ordinal);
    public static void Publish(ICaorenCupAudio service)
    {
        if (_current != null && !ReferenceEquals(_current, service)) throw new InvalidOperationException("Another audio service is active.");
        if (!_registered) { Capabilities.RegisterPluginCapability(Capability, () => _current); _registered = true; }
        _current = service;
    }
    public static void Withdraw(ICaorenCupAudio service) { if (ReferenceEquals(_current, service)) _current = null; }
    public static ICaorenCupAudio? TryGet() => _registered ? Capability.Get() : null;
    public static CaorenAudioResult Play(string eventId, IReadOnlyList<CCSPlayerController>? recipients = null, CBaseEntity? source = null)
    {
        CaorenAudioResult result;
        try { result = TryGet()?.Play(eventId, recipients, source) ?? new(false, "草人杯音频服务不可用。"); }
        catch (Exception ex) { result = new(false, "草人杯音频播放失败：" + ex.Message); }
        if (result.Success) LastFailures.Remove(eventId);
        else if (LastFailures.GetValueOrDefault(eventId) != result.Message)
        {
            LastFailures[eventId] = result.Message;
            Console.WriteLine($"[CaorenCupAudio] {eventId}: {result.Message}");
        }
        return result;
    }
}
