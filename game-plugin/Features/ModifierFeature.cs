using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public class ModifierFeature : ICaorenFeature
{
    public string FeatureName => "Modifier (/mod 全局 CVar 规则)";

    private const string Owner = "Modifier";
    private CaorenCupPlugin _plugin = null!;
    private bool _enabled;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        plugin.AddCommand("css_mod", "规则 CVar: /mod hit-scale|headshot|vampire|selfdamage|he-damage|reset", OnCommand);
        plugin.AddCommand("mod", "规则 CVar: /mod hit-scale|headshot|vampire|selfdamage|he-damage|reset", OnCommand);
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
        $" {ChatColors.Green}/mod hit-scale|headshot|vampire|selfdamage|he-damage|reset{ChatColors.Default} : 爆头/吸血/自伤/高爆 CVar";

    public string GetStatusInfo() =>
        $"Modifier: {(_enabled ? $"{ChatColors.Green}已启用" : $"{ChatColors.Red}已禁用")}{ChatColors.Default}";

    public string? GetPublicConfigInfo() =>
        _enabled ? "[全局 CVar 规则] 已修改命中部位、爆头限定、吸血、自伤或高爆伤害。" : null;

    public string GetFeatureDescription() =>
        "/mod hit-scale <t/ct/all/0> <headScale> <bodyScale>  (命中部位 CVar)\n" +
        "/mod headshot <0/1>  (全局)\n" +
        "/mod vampire <amount>  (全局)\n" +
        "/mod selfdamage <amount>  (全局)\n" +
        "/mod he-damage <scale>  (全局，只改高爆伤害倍率)\n" +
        "/mod reset 恢复本模块托管的 CVar。\n" +
        "普通受伤倍率/锁血请用 /dmg，持续扣血请用 /bleed，火焰伤害请用 /fh。";

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
            Reply(player, "Modifier 已恢复默认。");
            return;
        }

        switch (action)
        {
            case "hit-scale":
            case "hitscale":
                HandleHitScale(player, info);
                return;
            case "damage":
                Reply(player, "/mod damage 已停用，避免和 /dmg 重叠。命中部位倍率请用 /mod hit-scale，普通受伤倍率请用 /dmg。");
                return;
            case "headshot":
                HandleSingle(player, info, "mp_damage_headshot_only", "false", "0/1");
                return;
            case "vampire":
                HandleSingle(player, info, "mp_damage_vampiric_amount", "0", "amount");
                return;
            case "drain":
                Reply(player, "/mod drain 已停用，避免和 /bleed 重叠。请使用 /bleed <t/ct/all/0> <秒> <负数扣血>。");
                return;
            case "selfdamage":
                HandleSingle(player, info, "mp_weapon_self_inflict_amount", "0", "amount");
                return;
            case "he-damage":
            case "hedamage":
                HandleHeDamage(player, info);
                return;
            case "grenade-damage":
                HandleLegacyGrenadeDamage(player, info);
                return;
            default:
                PrintUsage(player);
                return;
        }
    }

    private void HandleHitScale(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 5 || !TryParseFloat(info.GetArg(3), out float head) || !TryParseFloat(info.GetArg(4), out float body))
        {
            Reply(player, "用法: /mod hit-scale <t/ct/all/0> <headScale> <bodyScale>");
            return;
        }

        string target = info.GetArg(2).Trim().ToLowerInvariant();
        if (target == "0")
        {
            SetEnabled(false);
            Reply(player, "Modifier 已恢复默认。");
            return;
        }

        if (!TryGetTeams(target, out bool t, out bool ct))
        {
            Reply(player, "目标无效，请使用 t / ct / all / 0。");
            return;
        }

        if (t)
        {
            Set("mp_damage_scale_t_head", Format(head), "1");
            Set("mp_damage_scale_t_body", Format(body), "1");
        }

        if (ct)
        {
            Set("mp_damage_scale_ct_head", Format(head), "1");
            Set("mp_damage_scale_ct_body", Format(body), "1");
        }

        _enabled = true;
        Announce($"hit-scale {target} head={head:0.###} body={body:0.###}");
    }

    private void HandleSingle(CCSPlayerController? player, CommandInfo info, string cvar, string fallback, string usage)
    {
        if (info.ArgCount < 3)
        {
            Reply(player, $"用法: /mod {info.GetArg(1)} <{usage}>");
            return;
        }

        string value = info.GetArg(2).Trim();
        Set(cvar, value, fallback);
        _enabled = true;
        Announce($"{cvar} = {value}");
    }

    private void HandleHeDamage(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 3 || !TryParseFloat(info.GetArg(2), out float he))
        {
            Reply(player, "用法: /mod he-damage <scale>");
            return;
        }

        Set("sv_hegrenade_damage_multiplier", Format(he), "1");
        _enabled = true;
        Announce($"HE damage scale = {he:0.###}");
    }

    private void HandleLegacyGrenadeDamage(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount < 3 || !TryParseFloat(info.GetArg(2), out float he))
        {
            Reply(player, "用法: /mod he-damage <scale>。火焰伤害请使用 /fh。");
            return;
        }

        Set("sv_hegrenade_damage_multiplier", Format(he), "1");
        _enabled = true;
        Reply(player, "已只应用 HE 伤害倍率；火焰伤害请使用 /fh，避免与 FireHeal 模块重叠。");
        Announce($"HE damage scale = {he:0.###}");
    }

    private void Set(string name, string value, string fallbackDefault) =>
        _plugin.ManagedCvars.Set(Owner, name, value, fallbackDefault);

    private static bool TryGetTeams(string target, out bool t, out bool ct)
    {
        t = target is "t" or "all";
        ct = target is "ct" or "all";
        return t || ct;
    }

    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static string Format(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsResetArg(string arg) => arg is "0" or "off" or "disable" or "reset";

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "/mod hit-scale <t/ct/all/0> <headScale> <bodyScale>  (命中部位 CVar)");
        Reply(player, "/mod headshot <0/1>  (全局)");
        Reply(player, "/mod vampire <amount>  (全局)");
        Reply(player, "/mod selfdamage <amount>  (全局)");
        Reply(player, "/mod he-damage <scale>  (全局)");
        Reply(player, "/mod reset");
        Reply(player, "普通受伤倍率/锁血用 /dmg；持续扣血用 /bleed；火焰伤害用 /fh。");
    }

    private bool HasRoot(CCSPlayerController? player)
    {
        if (player == null || AdminManager.PlayerHasPermissions(player, "@css/root")) return true;
        CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
        return false;
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player == null) Console.WriteLine($"[CaorenCup][Mod] {message}");
        else CaorenCupUtils.PrintToChat(player, message);
    }

    private static void Announce(string message) =>
        CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[Mod]{ChatColors.Default} {message}");
}
