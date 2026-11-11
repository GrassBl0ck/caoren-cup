using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace CaorenCup.Contracts;

/// <summary>玩法提供个人参数，现有娱乐模块继续负责实际效果。</summary>
public sealed record CaorenCupPlayerEffects(
    string Identity, int Round, int ExtraJumps = 0, int ExtraJumpsUsed = 0,
    float FireScale = 1, float SwitchSpeedPercent = 100, float FireSpeedPercent = 100,
    float MovePenaltyMultiplier = 1, float RecoilMultiplier = 1, float TagReduction = 0);

public interface ICaorenCupPlayerEffectsSource
{
    CaorenCupPlayerEffects? GetEffects(CCSPlayerController player);
    void ReportExtraJumpsUsed(CCSPlayerController player, int used);
    void ApplyTagging(CCSPlayerController player, Func<float, float> adjust);
}

public static class CaorenCupPlayerEffectsAccess
{
    private static readonly PluginCapability<ICaorenCupPlayerEffectsSource?> Capability = new("caorencup:player-effects:v1");
    private static ICaorenCupPlayerEffectsSource? _current;
    private static bool _registered;
    private static object? _consumer;
    public static bool ConsumerAvailable => _consumer is not null;

    public static void RegisterConsumer(object owner) => _consumer = owner;
    public static void UnregisterConsumer(object owner)
    {
        if (ReferenceEquals(_consumer, owner)) _consumer = null;
    }

    public static void Publish(ICaorenCupPlayerEffectsSource source)
    {
        _current = source;
        if (_registered) return;
        Capabilities.RegisterPluginCapability(Capability, () => _current);
        _registered = true;
    }

    public static void Withdraw(ICaorenCupPlayerEffectsSource source)
    {
        if (ReferenceEquals(_current, source)) _current = null;
    }

    public static ICaorenCupPlayerEffectsSource? TryGet() => _registered ? Capability.Get() : null;
    public static CaorenCupPlayerEffects? Get(CCSPlayerController player) => TryGet()?.GetEffects(player);
}
