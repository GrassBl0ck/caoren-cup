using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public class LoadoutFeature : ICaorenFeature
{
    public string FeatureName => "Loadout (默认装备)";

    private const string Owner = "Loadout";
    private CaorenCupPlugin _plugin = null!;
    private bool _enabled;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        plugin.AddCommand("css_loadout", "默认装备控制: /loadout weapon|grenade|armor|buy|allow|healthshot|reset", OnCommand);
        plugin.AddCommand("loadout", "默认装备控制: /loadout weapon|grenade|armor|buy|allow|healthshot|reset", OnCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config) { }

    public void OnUnload() => SetEnabled(false);

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _plugin.ManagedCvars.ResetOwner(Owner);
        }
    }

    public string GetHelpEntry() =>
        $" {ChatColors.Green}/loadout weapon|grenade|armor|buy|allow|healthshot|reset{ChatColors.Default} : 默认装备/武器池/治疗针";

    public string GetStatusInfo() =>
        $"Loadout: {(_enabled ? $"{ChatColors.Green}已启用" : $"{ChatColors.Red}已禁用")}{ChatColors.Default}";

    public string? GetPublicConfigInfo() => _enabled ? "[装备规则] 已修改默认武器/投掷物/护甲或购买规则。" : null;

    public string GetFeatureDescription() =>
        "Loadout 默认装备模块。\n" +
        "/loadout weapon <t/ct/all/0> <primary|secondary> <weapon|none>\n" +
        "/loadout grenade <t/ct/all/0> <grenade-list|none>\n" +
        "/loadout armor <t/ct/all/0> <0|1|2>\n" +
        "/loadout buy <time> <anywhere 0/1>\n" +
        "/loadout allow <heavy> <pistols> <rifles> <smgs> <zeus>\n" +
        "/loadout healthshot <t/ct/all/0> <heal|regen|immune|resist>\n" +
        "/loadout reset 恢复本模块托管的 CVar。";

    private void OnCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player)) return;

        if (info.ArgCount < 2)
        {
            PrintUsage(player);
            return;
        }

        string action = info.GetArg(1).Trim().ToLowerInvariant();
        if (IsResetArg(action))
        {
            SetEnabled(false);
            Reply(player, "Loadout 已恢复默认。");
            return;
        }

        switch (action)
        {
            case "weapon":
                HandleWeapon(player, info);
                return;
            case "grenade":
                HandleGrenade(player, info);
                return;
            case "armor":
                HandleArmor(player, info);
                return;
            case "buy":
                HandleBuy(player, info);
                return;
            case "allow":
                HandleAllow(player, info);
                return;
            case "healthshot":
                HandleHealthshot(player, info);
                return;
            default:
                PrintUsage(player);
                return;
        }
    }

    private void HandleWeapon(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 5)
        {
            Reply(player, "用法: /loadout weapon <t/ct/all/0> <primary|secondary> <weapon|none>");
            return;
        }

        string target = info.GetArg(2).Trim().ToLowerInvariant();
        string slot = info.GetArg(3).Trim().ToLowerInvariant();
        string weapon = NormalizeNone(info.GetArg(4).Trim());

        if (target == "0")
        {
            SetEnabled(false);
            Reply(player, "Loadout 已恢复默认。");
            return;
        }

        if (!TryGetTeams(target, out bool t, out bool ct))
        {
            Reply(player, "目标无效，请使用 t / ct / all / 0。");
            return;
        }

        string suffix = slot switch
        {
            "primary" => "primary",
            "secondary" => "secondary",
            _ => string.Empty
        };

        if (suffix.Length == 0)
        {
            Reply(player, "武器槽无效，请使用 primary 或 secondary。");
            return;
        }

        if (t) Set($"mp_t_default_{suffix}", weapon, suffix == "primary" ? "1" : "weapon_glock");
        if (ct) Set($"mp_ct_default_{suffix}", weapon, suffix == "primary" ? "1" : "weapon_usp_silencer");
        _enabled = true;
        Announce($"Loadout weapon {target} {slot} = {weapon}");
    }

    private void HandleGrenade(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 4)
        {
            Reply(player, "用法: /loadout grenade <t/ct/all/0> <grenade-list|none>");
            return;
        }

        string target = info.GetArg(2).Trim().ToLowerInvariant();
        if (target == "0")
        {
            SetEnabled(false);
            Reply(player, "Loadout 已恢复默认。");
            return;
        }

        if (!TryGetTeams(target, out bool t, out bool ct))
        {
            Reply(player, "目标无效，请使用 t / ct / all / 0。");
            return;
        }

        string grenades = NormalizeNone(string.Join(' ', Enumerable.Range(3, info.ArgCount - 3).Select(info.GetArg)));
        if (t) Set("mp_t_default_grenades", grenades, "1");
        if (ct) Set("mp_ct_default_grenades", grenades, "1");
        _enabled = true;
        Announce($"Loadout grenade {target} = {grenades}");
    }

    private void HandleArmor(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 4)
        {
            Reply(player, "用法: /loadout armor <t/ct/all/0> <0|1|2>");
            return;
        }

        string target = info.GetArg(2).Trim().ToLowerInvariant();
        if (target == "0")
        {
            SetEnabled(false);
            Reply(player, "Loadout 已恢复默认。");
            return;
        }

        if (target != "all" && target != "0")
        {
            Reply(player, "mp_free_armor 是全局 CVar，只能使用 all 或 0。分边护甲耐久请用 /armor。");
            return;
        }

        if (!int.TryParse(info.GetArg(3), out int armor) || armor < 0 || armor > 2)
        {
            Reply(player, "护甲值无效，请使用 0 / 1 / 2。");
            return;
        }

        Set("mp_free_armor", armor.ToString(), "0");
        _enabled = true;
        Announce($"Loadout armor all = {armor}");
    }

    private void HandleBuy(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 4 || !float.TryParse(info.GetArg(2), out float buyTime))
        {
            Reply(player, "用法: /loadout buy <time> <anywhere 0/1>");
            return;
        }

        string anywhere = info.GetArg(3).Trim();
        if (anywhere != "0" && anywhere != "1")
        {
            Reply(player, "anywhere 只能是 0 或 1。");
            return;
        }

        Set("mp_buytime", buyTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "35");
        Set("mp_buy_anywhere", anywhere, "0");
        _enabled = true;
        Announce($"Loadout buy time={buyTime:0.###} anywhere={anywhere}");
    }

    private void HandleAllow(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 7)
        {
            Reply(player, "用法: /loadout allow <heavy> <pistols> <rifles> <smgs> <zeus>");
            return;
        }

        Set("mp_weapons_allow_heavy", info.GetArg(2).Trim(), "-1");
        Set("mp_weapons_allow_pistols", info.GetArg(3).Trim(), "-1");
        Set("mp_weapons_allow_rifles", info.GetArg(4).Trim(), "-1");
        Set("mp_weapons_allow_smgs", info.GetArg(5).Trim(), "-1");
        Set("mp_weapons_allow_zeus", info.GetArg(6).Trim(), "5");
        _enabled = true;
        Announce($"Loadout allow heavy={info.GetArg(2)} pistols={info.GetArg(3)} rifles={info.GetArg(4)} smgs={info.GetArg(5)} zeus={info.GetArg(6)}");
    }

    private void HandleHealthshot(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 4)
        {
            Reply(player, "用法: /loadout healthshot <t/ct/all/0> <heal|regen|immune|resist>");
            return;
        }

        string target = info.GetArg(2).Trim().ToLowerInvariant();
        string mode = info.GetArg(3).Trim().ToLowerInvariant();
        if (target == "0")
        {
            SetEnabled(false);
            Reply(player, "Loadout 已恢复默认。");
            return;
        }

        string selector = target switch
        {
            "t" => "@t",
            "ct" => "@ct",
            "all" => "@all",
            "0" => string.Empty,
            _ => string.Empty
        };

        if (selector.Length == 0)
        {
            Reply(player, "目标无效，请使用 t / ct / all。");
            return;
        }

        switch (mode)
        {
            case "heal":
                Set("healthshot_healthboost_time", "1", "1");
                Set("healthshot_health", "100", "50");
                Set("healthshot_healthboost_damage_multiplier", "1", "1");
                Set("healthshot_healthboost_speed_multiplier", "100", "1");
                break;
            case "regen":
                Set("healthshot_healthboost_time", "45", "1");
                Set("healthshot_health", "135", "50");
                Set("healthshot_healthboost_damage_multiplier", "1", "1");
                Set("healthshot_healthboost_speed_multiplier", "0.3", "1");
                break;
            case "immune":
                Set("healthshot_healthboost_time", "2.59", "1");
                Set("healthshot_health", "0", "50");
                Set("healthshot_healthboost_damage_multiplier", "0", "1");
                Set("healthshot_healthboost_speed_multiplier", "0", "1");
                break;
            case "resist":
                Set("healthshot_healthboost_time", "20", "1");
                Set("healthshot_health", "25", "50");
                Set("healthshot_healthboost_damage_multiplier", "0.8", "1");
                Set("healthshot_healthboost_speed_multiplier", "100", "1");
                break;
            default:
                Reply(player, "治疗针模式无效，请使用 heal / regen / immune / resist。");
                return;
        }

        Server.ExecuteCommand($"css_give {selector} healthshot");
        _enabled = true;
        Announce($"healthshot {target} {mode}");
    }

    private void Set(string name, string value, string fallbackDefault) =>
        _plugin.ManagedCvars.Set(Owner, name, value, fallbackDefault);

    private static string NormalizeNone(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? "1" : value;

    private static bool TryGetTeams(string target, out bool t, out bool ct)
    {
        t = target is "t" or "all";
        ct = target is "ct" or "all";
        if (target == "0")
        {
            t = false;
            ct = false;
            return true;
        }
        return t || ct;
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "/loadout weapon <t/ct/all/0> <primary|secondary> <weapon|none>");
        Reply(player, "/loadout grenade <t/ct/all/0> <grenade-list|none>");
        Reply(player, "/loadout armor <all/0> <0|1|2>  (全局)");
        Reply(player, "/loadout buy <time> <anywhere 0/1>  (全局)");
        Reply(player, "/loadout allow <heavy> <pistols> <rifles> <smgs> <zeus>  (全局)");
        Reply(player, "/loadout healthshot <t/ct/all/0> <heal|regen|immune|resist>");
        Reply(player, "/loadout reset");
    }

    private bool HasRoot(CCSPlayerController? player)
    {
        if (player == null || AdminManager.PlayerHasPermissions(player, "@css/root")) return true;
        CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
        return false;
    }

    private static bool IsResetArg(string arg) => arg is "0" or "off" or "disable" or "reset";

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player == null) Console.WriteLine($"[CaorenCup][Loadout] {message}");
        else CaorenCupUtils.PrintToChat(player, message);
    }

    private static void Announce(string message) =>
        CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[Loadout]{ChatColors.Default} {message}");
}
