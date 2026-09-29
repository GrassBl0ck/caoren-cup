using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Core;

public sealed partial class CaorenCupCorePlugin
{
    private bool _allowPlayerNoclip;

    private void RegisterCoreCommands()
    {
        AddCommand("helpall", "查看所有指令入口", OnCommandHelp);
        AddCommand("help_plu", "查看插件列表", OnCommandHelpPlu);
        AddCommand("status", "查看功能状态", OnCommandStatus);
        AddCommand("reset_plu", "重置", OnCommandReset);
        AddCommand("save_plu", "保存", OnCommandSave);
        AddCommand("rules", "显示当前服务器规则", OnCommandRules);
        AddCommand("hpcap", "设置模块血量全局上下限", OnCommandHpCap);
        AddCommand("css_hpcap", "设置模块血量全局上下限", OnCommandHpCap);
        AddCommand("info", "查看模块玩法说明", OnCommandInfo);
        AddCommand("info_cast", "向全服广播玩法说明", OnCommandInfoCast);
        AddCommand("sv_noclip", "控制玩家 noclip", OnCommandSvNoclip);
        AddCommandListener("noclip", OnNoclipCommand, HookMode.Pre);
        RegisterEventHandler<EventPlayerChat>(OnTeamChatForSpectators, HookMode.Post);
    }

    private static bool HasRoot(CCSPlayerController? player) =>
        player is null || AdminManager.PlayerHasPermissions(player, "@css/root");

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player is null) Console.WriteLine($"[CaorenCup] {message}");
        else CaorenCupChat.PrintToChat(player, message);
    }

    private void OnCommandHelp(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null) return;
        Reply(player, "=== 草人杯 总指令菜单 ===");
        Reply(player, $"/help_plu : 查看所有功能插件列表");
        Reply(player, "/status : 查看状态");
        Reply(player, "/save_plu : 保存配置");
        Reply(player, "/reset_plu : 一键重置");
        Reply(player, "/hpcap <min> <max> : 设置模块血量全局上下限");
        Reply(player, "提示：配置类命令的数值参数可用 - 表示保持当前值。");
        Reply(player, "/rules : 查看草人杯当前服务器规则");
        Reply(player, "/info <模块名> : 查看指定玩法说明");
        Reply(player, "/info_cast <模块名> : 向全服广播玩法说明（管理员）");
        Reply(player, "/sv_noclip <1|0|status> : 控制或查看 noclip（管理员）");
        Reply(player, "/cclogin : 获取草人杯网页登录码");
        Reply(player, "/ccstate : 查看网页指挥台连接状态");
        Reply(player, "/ccsnapshot : 手动推送一次网页比赛快照");
        Reply(player, "/duel help : 查看游戏内单挑全部命令");
        if (HasRoot(player))
        {
            Reply(player, "/notice <范围> <内容> : 向玩家发送醒目提示（管理员）");
            Reply(player, "/lobbyreminder on|off|1|0 : 开启或关闭大厅验证提醒（管理员）");
        }
    }

    private void OnCommandHelpPlu(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null) return;
        Reply(player, "=== 功能模块列表 ===");
        foreach (var module in CaorenCupModuleRegistry.Snapshot())
            player.PrintToChat(CaorenCupChat.FormatHelpMenuLine(module.Help()));
    }

    private void OnCommandStatus(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null) return;
        if (info.ArgCount > 1)
        {
            var module = CaorenCupModuleRegistry.Find(info.GetArg(1));
            if (module is null) Reply(player, "未找到该模块。");
            else
            {
                Reply(player, $"=== {module.FeatureName} 详细状态 ===");
                Reply(player, module.Status());
            }
            return;
        }

        Reply(player, "=== 全局状态 (/status <模块> 详情) ===");
        Reply(player, Config.HpCap.Enabled
            ? $"HpCap: {ChatColors.Green}启用{ChatColors.Default} | 范围:{Config.HpCap.Min}-{Config.HpCap.Max}"
            : $"HpCap: {ChatColors.Red}已禁用{ChatColors.Default}");
        foreach (var module in CaorenCupModuleRegistry.Snapshot())
            Reply(player, module.Status().Split('|')[0]);
    }

    private void OnCommandInfo(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null) return;
        if (info.ArgCount < 2)
        {
            Reply(player, "用法: /info <模块名>");
            Reply(player, "例如: /info bomb (查看BombQuiz玩法)");
            Reply(player, "可用模块: " + string.Join(", ",
                CaorenCupModuleRegistry.Snapshot().Select(m => m.TypeName.Replace("Feature", ""))));
            return;
        }

        var module = CaorenCupModuleRegistry.Find(info.GetArg(1));
        if (module is null) { Reply(player, "未找到该模块。"); return; }
        Reply(player, $"\x10========== {module.FeatureName} 玩法说明 ==========\x01");
        foreach (string line in module.Description().Split('\n'))
            Reply(player, line);
        Reply(player, "\x10============================================\x01");
    }

    private void OnCommandInfoCast(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player)) { Reply(player, "你没有权限执行全服广播。"); return; }
        if (info.ArgCount < 2) { if (player is not null) Reply(player, "用法: /info_cast <模块名>"); return; }
        var module = CaorenCupModuleRegistry.Find(info.GetArg(1));
        if (module is null) { if (player is not null) Reply(player, "未找到该模块。"); return; }
        CaorenCupChat.PrintToChatAll($" \x10========== [草人杯] {module.FeatureName} 玩法介绍 ==========\x01");
        foreach (string line in module.Description().Split('\n'))
            CaorenCupChat.PrintToChatAll(line);
        CaorenCupChat.PrintToChatAll(" \x10==================================================\x01");
    }

    private void OnCommandRules(CCSPlayerController? player, CommandInfo info)
    {
        var rules = CaorenCupModuleRegistry.Snapshot()
            .Select(module => module.PublicConfig())
            .Where(rule => !string.IsNullOrEmpty(rule))
            .ToList();
        if (Config.HpCap.Enabled)
            rules.Add($"全局血量保护: 模块回血最高到 {Config.HpCap.Max} HP，模块扣血最低到 {Config.HpCap.Min} HP");
        if (rules.Count == 0)
        {
            CaorenCupChat.PrintToChatAll(" [草人杯] 当前服务器运行默认竞技规则，未开启特殊修改。");
            return;
        }
        CaorenCupChat.PrintToChatAll(" \x10========== [草人杯] 当前特殊规则 ==========\x01");
        foreach (string? rule in rules)
            CaorenCupChat.PrintToChatAll($" {ChatColors.Green}>{ChatColors.Default} {rule}");
        CaorenCupChat.PrintToChatAll(" \x10===========================================\x01");
    }

    [ConsoleCommand("css_reset_plu", "重置")]
    public void OnConsoleReset(CCSPlayerController? player, CommandInfo info)
    {
        if (!ResetCommandPolicy.Run(player is null, HasRoot(player), PerformReset)) { Reply(player, "无权操作。"); return; }
        if (player is null) Console.WriteLine("[CaorenCup] 已重置。");
    }

    private void OnCommandReset(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null && !ResetCommandPolicy.Run(false, HasRoot(player), PerformReset))
            Reply(player, "无权操作。");
    }

    private void PerformReset()
    {
        ManagedCvars.ResetAll();
        foreach (var module in CaorenCupModuleRegistry.Snapshot())
            module.Reset?.Invoke();
        Config.HpCap.Enabled = false;
        CaorenCupChat.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} 所有功能已重置为竞技状态。");
        SaveConfig();
    }

    private void OnCommandHpCap(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player)) { Reply(player, "无权操作。"); return; }
        if (info.ArgCount == 1)
        {
            string state = Config.HpCap.Enabled
                ? $"已启用：模块回血最高 {Config.HpCap.Max} HP，模块扣血最低 {Config.HpCap.Min} HP"
                : "已禁用";
            Reply(player, $"当前 /hpcap 状态：{state}");
            if (player is not null) Reply(player, "用法: /hpcap <min> <max>，例如 /hpcap 1 150；/hpcap 0 可禁用。");
            return;
        }

        string arg = info.GetArg(1).ToLowerInvariant();
        if (arg is "0" or "off" or "disable")
        {
            Config.HpCap.Enabled = false;
            SaveConfig();
            CaorenCupChat.PrintToChatAll($" {ChatColors.Red}全局血量保护已禁用。{ChatColors.Default}");
            return;
        }
        if (info.ArgCount < 3)
        {
            if (player is not null) Reply(player, "用法: /hpcap <min> <max>，例如 /hpcap 1 150。输入 /hpcap 0 禁用。");
            return;
        }
        if (!int.TryParse(info.GetArg(1), out int min) || !int.TryParse(info.GetArg(2), out int max))
        {
            if (player is not null) Reply(player, "min/max 必须是整数。");
            return;
        }
        if (min < 0 || max < 1 || max < min)
        {
            if (player is not null) Reply(player, "参数非法：要求 min >= 0，max >= 1，且 max >= min。");
            return;
        }
        Config.HpCap.Enabled = true;
        Config.HpCap.Min = min;
        Config.HpCap.Max = max;
        SaveConfig();
        CaorenCupChat.PrintToChatAll($" {ChatColors.Green}全局血量保护已启用：{ChatColors.Default}模块回血最高到 {ChatColors.Green}{max}{ChatColors.Default} HP，模块扣血最低到 {ChatColors.Green}{min}{ChatColors.Default} HP。");
    }

    private void OnCommandSave(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player)) { Reply(player, "无权操作。"); return; }
        SaveConfig();
        if (player is not null) Reply(player, "配置已强制保存到插件目录。");
    }

    private HookResult OnNoclipCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || _allowPlayerNoclip) return HookResult.Continue;
        Reply(player, "服务器已禁用 noclip，避免误触飞行。");
        Console.WriteLine($"[CaorenCup] Blocked noclip from player {player.PlayerName} (slot {player.Slot}).");
        return HookResult.Handled;
    }

    private void OnCommandSvNoclip(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player)) { Reply(player, "你没有权限修改 noclip 开关。"); return; }
        if (info.ArgCount < 2) { ReplyNoclipStatus(player); return; }
        switch (info.GetArg(1).Trim().ToLowerInvariant())
        {
            case "1": case "on": case "true": case "enable":
                _allowPlayerNoclip = true;
                ReplyNoclipChanged(player);
                break;
            case "0": case "off": case "false": case "disable":
                _allowPlayerNoclip = false;
                ReplyNoclipChanged(player);
                break;
            case "status": ReplyNoclipStatus(player); break;
            default: Reply(player, "[草人杯] 用法：/sv_noclip <1允许/0禁止/status>"); break;
        }
    }

    private void ReplyNoclipChanged(CCSPlayerController? player)
    {
        string message = $"[草人杯] 玩家 noclip 已设置为：{(_allowPlayerNoclip ? "允许" : "禁止")}。";
        if (player is null) Console.WriteLine(message);
        else CaorenCupChat.PrintToChatAll(message);
    }

    private void ReplyNoclipStatus(CCSPlayerController? player) =>
        Reply(player, $"[草人杯] 当前玩家 noclip：{(_allowPlayerNoclip ? "允许" : "禁止")}。用法：/sv_noclip <1允许/0禁止/status>");

    private HookResult OnTeamChatForSpectators(EventPlayerChat @event, GameEventInfo info)
    {
        if (!@event.Teamonly) return HookResult.Continue;
        var speaker = Utilities.GetPlayerFromUserid(@event.Userid);
        if (speaker is null || !speaker.IsValid || speaker.IsBot ||
            speaker.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            return HookResult.Continue;
        string message = new string((@event.Text ?? string.Empty).Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (message.Length == 0 || message[0] is '.' or '!' or '/')
            return HookResult.Continue;
        string name = new string(speaker.PlayerName.Where(ch => !char.IsControl(ch)).ToArray());
        string side = speaker.Team == CsTeam.Terrorist ? "T" : "CT";
        foreach (var spectator in Utilities.GetPlayers())
        {
            if (spectator is null || !spectator.IsValid || spectator.IsBot || spectator.IsHLTV ||
                spectator.Team != CsTeam.Spectator) continue;
            spectator.PrintToChat($" [队伍 {side}] {name}: {message}");
        }
        return HookResult.Continue;
    }
}
