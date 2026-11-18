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

    private void RegisterMatchZyPracticeStateListeners()
    {
        foreach (var command in new[] { "prac", "css_prac", "sleep", "css_sleep", "exitprac", "css_exitprac", "noprac", "css_noprac" })
            AddCommandListener(command, OnMatchZyPracticeStateCommand, HookMode.Pre);
    }

    private void UnregisterMatchZyPracticeStateListeners()
    {
        foreach (var command in new[] { "prac", "css_prac", "sleep", "css_sleep", "exitprac", "css_exitprac", "noprac", "css_noprac" })
            RemoveCommandListener(command, OnMatchZyPracticeStateCommand, HookMode.Pre);
    }

    private HookResult OnMatchZyPracticeStateCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (CaorenCupMatchAuthority.BridgeCompatibilityCommandsEnabled) return HookResult.Continue;
        var name = command.GetCommandString.TrimStart('!', '/').ToLowerInvariant();
        if (name.Contains("exitprac", StringComparison.Ordinal) ||
            name.Contains("noprac", StringComparison.Ordinal) ||
            name.Contains("sleep", StringComparison.Ordinal))
            CaorenCupPracticeState.EnterSleep();
        else if (name.Contains("prac", StringComparison.Ordinal))
            CaorenCupPracticeState.EnterPractice();
        return HookResult.Continue;
    }

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
        AddCommand("css_noclip", "控制玩家 noclip", OnCommandNoclip);
        AddCommand("gamemode", "统一切换和控制游戏模式", OnGameModeCommand);
        AddCommand("gm", "统一切换和控制游戏模式（简写）", OnGameModeCommand);
        AddCommand("gamemode_cvar", "设置当前游戏模式 CVar", OnGameModeCvarCommand);
        AddCommand("ccrestore", "恢复指定回合的自动备份。用法：/ccrestore <回合数>", OnRestoreRoundCommand);
        AddCommand("css_ccrestore", "恢复指定回合的自动备份。用法：/ccrestore <回合数>", OnRestoreRoundCommand);
        AddCommandListener("noclip", OnNoclipCommand, HookMode.Pre);
        RegisterEventHandler<EventPlayerChat>(OnTeamChatForSpectators, HookMode.Post);
    }

    private static bool HasRoot(CCSPlayerController? player) =>
        player is null || AdminManager.PlayerHasPermissions(player, "@css/root");

    private static void Reply(CCSPlayerController? player, string message)
    {
        CaorenCupChat.Reply(player, message);
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
        Reply(player, "=== Gamemode 模式 ===");
        Reply(player, "/gamemode (/gm) : 管理游戏模式（管理员）");
        Reply(player, "/ccrestore <回合数> : 恢复当前模式指定回合的自动备份（管理员）");
        Reply(player, "/pause (/p)、/unpause (/up) : 管理员暂停/恢复；/tac、/tech : 玩家战术/技术暂停（草人杯正式比赛）");
        Reply(player, "=== 杂项 ===");
        Reply(player, "/ccbind : 获取草人杯 CS 游戏身份绑定码");
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
            CaorenCupChat.PrintToChat(player, CaorenCupChat.StripChatColors(module.Help()));
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
            Reply(player, " [草人杯] 当前服务器运行默认竞技规则，未开启特殊修改。");
            return;
        }
        Reply(player, " \x10========== [草人杯] 当前特殊规则 ==========\x01");
        foreach (string? rule in rules)
            Reply(player, $" {ChatColors.Green}>{ChatColors.Default} {rule}");
        Reply(player, " \x10===========================================\x01");
    }

    [ConsoleCommand("css_reset_plu", "重置")]
    public void OnConsoleReset(CCSPlayerController? player, CommandInfo info)
    {
        if (!ResetCommandPolicy.Run(player is null, HasRoot(player), PerformReset)) { Reply(player, "无权操作。"); return; }
    }

    private void OnCommandReset(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null && !ResetCommandPolicy.Run(false, HasRoot(player), PerformReset))
            Reply(player, "无权操作。");
    }

    private void PerformReset()
    {
        ManagedCvars.ResetAll();
        var failed = new List<string>();
        foreach (var module in CaorenCupModuleRegistry.Snapshot())
        {
            try
            {
                module.Reset?.Invoke();
            }
            catch (Exception ex)
            {
                failed.Add(module.FeatureName);
                CaorenCupChat.PrintToConsole($"模块 {module.Key} 重置暂未完成：{ex.Message}");
            }
        }
        Config.HpCap.Enabled = false;
        CaorenCupChat.PrintToChatAll(failed.Count == 0
            ? "所有功能已重置为竞技状态。"
            : $"已执行功能重置，但以下模块尚未完整恢复：{string.Join("、", failed)}。请管理员检查日志。");
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
        Reply(player, "配置已强制保存到插件目录。");
    }

    private HookResult OnNoclipCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || _allowPlayerNoclip) return HookResult.Continue;
        // Closing the entry must never prevent a player who is already flying from exiting.
        var pawn = player.PlayerPawn.Value;
        if (pawn is { IsValid: true } && (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP || pawn.ActualMoveType == MoveType_t.MOVETYPE_NOCLIP))
            return HookResult.Continue;
        Reply(player, "服务器已禁用 noclip，避免误触飞行。");
        Console.WriteLine($"[草人杯] Blocked noclip from player {player.PlayerName} (slot {player.Slot}).");
        return HookResult.Handled;
    }

    private void OnCommandNoclip(CCSPlayerController? player, CommandInfo info)
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
                var (restored, failed) = RestoreNoclipPlayers();
                CaorenCupChat.PrintToChatAll(failed == 0
                    ? $"玩家 noclip 已禁止，已将 {restored} 名飞行中的玩家恢复正常移动。"
                    : $"玩家 noclip 已禁止，已恢复 {restored} 名玩家；另有 {failed} 名玩家恢复失败，请管理员检查日志。");
                break;
            case "status": ReplyNoclipStatus(player); break;
            default: Reply(player, "[草人杯] 用法：/noclip <1允许/0禁止/status>"); break;
        }
    }

    private static (int Restored, int Failed) RestoreNoclipPlayers()
    {
        int restored = 0, failed = 0;
        foreach (var controller in Utilities.GetPlayers())
        {
            if (controller is null || !controller.IsValid || !controller.PawnIsAlive) continue;
            var pawn = controller.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || (pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP && pawn.ActualMoveType != MoveType_t.MOVETYPE_NOCLIP)) continue;
            try
            {
                // Both movement fields must agree, otherwise the engine may leave the pawn flying.
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
                restored++;
            }
            catch (Exception ex)
            {
                failed++;
                CaorenCupChat.PrintToConsole($"恢复玩家 noclip 失败（slot {controller.Slot}）：{ex.Message}");
            }
        }
        return (restored, failed);
    }

    private void ReplyNoclipChanged(CCSPlayerController? player)
    {
        string message = $"[草人杯] 玩家 noclip 已设置为：{(_allowPlayerNoclip ? "允许" : "禁止")}。";
        CaorenCupChat.PrintToChatAll(message);
    }

    private void ReplyNoclipStatus(CCSPlayerController? player) =>
        Reply(player, $"[草人杯] 当前玩家 noclip：{(_allowPlayerNoclip ? "允许" : "禁止")}。用法：/noclip <1允许/0禁止/status>");

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
            CaorenCupChat.PrintToGroup([spectator], $"[队伍 {side}] {name}: {message}");
        }
        return HookResult.Continue;
    }

    private void OnRestoreRoundCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            Reply(player, "只有管理员可以恢复正式比赛回合。");
            return;
        }

        if (CaorenCupPracticeState.IsPracticeActive)
        {
            Reply(player, "当前处于 MatchZy Practice，不能使用 /ccrestore；请先 /sleep。");
            return;
        }

        if (!OwnsPauseCommands || ReadGameRules() is not { WarmupPeriod: false, GameRestart: false } rules
            || rules.GamePhase == MatchEndedPhase || _finishedMatchRevision == GameModes.State.Revision)
        {
            Reply(player, "当前没有正在运行的正式 Gamemode，不能恢复回合。");
            return;
        }

        if (info.ArgCount < 2 || !int.TryParse(info.GetArg(1), out var round) || round is < 1 or > 999)
        {
            Reply(player, "用法：/ccrestore <回合数>，回合数必须是 1 到 999 的整数。");
            return;
        }

        var fileName = (GameModes as GameModeRuntime)?.GetRoundBackupFileName(round);
        if (fileName is null || !File.Exists(Path.Combine(Server.GameDirectory, "csgo", fileName)))
        {
            Reply(player, $"本场第 {round} 回合没有可用备份。若所有回合都缺少备份，请核对服务端启动参数 -allow_round_backup_files。");
            return;
        }

        Server.ExecuteCommand($"mp_backup_restore_load_file {fileName}");
        CaorenCupChat.PrintToChatAll($"管理员已请求恢复至第 {round} 回合开局，等待 CS2 载入备份。");
    }

    private void OnGameModeCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            Reply(player, "你没有权限控制游戏模式。");
            return;
        }

        if (info.ArgCount < 2)
        {
            Reply(player, "/gamemode (/gm) <competitive|comp|rush|duel|ability|random|undercover|under|unbalanced|unb|status|start|stop>");
            Reply(player, "正式比赛暂停：管理员 /pause (/p)、/unpause (/up)；玩家在回合冻结时间使用 /tac（战术）、/tech（技术）。");
            return;
        }

        string action = info.GetArg(1).Trim().ToLowerInvariant();
        GameModeResult result = action switch
        {
            "status" => GameModeResult.Ok(GameModes.GetStatusText()),
            "start" => GameModes.Start(),
            "stop" => GameModes.Stop(),
            _ => GameModes.SelectMode(action),
        };
        if (!result.Success || action == "status" || (action == "stop" && GameModes.State.ModeId is null))
            Reply(player, result.Message);
    }

    private void OnGameModeCvarCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!HasRoot(player))
        {
            Reply(player, "你没有权限修改游戏模式 CVar。");
            return;
        }
        if (info.ArgCount < 3)
        {
            Reply(player, "/gamemode_cvar <cvar> <value>");
            return;
        }

        string name = info.GetArg(1).Trim();
        string value = info.GetArg(2).Trim();
        var result = GameModes.SetOverride(name, value, webSource: false);
        if (!result.Success) Reply(player, result.Message);
    }

}
