using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CaorenCup.Contracts;

namespace CaorenCup.Gamemodes.Rush;

/// <summary>官方 RUSH 模式的最小启动入口。</summary>
public sealed class CaorenCupRushPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup RUSH";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";

    private bool _loaded;

    public override void Load(bool hotReload)
    {
        _loaded = true;
        AddCommand("css_rush", "RUSH 模式管理: /rush start", OnRushCommand);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventBeginNewMatch>(OnBeginNewMatch, HookMode.Post);
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this,
            "rush",
            "RUSH Mode Launcher (RUSH 模式启动)",
            "RushFeature",
            () => $" {ChatColors.Green}/rush start{ChatColors.Default} : 直接切换到官方 RUSH 模式（管理员）",
            () => $"RUSH: {ChatColors.Green}启动命令可用{ChatColors.Default} | /rush start",
            () => " [RUSH] 官方 3v3 RUSH 模式启动入口。\n 管理员输入 /rush start 后，服务器会直接切换到 rush_001。",
            () => null,
            null));
    }

    public override void Unload(bool hotReload)
    {
        _loaded = false;
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

        Reply(player, "正在切换到 RUSH 模式……");
        foreach (string command in RushModePlan.StartCommands)
        {
            Server.ExecuteCommand(command);
        }
    }

    private void OnMapStart(string mapName)
    {
        if (!_loaded || !RushModePlan.IsRushMap(mapName)) return;

        // MatchZy 的 Sleep 模式会在换图后延迟执行 sleep.cfg。
        // 等它完成后再恢复官方 RUSH 配置，避免经济、冻结时间和开场动画被覆盖。
        AddTimer(RushModePlan.ReapplyDelaySeconds, ReapplyRushConfig);
    }

    private void ReapplyRushConfig()
    {
        if (!RushModePlan.IsRushMap(Server.MapName)) return;

        foreach (string command in RushModePlan.PostMapStartCommands)
        {
            Server.ExecuteCommand(command);
        }

        Console.WriteLine("[CaorenCup] RUSH map loaded; official gamemode_rush.cfg reapplied after MatchZy Sleep.");
    }

    private HookResult OnBeginNewMatch(EventBeginNewMatch @event, GameEventInfo info)
    {
        if (!_loaded || !RushModePlan.IsRushMap(Server.MapName)) return HookResult.Continue;

        // The map lowers mp_maxrounds after an early castle win. Restore the normal
        // 15-round limit when the next match starts on the same map.
        foreach (string command in RushModePlan.NewMatchCommands)
        {
            Server.ExecuteCommand(command);
        }

        Console.WriteLine("[CaorenCup] RUSH new match; restored mp_maxrounds 15.");
        return HookResult.Continue;
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player != null && player.IsValid)
        {
            PrintToChat(player, message);
        }
        else
        {
            Console.WriteLine($"[CaorenCup] RUSH: {message}");
        }
    }

    private static void PrintToChat(CCSPlayerController player, string message)
    {
        if (!player.IsValid) return;
        string body = message.Replace("[草人杯]", string.Empty, StringComparison.Ordinal).Trim();
        player.PrintToChat($" {ChatColors.Green}[草人杯]{ChatColors.Default} {body}");
    }
}

/// <summary>可独立测试的 RUSH 命令顺序与地图判定。</summary>
public static class RushModePlan
{
    public const string MapName = "rush_001";
    public const float ReapplyDelaySeconds = 2.0f;

    public static IReadOnlyList<string> StartCommands { get; } = new[]
    {
        "css_reset_plu",
        "css_sleep",
        "game_type 0",
        "game_mode 6",
        $"changelevel {MapName}",
    };

    public static IReadOnlyList<string> PostMapStartCommands { get; } = new[]
    {
        "exec gamemode_rush.cfg",
    };

    public static IReadOnlyList<string> NewMatchCommands { get; } = new[]
    {
        "mp_maxrounds 15",
    };

    public static bool IsRushMap(string? mapName) =>
        string.Equals(mapName?.Trim(), MapName, StringComparison.OrdinalIgnoreCase);
}
