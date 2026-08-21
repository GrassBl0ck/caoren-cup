using CaorenCup;

namespace Caoren.AbilityMode;

public sealed record AbilityRoleValidationResult(bool Ok, string Code = "OK", string Message = "");

public sealed class AbilityRoleRegistry
{
    private static readonly HashSet<string> KnownCatalogRoleIds = new(StringComparer.Ordinal)
    {
        "medic", "berserker", "assassin", "tank", "istaru", "capitalist", "balance",
        "glass_cannon", "utility_specialist", "commander", "sky_courier", "snow_golem", "witch",
    };

    private readonly IReadOnlyDictionary<string, bool> _enabled;

    private AbilityRoleRegistry(AbilityRoleSettings settings)
    {
        _enabled = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["medic"] = settings.MedicEnabled,
            ["berserker"] = settings.BerserkerEnabled,
            ["tank"] = settings.TankEnabled,
            ["capitalist"] = settings.CapitalistEnabled,
            ["balance"] = settings.BalanceEnabled,
            ["commander"] = settings.CommanderEnabled,
        };
    }

    public IEnumerable<string> RegisteredRoleIds => _enabled.Keys;

    public static AbilityRoleRegistry Create(AbilityRoleSettings? settings) => new(settings ?? new AbilityRoleSettings());

    public bool IsEnabled(string abilityId) => _enabled.TryGetValue(abilityId, out var enabled) && enabled;

    public AbilityRoleValidationResult ValidateSelectedRoles(AbilitySyncConfig config)
    {
        foreach (var roleId in config.Seats.Select(seat => seat.AbilityId).Distinct(StringComparer.Ordinal))
        {
            if (_enabled.TryGetValue(roleId, out var enabled))
            {
                if (!enabled)
                    return new(false, "ROLE_DISABLED", $"职业 {roleId} 已在服务器配置中关闭，拒绝启动异能比赛。");
                continue;
            }

            if (KnownCatalogRoleIds.Contains(roleId))
                return new(false, "ROLE_NOT_IMPLEMENTED", $"职业 {roleId} 尚未在当前运行时实现，拒绝启动异能比赛。");
            return new(false, "UNKNOWN_ROLE", $"无法识别职业 {roleId}。");
        }

        return new(true);
    }
}
