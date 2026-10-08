using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CaorenCup.Contracts;

namespace CaorenCup.Gamemodes.Rush;

/// <summary>官方 RUSH 模式的统一 Gamemode 启动入口。</summary>
public sealed class CaorenCupRushPlugin : BasePlugin
{
    private readonly RushGameModeProvider _provider = new();
    private bool _loaded;

    public override string ModuleName => "CaorenCup RUSH";
    public override string ModuleVersion => "1.11.0";
    public override string ModuleAuthor => "Graslock + AI";

    public override void Load(bool hotReload)
    {
        _loaded = true;
        AddCommand("css_rush", "RUSH 模式管理: /rush start", OnRushCommand);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventBeginNewMatch>(OnBeginNewMatch, HookMode.Post);
        CaorenCupGameModeRegistry.Register(_provider);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this,
            "rush",
            "RUSH Mode Launcher (RUSH 模式启动)",
            "RushFeature",
            () => $" {ChatColors.Green}/rush start{ChatColors.Default} : 选择并启动官方 RUSH 模式（管理员）",
            () => $"RUSH: Gamemode 已加载 | /rush start",
            () => "RUSH 由统一 Gamemode Runtime 管理，并按模式策略切换到 rush_001。",
            () => null,
            null));
    }

    public override void Unload(bool hotReload)
    {
        _loaded = false;
        CaorenCupGameModeRegistry.Unregister(_provider);
        CaorenCupModuleRegistry.Unregister(this);
    }

    private void OnRushCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            PrintToChat(player, "你没有权限启动 RUSH 模式。");
            return;
        }

        string action = info.ArgCount >= 2
            ? info.GetArg(1).Trim().ToLowerInvariant()
            : string.Empty;

        if (action != "start")
        {
            Reply(player, "用法：/rush start");
            return;
        }

        Server.ExecuteCommand("gamemode rush");
        Server.ExecuteCommand("gamemode start");
        Reply(player, "已选择 RUSH，正在进入 Warmup。");
    }

    private void OnMapStart(string mapName)
    {
        if (!_loaded || !RushModePlan.IsRushMap(mapName)) return;

        AddTimer(RushModePlan.ReapplyDelaySeconds, ReapplyRushConfig);
    }

    private void ReapplyRushConfig()
    {
        if (!RushModePlan.IsRushMap(Server.MapName)) return;

        foreach (string command in RushModePlan.PostMapStartCommands)
            Server.ExecuteCommand(command);

        Console.WriteLine("[CaorenCup] RUSH map loaded; official gamemode_rush.cfg reapplied.");
    }

    private HookResult OnBeginNewMatch(EventBeginNewMatch @event, GameEventInfo info)
    {
        if (!_loaded || !RushModePlan.IsRushMap(Server.MapName)) return HookResult.Continue;

        foreach (string command in RushModePlan.NewMatchCommands)
            Server.ExecuteCommand(command);

        Console.WriteLine("[CaorenCup] RUSH new match; restored mp_maxrounds 15.");
        return HookResult.Continue;
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player != null && player.IsValid)
            PrintToChat(player, message);
        else
            Console.WriteLine($"[CaorenCup] RUSH: {message}");
    }

    private static void PrintToChat(CCSPlayerController player, string message)
    {
        if (!player.IsValid) return;
        player.PrintToChat($" {ChatColors.Green}[草人杯]{ChatColors.Default} {message}");
    }
}

public sealed class RushGameModeProvider : ICaorenCupGameModeProvider
{
    public GameModeDescriptor Descriptor { get; } = new(
        "rush",
        "RUSH",
        "CaorenCupGamemode_RUSH",
        new[] { RushModePlan.MapName },
        RushModePlan.MapName,
        15,
        new[]
        {
            new GameModeCvarDefinition("mp_freezetime", GameModeCvarType.Integer, "5", "0", "60"),
            new GameModeCvarDefinition("mp_roundtime_defuse", GameModeCvarType.Decimal, "1.5", "0.1", "15"),
            new GameModeCvarDefinition("mp_maxrounds", GameModeCvarType.Integer, "15", "1", "100"),
            new GameModeCvarDefinition("mp_halftime", GameModeCvarType.Boolean, "1"),
            new GameModeCvarDefinition("mp_warmuptime", GameModeCvarType.Integer, "15", "1", "300", WarmupOnly: true),
            new GameModeCvarDefinition("mp_warmup_pausetimer", GameModeCvarType.Boolean, "0", WarmupOnly: true),
            new GameModeCvarDefinition("mp_autoteambalance", GameModeCvarType.Boolean, "0"),
            new GameModeCvarDefinition("mp_limitteams", GameModeCvarType.Integer, "10", "0", "10"),
        },
        ConfigFileName: "default_rush.cfg");

    public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context)
    {
        if (string.IsNullOrWhiteSpace(request.MapName))
            return GameModeResult.Fail("RUSH 需要明确地图。");
        return GameModeResult.Ok("RUSH 地图和配置检查通过。");
    }

    public GameModeResult OnWarmupStarted(GameModeRuntimeContext context)
    {
        context.ExecuteServerCommand("css_reset_plu");
        context.ExecuteServerCommand("game_type 0");
        context.ExecuteServerCommand("game_mode 6");
        if (!RushModePlan.IsRushMap(context.GetCurrentMap()))
            context.ExecuteServerCommand($"changelevel {RushModePlan.MapName}");
        return GameModeResult.Ok("RUSH Warmup 已启动。");
    }

    public GameModeResult OnLiveStarted(GameModeRuntimeContext context)
    {
        context.ExecuteServerCommand("mp_warmup_end");
        return GameModeResult.Ok("RUSH 正式比赛已开始。");
    }

    public GameModeResult OnStopped(GameModeRuntimeContext context) =>
        GameModeResult.Ok("RUSH 已停止。");
}

public static class RushModePlan
{
    public const string MapName = "rush_001";
    public const float ReapplyDelaySeconds = 2.0f;

    public static IReadOnlyList<string> StartCommands { get; } = new[]
    {
        "css_reset_plu",
        "game_type 0",
        "game_mode 6",
        "changelevel rush_001",
    };

    public static IReadOnlyList<string> PostMapStartCommands { get; } = new[]
    {
        "exec grass/default_common.cfg",
        "exec grass/default_rush.cfg",
        "exec gamemode_rush.cfg",
    };

    public static IReadOnlyList<string> NewMatchCommands { get; } = new[]
    {
        "mp_maxrounds 15",
    };

    public static bool IsRushMap(string? mapName) =>
        string.Equals(mapName?.Trim(), MapName, StringComparison.OrdinalIgnoreCase);
}
