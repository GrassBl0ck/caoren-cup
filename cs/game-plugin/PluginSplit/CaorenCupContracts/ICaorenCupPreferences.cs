using CounterStrikeSharp.API.Core.Capabilities;

namespace CaorenCup.Contracts;

public sealed record CaorenCupPreference(int Transparency = 10, int Volume = 100,
    bool MouseEffectEnabled = false, string MouseEffectColor = "blue")
{
    public CaorenCupPreference Clamp() => this with
    {
        Transparency = Math.Clamp(Transparency, 0, 100), Volume = Math.Clamp(Volume, 0, 100)
    };
}

public interface ICaorenCupPreferences
{
    CaorenCupPreference Get(string steamId);
    void Set(string steamId, int? transparency = null, int? volume = null);
    // 任意冲突都使整个导入停止；旧文件由调用方备份，绝不删除。
    void ImportLegacy(IReadOnlyDictionary<string, CaorenCupPreference> values);
}

/// <summary>独立 capability，不改变现有 Core v1 接口，热重载时委托读取当前实例。</summary>
public static class CaorenCupPreferencesAccess
{
    private static readonly PluginCapability<ICaorenCupPreferences?> Capability = new("caorencup:preferences:v1");
    private static ICaorenCupPreferences? _current;
    private static bool _registered;
    public static void Publish(ICaorenCupPreferences service)
    {
        if (_current != null && !ReferenceEquals(_current, service)) throw new InvalidOperationException("Another preferences service is active.");
        if (!_registered) { Capabilities.RegisterPluginCapability(Capability, () => _current); _registered = true; }
        _current = service;
    }
    public static void Withdraw(ICaorenCupPreferences service) { if (ReferenceEquals(_current, service)) _current = null; }
    public static ICaorenCupPreferences? TryGet() => _registered ? Capability.Get() : null;
}
