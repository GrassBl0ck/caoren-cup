using CounterStrikeSharp.API.Core.Capabilities;
using CaorenCup.Features;

namespace CaorenCup.Contracts;

/// <summary>跨插件共享的最小契约。后续能力必须保持版本兼容。</summary>
public interface ICaorenCupCoreApi
{
    int ContractVersion { get; }
    CaorenCupConfig Config { get; }
    ManagedCvarScope ManagedCvars { get; }
    string LegacyConfigPath { get; }
    string ModulesDirectory { get; }
    void SaveConfig();
}

/// <summary>Core 热重载时保留模块配置对象与已托管的 CVar 状态。</summary>
public sealed record CaorenCupRuntimeState(
    CaorenCupConfig Config,
    ManagedCvarScope ManagedCvars);

/// <summary>
/// 共享契约程序集持有稳定的供应者，避免插件热重载后 Capability
/// 留下指向旧插件实例的委托。
/// </summary>
public static class CaorenCupCoreAccess
{
    public const int CurrentContractVersion = 1;
    private static readonly object Gate = new();
    private static readonly PluginCapability<ICaorenCupCoreApi?> Capability = new("caorencup:core:v1");
    private static ICaorenCupCoreApi? _current;
    private static bool _registered;
    private static CaorenCupRuntimeState? _runtimeState;

    public static CaorenCupRuntimeState? GetRetainedRuntimeState()
    {
        lock (Gate) return _runtimeState;
    }

    public static void RetainRuntimeState(CaorenCupRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (Gate) _runtimeState = state;
    }

    public static void Publish(ICaorenCupCoreApi core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (core.ContractVersion != CurrentContractVersion)
            throw new ArgumentException("CaorenCup Core contract version mismatch.", nameof(core));

        lock (Gate)
        {
            if (_current is not null && !ReferenceEquals(_current, core))
                throw new InvalidOperationException("Another CaorenCup Core is already active.");

            if (!_registered)
            {
                Capabilities.RegisterPluginCapability(Capability, () => _current);
                _registered = true;
            }

            _current = core;
        }
    }

    public static void Withdraw(ICaorenCupCoreApi core)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_current, core))
                _current = null;
        }
    }

    public static ICaorenCupCoreApi? TryGet()
    {
        lock (Gate)
        {
            // CounterStrikeSharp 1.0.374 的 Capability.Get() 在没有
            // 注册供应者时会抛 KeyNotFoundException，故先检查登记状态。
            return _registered ? Capability.Get() : null;
        }
    }
}
