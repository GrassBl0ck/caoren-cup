using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Gamemodes.Competitive;

public sealed class CaorenCupCompetitivePlugin : BasePlugin
{
    private readonly CompetitiveGameModeProvider _provider = new();

    public override string ModuleName => "CaorenCup Competitive";
    public override string ModuleVersion => "1.11.0";
    public override string ModuleAuthor => "Graslock + AI";

    public override void Load(bool hotReload)
    {
        AddCommand("css_competitive", "切换草人杯默认竞技模式: /competitive", OnCompetitiveCommand);
        CaorenCupGameModeRegistry.Register(_provider);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this,
            "competitive",
            "默认竞技模式 (Competitive)",
            "CompetitiveMode",
            () => $" {ChatColors.Green}/competitive{ChatColors.Default} : 选择并启动默认竞技模式（管理员）",
            () => "Competitive: Gamemode 已加载 | /competitive",
            () => "Competitive 由统一 Gamemode Runtime 管理：网页或管理员完成配置后进入短暂 Warmup，再自动结束热身开始正式比赛。",
            () => null,
            null));
    }

    public override void Unload(bool hotReload)
    {
        CaorenCupGameModeRegistry.Unregister(_provider);
        CaorenCupModuleRegistry.Unregister(this);
    }

    private static void OnCompetitiveCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupChat.PrintToChat(player, "你没有权限切换竞技模式。");
            return;
        }

        Server.ExecuteCommand("gamemode competitive");
        Server.ExecuteCommand("gamemode start");
        if (player is not null)
            CaorenCupChat.PrintToChat(player, "已选择 Competitive，正在进入 Warmup。");
        else
            Console.WriteLine("[CaorenCup] Competitive selected; entering Warmup.");
    }
}

public sealed class CompetitiveGameModeProvider : ICaorenCupGameModeProvider
{
    public GameModeDescriptor Descriptor { get; } = new(
        "competitive",
        "Competitive",
        "CaorenCupGamemode_Competitive",
        Array.Empty<string>(),
        null,
        15,
        new[]
        {
            new GameModeCvarDefinition("mp_freezetime", GameModeCvarType.Integer, "15", "0", "60"),
            new GameModeCvarDefinition("mp_roundtime_defuse", GameModeCvarType.Decimal, "1.92", "0.1", "15"),
            new GameModeCvarDefinition("mp_round_restart_delay", GameModeCvarType.Decimal, "7", "0", "30"),
            new GameModeCvarDefinition("mp_maxrounds", GameModeCvarType.Integer, "24", "1", "100"),
            new GameModeCvarDefinition("mp_halftime", GameModeCvarType.Boolean, "1"),
            new GameModeCvarDefinition("mp_warmuptime", GameModeCvarType.Integer, "15", "1", "300", WarmupOnly: true),
            new GameModeCvarDefinition("mp_warmup_pausetimer", GameModeCvarType.Boolean, "0", WarmupOnly: true),
            new GameModeCvarDefinition("mp_startmoney", GameModeCvarType.Integer, "800", "0", "65535"),
            new GameModeCvarDefinition("mp_buytime", GameModeCvarType.Decimal, "20", "0", "99999"),
            new GameModeCvarDefinition("mp_buy_anywhere", GameModeCvarType.Boolean, "0"),
        },
        ConfigFileName: "default_competitive.cfg");

    public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) =>
        Descriptor.SupportsMap(request.MapName)
            ? GameModeResult.Ok("Competitive 地图和配置检查通过。")
            : GameModeResult.Fail($"Competitive 不支持当前地图：{request.MapName}");

    public GameModeResult OnWarmupStarted(GameModeRuntimeContext context)
    {
        foreach (var command in CompetitiveModePlan.StartCommands)
            context.ExecuteServerCommand(command);
        return GameModeResult.Ok("Competitive Warmup 已启动。");
    }

    public GameModeResult OnLiveStarted(GameModeRuntimeContext context)
    {
        context.ExecuteServerCommand("mp_warmup_end");
        return GameModeResult.Ok("Competitive 正式比赛已开始。");
    }

    public GameModeResult OnStopped(GameModeRuntimeContext context) =>
        GameModeResult.Ok("Competitive 已停止。");
}

public static class CompetitiveModePlan
{
    public static IReadOnlyList<string> StartCommands { get; } =
    [
        "css_reset_plu"
    ];
}
