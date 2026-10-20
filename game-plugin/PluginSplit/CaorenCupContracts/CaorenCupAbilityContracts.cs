using CounterStrikeSharp.API.Core.Capabilities;

namespace CaorenCup.Contracts;

/// <summary>异能职业定义投影，供游戏内菜单与状态页展示。</summary>
public sealed record CaorenCupAbilityRoleInfo(string AbilityId, string DisplayName, bool Enabled);

/// <summary>已分配异能职业的玩家投影。</summary>
public sealed record CaorenCupAbilitySeatInfo(string SteamId, string PlayerName, string AbilityId);

/// <summary>异能模式对外只读能力。写入操作仍走 CaorenCupGamemode_Ability 的命令。</summary>
public interface ICaorenCupAbilityApi
{
    int ContractVersion { get; }

    /// <summary>当前是否已有运行中的异能比赛运行时。</summary>
    bool IsRunning { get; }

    /// <summary>单行状态摘要，用于菜单状态行。</summary>
    string StatusText { get; }

    IReadOnlyList<CaorenCupAbilityRoleInfo> Roles { get; }

    /// <summary>当前对局双方在线玩家；未分配职业时 AbilityId 为空串。</summary>
    IReadOnlyList<CaorenCupAbilitySeatInfo> OnlinePlayers { get; }

    IReadOnlyList<CaorenCupAbilitySeatInfo> AssignedPlayers { get; }
}

/// <summary>
/// 契约程序集持有稳定供应者，避免异能插件热重载后 Capability 指向旧实例。
/// </summary>
public static class CaorenCupAbilityAccess
{
    public const int CurrentContractVersion = 1;

    private static readonly object Gate = new();
    private static readonly PluginCapability<ICaorenCupAbilityApi?> Capability = new("caorencup:ability:v1");
    private static ICaorenCupAbilityApi? _current;
    private static bool _registered;

    public static void Publish(ICaorenCupAbilityApi ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        if (ability.ContractVersion != CurrentContractVersion)
            throw new ArgumentException("CaorenCup Ability contract version mismatch.", nameof(ability));

        lock (Gate)
        {
            if (_current is not null && !ReferenceEquals(_current, ability))
                throw new InvalidOperationException("Another CaorenCup Ability is already active.");

            if (!_registered)
            {
                Capabilities.RegisterPluginCapability(Capability, () => _current);
                _registered = true;
            }

            _current = ability;
        }
    }

    public static void Withdraw(ICaorenCupAbilityApi ability)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_current, ability))
                _current = null;
        }
    }

    public static ICaorenCupAbilityApi? TryGet()
    {
        lock (Gate)
        {
            return _registered ? Capability.Get() : null;
        }
    }
}
