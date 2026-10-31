using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

public class MovementRulesFeature : ICaorenFeature
{
    public string FeatureName => "Move Rules (/move 全局移动规则)";

    private const string Owner = "Move";
    private CaorenCupPlugin _plugin = null!;
    private bool _enabled;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        plugin.AddCommand("css_move", "全局移动规则: /move gravity|jump|fall|friction|speed|bhop|reset", OnCommand);
        plugin.AddCommand("move", "全局移动规则: /move gravity|jump|fall|friction|speed|bhop|reset", OnCommand);
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
        $" {ChatColors.Green}/move gravity|jump|fall|friction|speed|bhop|reset{ChatColors.Default} : 全局移动/物理 CVar";

    public string GetStatusInfo() =>
        $"Move: {(_enabled ? $"{ChatColors.Green}已启用" : $"{ChatColors.Red}已禁用")}{ChatColors.Default}";

    public string? GetPublicConfigInfo() => _enabled ? "[移动规则] 已修改全局移动或物理规则。" : null;

    public string GetFeatureDescription() =>
        "/move 是全局移动规则模块，底层修改服务器 CVar，不按阵营分边。\n" +
        "/move gravity <value>\n" +
        "/move jump <value>\n" +
        "/move fall <value>\n" +
        "/move friction <value>\n" +
        "/move speed <value>\n" +
        "/move bhop <0/1>\n" +
        "/move reset 恢复本模块托管的 CVar。";

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
            Reply(player, "Move 已恢复默认。");
            return;
        }

        if (info.ArgCount < 3)
        {
            PrintUsage(player);
            return;
        }

        string value = info.GetArg(2).Trim();
        switch (action)
        {
            case "gravity":
                Set("sv_gravity", value, "800");
                break;
            case "jump":
                Set("sv_jump_impulse", value, "301.9933");
                break;
            case "fall":
                Set("sv_falldamage_scale", value, "1");
                break;
            case "friction":
                Set("sv_friction", value, "5.2");
                break;
            case "speed":
                Set("sv_maxspeed", value, "320");
                break;
            case "bhop":
                if (value != "0" && value != "1")
                {
                    Reply(player, "bhop 只能是 0 或 1。");
                    return;
                }
                Set("sv_enablebunnyhopping", value, "0");
                Set("sv_autobunnyhopping", value, "0");
                Set("sv_staminajumpcost", value == "1" ? "0" : "0.08", "0.08");
                Set("sv_staminalandcost", value == "1" ? "0" : "0.05", "0.05");
                break;
            default:
                PrintUsage(player);
                return;
        }

        _enabled = true;
        Announce($"{action} = {value} (全局)");
    }

    private void Set(string name, string value, string fallbackDefault) =>
        _plugin.ManagedCvars.Set(Owner, name, value, fallbackDefault);

    private static bool IsResetArg(string arg) => arg is "0" or "off" or "disable" or "reset";

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "/move gravity <value>  (全局)");
        Reply(player, "/move jump <value>  (全局)");
        Reply(player, "/move fall <value>  (全局)");
        Reply(player, "/move friction <value>  (全局)");
        Reply(player, "/move speed <value>  (全局)");
        Reply(player, "/move bhop <0/1>  (全局)");
        Reply(player, "/move reset");
    }

    private bool HasRoot(CCSPlayerController? player)
    {
        if (player == null || AdminManager.PlayerHasPermissions(player, "@css/root")) return true;
        CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
        return false;
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player == null) Console.WriteLine($"[CaorenCup][Move] {message}");
        else CaorenCupUtils.PrintToChat(player, message);
    }

    private static void Announce(string message) =>
        CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[Move]{ChatColors.Default} {message}");
}
