using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Competitive;

public sealed class CaorenCupCompetitivePlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Competitive";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";

    public override void Load(bool hotReload)
    {
        AddCommand("css_competitive", "切换草人杯默认竞技模式: /competitive", OnCompetitiveCommand);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this,
            "competitive",
            "默认竞技模式 (Competitive)",
            "CompetitiveMode",
            () => $" {ChatColors.Green}/competitive{ChatColors.Default} : 切换默认竞技模式（管理员）",
            () => "Competitive: 切换命令可用 | /competitive",
            () => "管理员使用 /competitive：先重置草人杯玩法，再退出 MatchZy 练习模式，最后执行 grass/default 配置。",
            () => null,
            null));
    }

    public override void Unload(bool hotReload) => CaorenCupModuleRegistry.Unregister(this);

    private static void OnCompetitiveCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupChat.PrintToChat(player, "你没有权限切换竞技模式。");
            return;
        }

        if (player is not null)
            CaorenCupChat.PrintToChat(player, "正在切换到默认竞技模式……");
        else
            Console.WriteLine("[CaorenCup] 正在切换到默认竞技模式……");

        foreach (string command in CompetitiveModePlan.StartCommands)
            Server.ExecuteCommand(command);
    }
}

public static class CompetitiveModePlan
{
    public static IReadOnlyList<string> StartCommands { get; } =
    [
        "css_reset_plu",
        "css_exitprac",
        "exec grass/default"
    ];
}
