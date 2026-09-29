using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;

namespace CaorenCup.WeaponPaints;

public partial class CaorenWeaponPaintsPlugin
{
    internal static CaorenWeaponPaintsPlugin Instance { get; private set; } = null!;
    internal static CaorenWeaponPaintsConfig _config { get; private set; } = new();

    internal static readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, string>> GPlayersKnife = new();
    internal static readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, ushort>> GPlayersGlove = new();
    internal static readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, ushort>> GPlayersMusic = new();
    internal static readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, ushort>> GPlayersPin = new();
    internal static readonly ConcurrentDictionary<int, (string? CT, string? T)> GPlayersAgent = new();
    internal static readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>> GPlayerWeaponsInfo = new();
    internal static List<JObject> SkinsList { get; private set; } = [];

    private static MemoryFunctionVoid<nint, string, float>? _attributeSetter;
    private static MemoryFunctionVoid<nint, string, float> CAttributeListSetOrAddAttributeValueByName =>
        _attributeSetter ??= new MemoryFunctionVoid<nint, string, float>(
            GameData.GetSignature("CAttributeList_SetOrAddAttributeValueByName"));

    private static readonly Dictionary<int, string> WeaponDefindex = new()
    {
        [1] = "weapon_deagle", [2] = "weapon_elite", [3] = "weapon_fiveseven", [4] = "weapon_glock",
        [7] = "weapon_ak47", [8] = "weapon_aug", [9] = "weapon_awp", [10] = "weapon_famas",
        [11] = "weapon_g3sg1", [13] = "weapon_galilar", [14] = "weapon_m249", [16] = "weapon_m4a1",
        [17] = "weapon_mac10", [19] = "weapon_p90", [23] = "weapon_mp5sd", [24] = "weapon_ump45",
        [25] = "weapon_xm1014", [26] = "weapon_bizon", [27] = "weapon_mag7", [28] = "weapon_negev",
        [29] = "weapon_sawedoff", [30] = "weapon_tec9", [31] = "weapon_taser", [32] = "weapon_hkp2000",
        [33] = "weapon_mp7", [34] = "weapon_mp9", [35] = "weapon_nova", [36] = "weapon_p250",
        [38] = "weapon_scar20", [39] = "weapon_sg556", [40] = "weapon_ssg08", [60] = "weapon_m4a1_silencer",
        [61] = "weapon_usp_silencer", [63] = "weapon_cz75a", [64] = "weapon_revolver",
        [500] = "weapon_bayonet", [503] = "weapon_knife_css", [505] = "weapon_knife_flip",
        [506] = "weapon_knife_gut", [507] = "weapon_knife_karambit", [508] = "weapon_knife_m9_bayonet",
        [509] = "weapon_knife_tactical", [512] = "weapon_knife_falchion", [514] = "weapon_knife_survival_bowie",
        [515] = "weapon_knife_butterfly", [516] = "weapon_knife_push", [517] = "weapon_knife_cord",
        [518] = "weapon_knife_canis", [519] = "weapon_knife_ursus", [520] = "weapon_knife_gypsy_jackknife",
        [521] = "weapon_knife_outdoor", [522] = "weapon_knife_stiletto", [523] = "weapon_knife_widowmaker",
        [525] = "weapon_knife_skeleton", [526] = "weapon_knife_kukri"
    };

    private const ulong MinimumCustomItemId = 65578;
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, float>> _temporaryPlayerWeaponWear = new();
    private ulong _nextItemId = MinimumCustomItemId;
    private int _fadeSeed;
    private bool _gBCommandsAllowed = true;

    internal static void SetInstance(CaorenWeaponPaintsPlugin instance, CaorenWeaponPaintsConfig config)
    {
        Instance = instance;
        _config = config;
    }

    internal static void SetSkinMetadata(List<JObject> skins)
    {
        SkinsList = skins;
    }

    internal static void ClearRuntimeState()
    {
        GPlayersKnife.Clear();
        GPlayersGlove.Clear();
        GPlayersMusic.Clear();
        GPlayersPin.Clear();
        GPlayersAgent.Clear();
        GPlayerWeaponsInfo.Clear();
    }
}

internal static class WeaponPaintsUtility
{
    internal static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player is { IsValid: true, IsBot: false, IsHLTV: false, UserId: not null };
    }
}
