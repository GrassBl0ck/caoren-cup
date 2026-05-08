using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features;

public class SkillPointsFeature : ICaorenFeature
{
    public string FeatureName => "技能点系统 (SkillPoints)";

    private SkillPointsSettings _settings = null!;
    private CaorenCupPlugin _plugin = null!;

    // 运行时数据（不保存到 Config，重启重置）
    private int _pointsT = 0;
    private int _pointsCT = 0;

    // 本回合消耗记录
    private int _spentRoundT = 0;
    private int _spentRoundCT = 0;

    private enum RoundSpendMode { None, Fixed, Random }
    private RoundSpendMode _roundModeT = RoundSpendMode.None;
    private RoundSpendMode _roundModeCT = RoundSpendMode.None;

    private readonly Random _rng = new();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册事件
        _plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        _plugin.RegisterEventHandler<EventPlayerChat>(OnPlayerChat);

        // === 唯一核心指令 ===
        _plugin.AddCommand("css_sp", "技能点控制系统", OnCommandSp);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.SkillPoints;
    }

    public void OnUnload()
    {
        // 清理数据
        _pointsT = 0;
        _pointsCT = 0;
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled)
        {
            _pointsT = 0;
            _pointsCT = 0;
            _spentRoundT = 0;
            _spentRoundCT = 0;
        }
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnCommandSp(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        bool isChatCommand = info.CallingContext == CommandCallingContext.Chat;

        // 相当于输入 /sp -> 原版的 show 和 menu 功能
        if (info.ArgCount == 1)
        {
            if (player != null)
            {
                if (isChatCommand)
                {
                    CaorenCupUtils.PrintToChat(player, $"\u0004当前点数: \u0007T: {_pointsT} \u0001| \u000CCT: {_pointsCT}");
                    CaorenCupUtils.PrintToChat(player, $"\u0004用法: \u0001/sp <t/ct/all/swap/0> [数值或+加-减]");
                    CaorenCupUtils.PrintToChat(player, $"\u0004玩家: \u0001聊天栏输入 .sp 1/3/5/rand 使用技能点");
                    CaorenCupUtils.PrintToChat(player, $"当前点数: T={_pointsT} | CT={_pointsCT}");
                }
                else
                {
                    player.PrintToConsole("========== [草人杯] 技能点系统 (SkillPoints) ==========");
                    player.PrintToConsole($"系统状态: {GetStatusInfo()}");
                    player.PrintToConsole($"当前点数: T={_pointsT} | CT={_pointsCT}");
                    player.PrintToConsole("指令: css_sp <目标/操作> [数值或加减]");
                    player.PrintToConsole("  输入 0 禁用系统并清空分数。");
                    player.PrintToConsole("  输入 swap 交换双方当前点数。");
                    player.PrintToConsole("  目标: t, ct, all");
                    player.PrintToConsole("范例1(绝对赋值): css_sp t 50 (直接将T方点数设置为50)");
                    player.PrintToConsole("范例2(相对增减): css_sp ct +10 (CT方点数增加10)");
                    player.PrintToConsole("范例3(相对增减): css_sp all -5 (双方同时扣除5点，最低为0)");
                    player.PrintToConsole("玩家使用: 在聊天框输入 .sp 1 (或3,5,rand) 消耗点数");
                    player.PrintToConsole("=====================================================");
                }
            }
            return;
        }

        string arg1 = info.GetArg(1).ToLower();

        if (arg1 == "0" || arg1 == "off" || arg1 == "false")
        {
            SetEnabled(false);
            CaorenCupUtils.PrintToChatAll($"\u0002已禁用\u0001 技能点系统，当前点数已清空。");
            return;
        }

        if (arg1 == "1" || arg1 == "on" || arg1 == "true")
        {
            SetEnabled(true);
            CaorenCupUtils.PrintToChatAll($"\u0004已启用\u0001 技能点系统。");
            return;
        }

        if (arg1 == "swap")
        {
            (_pointsT, _pointsCT) = (_pointsCT, _pointsT);
            _spentRoundT = 0;
            _spentRoundCT = 0;
            _roundModeT = RoundSpendMode.None;
            _roundModeCT = RoundSpendMode.None;
            CaorenCupUtils.PrintToChatAll($"\u0004[技能点]\u0001 已交换双方点数。当前 \u0007T={_pointsT}\u0001，\u000CCT={_pointsCT}");
            return;
        }

        if (arg1 == "t" || arg1 == "ct" || arg1 == "all")
        {
            if (info.ArgCount < 3)
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "参数不足，请提供点数或增减量。");
                return;
            }

            string arg2 = info.GetArg(2);
            bool isRelative = arg2.StartsWith('+') || arg2.StartsWith('-');

            // 过滤掉加号，以支持 int.TryParse 解析
            if (!int.TryParse(arg2.Replace("+", ""), out int val))
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "数值格式错误。");
                return;
            }

            _settings.Enabled = true;
            _plugin.SaveConfig();

            if (arg1 == "t" || arg1 == "all")
                _pointsT = Math.Max(0, isRelative ? _pointsT + val : val);

            if (arg1 == "ct" || arg1 == "all")
                _pointsCT = Math.Max(0, isRelative ? _pointsCT + val : val);

            string actionStr = isRelative ? $"变化为" : "设置为";
            CaorenCupUtils.PrintToChatAll($"\u0004[技能点]\u0001 管理员已将 \u0004{arg1.ToUpper()}\u0001 方点数{actionStr} -> \u0007T: {_pointsT} \u0001| \u000CCT: {_pointsCT}");
        }
        else
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效参数，请输入 /sp 查看帮助。");
        }
    }

    // --- 游戏逻辑 ---

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _spentRoundT = 0;
        _spentRoundCT = 0;
        _roundModeT = RoundSpendMode.None;
        _roundModeCT = RoundSpendMode.None;
        return HookResult.Continue;
    }

    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        string text = (@event.Text ?? string.Empty).Trim();
        if (!text.StartsWith(".sp", StringComparison.OrdinalIgnoreCase)) return HookResult.Continue;

        var player = Utilities.GetPlayerFromUserid(@event.Userid);
        if (player == null || !player.IsValid) return HookResult.Continue;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[1] == "?" || parts[1].ToLower() == "help")
        {
            int myPoints = (player.TeamNum == (int)CsTeam.Terrorist) ? _pointsT : _pointsCT;
            CaorenCupUtils.PrintToChat(player, $"你所在阵营剩余技能点: \u0004{myPoints}");
            CaorenCupUtils.PrintToChat(player, "用法: \u0004.sp 1 \u0001或 \u0004.sp 3 \u0001或 \u0004.sp 5 \u0001或 \u0004.sp rand");
            return HookResult.Continue;
        }

        ProcessSpRequest(player, parts[1].ToLower());
        return HookResult.Continue;
    }

    private void ProcessSpRequest(CCSPlayerController player, string arg)
    {
        int teamNum = player.TeamNum;
        if (teamNum != (int)CsTeam.Terrorist && teamNum != (int)CsTeam.CounterTerrorist) return;

        string teamName = (teamNum == (int)CsTeam.Terrorist) ? "\u0007T\u0001" : "\u000CCT\u0001";
        int curPoints = (teamNum == (int)CsTeam.Terrorist) ? _pointsT : _pointsCT;
        int spentRound = (teamNum == (int)CsTeam.Terrorist) ? _spentRoundT : _spentRoundCT;
        RoundSpendMode mode = (teamNum == (int)CsTeam.Terrorist) ? _roundModeT : _roundModeCT;

        int roundLimit = 5;
        int remainRound = roundLimit - spentRound;
        bool isRandom = (arg == "rand" || arg == "r" || arg == "random");
        int cost = 0;

        if (isRandom && mode == RoundSpendMode.Fixed) { CaorenCupUtils.PrintToChat(player, "\u0002本回合已使用固定点数，不能混用随机。"); return; }
        if (!isRandom && mode == RoundSpendMode.Random) { CaorenCupUtils.PrintToChat(player, "\u0002本回合已使用随机点数，不能混用固定。"); return; }
        if (isRandom && mode == RoundSpendMode.Random) { CaorenCupUtils.PrintToChat(player, "\u0002本回合已随机过一次，不可重复。"); return; }

        if (isRandom)
        {
            if (remainRound <= 0) { CaorenCupUtils.PrintToChat(player, "\u0002本回合限额已满。"); return; }
            cost = _rng.Next(0, remainRound + 1);
        }
        else
        {
            if (!int.TryParse(arg, out cost)) { CaorenCupUtils.PrintToChat(player, "\u0002无效数值。"); return; }
            if (cost != 1 && cost != 3 && cost != 5) { CaorenCupUtils.PrintToChat(player, "\u0002只能使用 1, 3, 5 或 rand。"); return; }
            if (cost > remainRound) { CaorenCupUtils.PrintToChat(player, $"\u0002本回合剩余限额不足 (剩{remainRound})。"); return; }
        }

        if (curPoints < cost) { CaorenCupUtils.PrintToChat(player, $"\u0002阵营总点数不足 (剩{curPoints})。"); return; }

        if (teamNum == (int)CsTeam.Terrorist) { _pointsT -= cost; _spentRoundT += cost; _roundModeT = isRandom ? RoundSpendMode.Random : RoundSpendMode.Fixed; }
        else { _pointsCT -= cost; _spentRoundCT += cost; _roundModeCT = isRandom ? RoundSpendMode.Random : RoundSpendMode.Fixed; }

        string typeStr = isRandom ? "随机" : cost.ToString();
        CaorenCupUtils.PrintToChatAll($"\u0004[技能点]\u0001 {teamName}方 \u0004{player.PlayerName}\u0001 消耗 \u0002{typeStr}\u0001 点技能！(实扣:{cost})");
        CaorenCupUtils.PrintToChatAll($"\u0001该阵营剩余: \u0004{(teamNum == 2 ? _pointsT : _pointsCT)}\u0001 | 本回合已用: \u0004{(teamNum == 2 ? _spentRoundT : _spentRoundCT)}\u0001/5");

        if (!string.IsNullOrEmpty(_settings.AutoPauseCommand))
        {
            Server.ExecuteCommand(_settings.AutoPauseCommand);
        }
    }

    // --- 接口实现 ---
    public string GetHelpEntry() => "/sp - 管理员技能点控制系统";

    public string GetStatusInfo() => _settings.Enabled ? $"已开启 (T:{_pointsT} | CT:{_pointsCT})" : "已禁用";

    public string? GetPublicConfigInfo() => _settings.Enabled ? "[技能点] 技能经济系统当前可用" : null;

    public string GetFeatureDescription()
    {
        return "【技能点系统】独立于金钱之外的第二经济系统。\n" +
               "- 玩家可在聊天框输入 \u0004.sp 1\u0001 (或3,5,rand) 消耗阵营点数。\n" +
               "- 兑换的技能需要由服主配合其他插件执行释放。";
    }
}