using System.Globalization;

namespace CaorenCupPlugin;

internal enum DuelPreferredWeaponSlot
{
    Primary,
    Secondary
}

internal sealed record DuelLoadoutRule(
    IReadOnlySet<string> AllowedFirearms,
    string PreferredWeapon,
    DuelPreferredWeaponSlot PreferredSlot);

internal sealed record DuelPlayerLoadoutInput(string SteamId, string Primary, string Secondary);

internal sealed record DuelSteamBoundLoadoutPlan(
    string SteamId,
    string Primary,
    string Secondary,
    DuelLoadoutRule Rule);

internal sealed record DuelCvarSetting(string Name, string Value, string Fallback);

internal static class DuelRuntimePolicy
{
    private static readonly HashSet<string> NonFirearmWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapon_bayonet",
        "weapon_breachcharge",
        "weapon_bumpmine",
        "weapon_c4",
        "weapon_decoy",
        "weapon_diversion",
        "weapon_firebomb",
        "weapon_fists",
        "weapon_flashbang",
        "weapon_frag_grenade",
        "weapon_healthshot",
        "weapon_hegrenade",
        "weapon_incgrenade",
        "weapon_molotov",
        "weapon_shield",
        "weapon_smokegrenade",
        "weapon_snowball",
        "weapon_tagrenade",
        "weapon_tripwirefire"
    };

    public static DuelLoadoutRule BuildLoadoutRule(DuelStage stage, string? primary, string? secondary)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsFirearm(primary)) allowed.Add(primary!);
        if (IsFirearm(secondary)) allowed.Add(secondary!);

        var preferred = stage == DuelStage.Pistol ? secondary : primary;
        if (!IsFirearm(preferred))
        {
            throw new InvalidOperationException("Duel stage has no preferred firearm.");
        }

        return new DuelLoadoutRule(
            allowed,
            preferred!,
            stage == DuelStage.Pistol
                ? DuelPreferredWeaponSlot.Secondary
                : DuelPreferredWeaponSlot.Primary);
    }

    public static IReadOnlyDictionary<string, DuelSteamBoundLoadoutPlan> BuildSteamBoundLoadoutPlans(
        DuelStage stage,
        IEnumerable<DuelPlayerLoadoutInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var plans = new Dictionary<string, DuelSteamBoundLoadoutPlan>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.SteamId))
            {
                throw new ArgumentException("Duel loadout SteamID cannot be empty.", nameof(inputs));
            }

            var plan = new DuelSteamBoundLoadoutPlan(
                input.SteamId,
                input.Primary,
                input.Secondary,
                BuildLoadoutRule(stage, input.Primary, input.Secondary));
            if (!plans.TryAdd(input.SteamId, plan))
            {
                throw new ArgumentException($"Duplicate duel loadout SteamID: {input.SteamId}", nameof(inputs));
            }
        }

        return plans;
    }

    public static bool IsFirearm(string? designerName)
    {
        if (string.IsNullOrWhiteSpace(designerName)) return false;
        if (!designerName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)) return false;
        if (designerName.StartsWith("weapon_knife", StringComparison.OrdinalIgnoreCase)) return false;
        return !NonFirearmWeapons.Contains(designerName);
    }

    public static bool ShouldBlockDrop(string? designerName) => IsFirearm(designerName);

    public static bool CanActivateRuntime(bool cvarScopeReady, bool duelRuntimeActive) =>
        cvarScopeReady || duelRuntimeActive;

    public static int EngineRoundLimit(int configuredTotalRounds)
    {
        if (configuredTotalRounds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredTotalRounds));
        }

        return configuredTotalRounds;
    }

    public static IReadOnlyList<DuelCvarSetting> BuildCvarPlan(DuelGameConfig config)
    {
        return BuildCvarPlan(config, EngineRoundLimit(config.TotalRounds), includeNativePostMatch: true);
    }

    internal static IReadOnlyList<DuelCvarSetting> BuildWebManagedCvarPlan(DuelGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return BuildCvarPlan(config, checked(config.TotalRounds + 1), includeNativePostMatch: false);
    }

    private static IReadOnlyList<DuelCvarSetting> BuildCvarPlan(
        DuelGameConfig config,
        int engineRoundLimit,
        bool includeNativePostMatch)
    {
        ArgumentNullException.ThrowIfNull(config);
        var settings = new List<DuelCvarSetting>
        {
            new("mp_maxrounds", engineRoundLimit.ToString(CultureInfo.InvariantCulture), "24"),
            new("mp_winlimit", "0", "0"),
            new("mp_match_can_clinch", "0", "1"),
            new("mp_roundtime", config.RoundTimeMinutes.ToString("0.##", CultureInfo.InvariantCulture), "1.92"),
            new("mp_freezetime", "0", "15"),
            new("mp_round_restart_delay", "2", "7"),
            new("mp_free_armor", "0", "0"),
            new("mp_halftime", "0", "1"),
            new("mp_autoteambalance", "0", "1"),
            new("mp_limitteams", "0", "2"),
            new("mp_weapons_allow_map_placed", "0", "1"),
            new("mp_death_drop_gun", "0", "1"),
            new("sv_showimpacts", "0", "0"),
            new("sv_showimpacts_time", "0", "4")
        };

        if (includeNativePostMatch)
        {
            settings.Add(new("mp_overtime_enable", "0", "1"));
            settings.Add(new("mp_endmatch_votenextmap", "0", "1"));
            settings.Add(new("mp_match_end_restart", "1", "0"));
        }

        return settings;
    }
}
