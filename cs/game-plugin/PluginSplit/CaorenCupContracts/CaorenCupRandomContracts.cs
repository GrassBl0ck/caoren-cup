using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace CaorenCup.Contracts;

public sealed record RandomBuffView(string Key, string DisplayId, string Name, string Quality,
    string Category, string SimpleDescription, string DetailedDescription, int Level, int MaxLevel, int Cost)
{
    public string Scope { get; init; } = "个人";
    public bool Active { get; init; }
}
public sealed record RandomPlayerView(long Revision, int Round, int Coupons, bool CanPurchase,
    bool CanReroll, bool CanExchange, string Status, IReadOnlyList<RandomBuffView> Offers,
    IReadOnlyList<RandomBuffView> Owned)
{
    public string LockedKey { get; init; } = "";
    public int PerformanceCoupons { get; init; }
    public bool CanLock { get; init; }
    public IReadOnlyList<RandomSkillView> Skills { get; init; } = [];
}
public sealed record RandomSkillView(string Key, string Name, bool Available, string Status);
public sealed record RandomAdminPlayerView(string SteamId, string Name, int Team, bool Connected,
    bool Participating, int Coupons, bool PurchasedThisRound, bool RerolledThisRound,
    bool ExchangedThisRound, IReadOnlyList<RandomBuffView> Owned)
{
    public bool IsBot { get; init; }
    public long Revision { get; init; }
    public int PerformanceCoupons { get; init; }
}
public sealed record RandomAdminView(int Round, bool Running, IReadOnlyList<RandomAdminPlayerView> Players)
{
    public IReadOnlyList<RandomBuffView> Catalog { get; init; } = [];
}
public sealed record RandomDebugRequest(string Operation, string TargetKey, string BuffKey = "", int Level = 1,
    long ExpectedRevision = 0, bool TeamScope = false);

/// <summary>菜单只接收快照。全部交易由Random供应者重新验证身份、阶段与版本。</summary>
public interface ICaorenCupRandomApi
{
    bool IsRunning { get; }
    RandomPlayerView? GetView(CCSPlayerController player);
    RandomAdminView? GetAdminView(CCSPlayerController administrator);
    GameModeResult Purchase(CCSPlayerController player, string key, long revision);
    GameModeResult Reroll(CCSPlayerController player, long revision);
    GameModeResult Exchange(CCSPlayerController player, long revision);
    GameModeResult SetLock(CCSPlayerController player, string key, long revision);
    GameModeResult Activate(CCSPlayerController player, string key, long revision);
    GameModeResult Debug(CCSPlayerController administrator, RandomDebugRequest request);
}

public static class CaorenCupRandomAccess
{
    private static readonly PluginCapability<ICaorenCupRandomApi?> Capability = new("caorencup:random:v1");
    private static ICaorenCupRandomApi? _current;
    private static bool _registered;
    public static void Publish(ICaorenCupRandomApi api)
    {
        if (_current is not null && !ReferenceEquals(_current, api))
            throw new InvalidOperationException("Random provider already registered.");
        if (!_registered) { Capabilities.RegisterPluginCapability(Capability, () => _current); _registered = true; }
        _current = api;
    }
    public static void Withdraw(ICaorenCupRandomApi api) { if (ReferenceEquals(_current, api)) _current = null; }
    public static ICaorenCupRandomApi? TryGet() => _registered ? Capability.Get() : null;
}
