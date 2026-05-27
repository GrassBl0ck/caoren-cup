using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CaorenCup.Features;

public class SkillPointsFeature : ICaorenFeature
{
    public string FeatureName => "技能点系统 (SkillPoints)";

    private const int CashPerSkillPoint = 600;
    private const int MaxBuyPointsPerRequest = 5;
    private const int MaxBuyRequestsPerRound = 1;
    private const int MaxBuyRequestsPerHalf = 2;
    private const int BuyConfirmSeconds = 10;
    private const int RoundsPerHalf = 12;

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

    // 本回合购买记录
    private int _boughtRoundT = 0;
    private int _boughtRoundCT = 0;

    // 本半场购买记录。当前按 CS2 MR12 的 12 回合半场计数。
    private int _buyCountHalfT = 0;
    private int _buyCountHalfCT = 0;
    private int _roundInHalf = 0;

    private PendingSkillPointBuy? _pendingBuyT;
    private PendingSkillPointBuy? _pendingBuyCT;
    private Timer? _pendingBuyTimerT;
    private Timer? _pendingBuyTimerCT;

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
            _boughtRoundT = 0;
            _boughtRoundCT = 0;
            _buyCountHalfT = 0;
            _buyCountHalfCT = 0;
            CancelPendingBuy((int)CsTeam.Terrorist, false);
            CancelPendingBuy((int)CsTeam.CounterTerrorist, false);
        }
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnCommandSp(CCSPlayerController? player, CommandInfo info)
    {
        bool isChatCommand = info.CallingContext == CommandCallingContext.Chat;
        string arg1 = info.ArgCount >= 2 ? info.GetArg(1).ToLowerInvariant() : string.Empty;

        if (isChatCommand && arg1 == "buy")
        {
            HandleBuyCommand(player, info);
            return;
        }

        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        // 相当于输入 /sp -> 原版的 show 和 menu 功能
        if (info.ArgCount == 1)
        {
            if (player != null)
            {
                if (isChatCommand)
                {
                    CaorenCupUtils.PrintToChat(player, $"\u0004当前点数: \u0007T: {_pointsT} \u0001| \u000CCT: {_pointsCT}");
                    CaorenCupUtils.PrintToChat(player, $"\u0004管理员: \u0001/sp <t/ct/all/swap/0> [数值或+加-减]");
                    CaorenCupUtils.PrintToChat(player, $"\u0004玩家: \u0001聊天栏输入 .sp 1/3/5/rand 使用技能点；/sp buy <1-5> 购买技能点");
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
            _boughtRoundT = 0;
            _boughtRoundCT = 0;
            _roundModeT = RoundSpendMode.None;
            _roundModeCT = RoundSpendMode.None;
            CancelPendingBuy((int)CsTeam.Terrorist, false);
            CancelPendingBuy((int)CsTeam.CounterTerrorist, false);
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
        _boughtRoundT = 0;
        _boughtRoundCT = 0;
        _roundModeT = RoundSpendMode.None;
        _roundModeCT = RoundSpendMode.None;
        CancelPendingBuy((int)CsTeam.Terrorist, false);
        CancelPendingBuy((int)CsTeam.CounterTerrorist, false);

        _roundInHalf++;
        if (_roundInHalf > RoundsPerHalf)
        {
            _roundInHalf = 1;
            _buyCountHalfT = 0;
            _buyCountHalfCT = 0;
            CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[技能点]{ChatColors.Default} 新半场开始，双方购买技能点次数已重置。");
        }

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
            CaorenCupUtils.PrintToChat(player, "购买: \u0004/sp buy <1-5>\u0001 发起全队付费购买；/sp buy y 或 /sp buy n 确认/拒绝。");
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
        int boughtRound = (teamNum == (int)CsTeam.Terrorist) ? _boughtRoundT : _boughtRoundCT;
        RoundSpendMode mode = (teamNum == (int)CsTeam.Terrorist) ? _roundModeT : _roundModeCT;

        int roundLimit = 5;
        int remainRound = roundLimit - spentRound;
        bool isRandom = (arg == "rand" || arg == "r" || arg == "random");
        int cost = 0;

        if (GetPendingBuy(teamNum) != null) { CaorenCupUtils.PrintToChat(player, "\u0002本回合有技能点购买正在确认中，不能同时使用技能点。"); return; }
        if (boughtRound > 0) { CaorenCupUtils.PrintToChat(player, "\u0002本回合已购买技能点，不能再使用技能点。"); return; }
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

    private void HandleBuyCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_settings.Enabled)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "技能点系统当前未启用。");
            return;
        }

        if (player == null || !player.IsValid)
        {
            return;
        }

        int teamNum = player.TeamNum;
        if (teamNum != (int)CsTeam.Terrorist && teamNum != (int)CsTeam.CounterTerrorist)
        {
            CaorenCupUtils.PrintToChat(player, "只有 T 或 CT 阵营玩家可以购买技能点。");
            return;
        }

        if (info.ArgCount < 3)
        {
            CaorenCupUtils.PrintToChat(player, "用法: /sp buy <1-5>，或 /sp buy y / /sp buy n。");
            return;
        }

        string arg = info.GetArg(2).Trim().ToLowerInvariant();
        if (arg is "y" or "yes" or "1")
        {
            ConfirmPendingBuy(player, true);
            return;
        }

        if (arg is "n" or "no" or "0")
        {
            ConfirmPendingBuy(player, false);
            return;
        }

        if (!int.TryParse(arg, out int points))
        {
            CaorenCupUtils.PrintToChat(player, "购买数量必须是 1-5 的整数。");
            return;
        }

        StartBuyRequest(player, points);
    }

    private void StartBuyRequest(CCSPlayerController requester, int points)
    {
        int teamNum = requester.TeamNum;

        if (points < 1 || points > MaxBuyPointsPerRequest)
        {
            CaorenCupUtils.PrintToChat(requester, $"每次最多购买 {MaxBuyPointsPerRequest} 点技能点。");
            return;
        }

        if (GetPendingBuy(teamNum) != null)
        {
            CaorenCupUtils.PrintToChat(requester, "你所在阵营已有一笔技能点购买正在确认中。");
            return;
        }

        if (GetRoundMode(teamNum) != RoundSpendMode.None)
        {
            CaorenCupUtils.PrintToChat(requester, "本回合已使用技能点，不能再购买技能点。");
            return;
        }

        if (GetBoughtRound(teamNum) >= MaxBuyRequestsPerRound)
        {
            CaorenCupUtils.PrintToChat(requester, "本回合已购买过技能点，不能再次购买。");
            return;
        }

        if (GetBuyCountHalf(teamNum) >= MaxBuyRequestsPerHalf)
        {
            CaorenCupUtils.PrintToChat(requester, $"本半场已购买 {MaxBuyRequestsPerHalf} 次技能点，不能再次购买。");
            return;
        }

        var players = GetTeamPlayers(teamNum);
        if (players.Count == 0)
        {
            CaorenCupUtils.PrintToChat(requester, "没有找到你所在阵营的有效玩家。");
            return;
        }

        int costPerPlayer = CashPerSkillPoint * points;
        var lacking = players
            .Where(p => p.InGameMoneyServices == null || p.InGameMoneyServices.Account < costPerPlayer)
            .Select(p => p.PlayerName)
            .ToList();

        if (lacking.Count > 0)
        {
            CaorenCupUtils.PrintToChat(requester, $"购买失败，以下队友金钱不足：{string.Join(", ", lacking)}。每人需要 ${costPerPlayer}。");
            return;
        }

        var pending = new PendingSkillPointBuy(
            teamNum,
            requester.SteamID,
            requester.PlayerName,
            points,
            costPerPlayer,
            BuyConfirmSeconds,
            players.Select(p => p.SteamID).ToHashSet()
        );
        pending.AcceptedSteamIds.Add(requester.SteamID);
        SetPendingBuy(teamNum, pending);

        if (pending.AcceptedSteamIds.Count >= pending.RequiredSteamIds.Count)
        {
            CompletePendingBuy(teamNum);
            return;
        }

        AnnounceBuyPrompt(pending);
        StartPendingBuyTimer(teamNum);
    }

    private void ConfirmPendingBuy(CCSPlayerController player, bool accepted)
    {
        int teamNum = player.TeamNum;
        var pending = GetPendingBuy(teamNum);
        if (pending == null)
        {
            CaorenCupUtils.PrintToChat(player, "当前没有需要你确认的技能点购买。");
            return;
        }

        if (!pending.RequiredSteamIds.Contains(player.SteamID))
        {
            CaorenCupUtils.PrintToChat(player, "你不在这笔购买的确认名单中。");
            return;
        }

        if (!accepted)
        {
            PrintToTeam(teamNum, $"{ChatColors.Red}[技能点购买]{ChatColors.Default} {player.PlayerName} 已拒绝，本次购买终止。");
            CancelPendingBuy(teamNum, false);
            return;
        }

        pending.AcceptedSteamIds.Add(player.SteamID);
        int remaining = pending.RequiredSteamIds.Count - pending.AcceptedSteamIds.Count;
        if (remaining > 0)
        {
            PrintToTeam(teamNum, $"{ChatColors.Green}[技能点购买]{ChatColors.Default} {player.PlayerName} 已同意，还差 {remaining} 人确认。");
            return;
        }

        CompletePendingBuy(teamNum);
    }

    private void CompletePendingBuy(int teamNum)
    {
        var pending = GetPendingBuy(teamNum);
        if (pending == null) return;

        var players = GetRequiredBuyPlayers(pending);
        if (players.Count != pending.RequiredSteamIds.Count)
        {
            PrintToTeam(teamNum, $"{ChatColors.Red}[技能点购买]{ChatColors.Default} 购买失败：确认期间队伍成员发生变化，请重新发起。");
            CancelPendingBuy(teamNum, false);
            return;
        }

        if (!ValidateTeamCanPay(players, pending.CostPerPlayer, out string error))
        {
            PrintToTeam(teamNum, $"{ChatColors.Red}[技能点购买]{ChatColors.Default} 购买失败：{error}");
            CancelPendingBuy(teamNum, false);
            return;
        }

        foreach (var p in players)
        {
            var services = p.InGameMoneyServices;
            if (services == null) continue;
            services.Account = Math.Max(0, services.Account - pending.CostPerPlayer);
            Utilities.SetStateChanged(p, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        if (teamNum == (int)CsTeam.Terrorist)
        {
            _pointsT += pending.Points;
            _boughtRoundT++;
            _buyCountHalfT++;
        }
        else
        {
            _pointsCT += pending.Points;
            _boughtRoundCT++;
            _buyCountHalfCT++;
        }

        string teamName = TeamName(teamNum);
        CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[技能点购买成功]{ChatColors.Default} {teamName} 方全员支付 ${pending.CostPerPlayer}，购买 {pending.Points} 点技能点。");
        CaorenCupUtils.PrintToChatAll($"{ChatColors.Green}[技能点]{ChatColors.Default} 当前点数：\u0007T={_pointsT}\u0001 | \u000CCT={_pointsCT}\u0001");
        CancelPendingBuy(teamNum, false);
    }

    private void StartPendingBuyTimer(int teamNum)
    {
        SetPendingBuyTimer(teamNum, _plugin.AddTimer(1.0f, () =>
        {
            var pending = GetPendingBuy(teamNum);
            if (pending == null) return;

            pending.RemainingSeconds--;
            if (pending.RemainingSeconds <= 0)
            {
                PrintToTeam(teamNum, $"{ChatColors.Red}[技能点购买]{ChatColors.Default} 确认超时，本次购买终止。");
                CancelPendingBuy(teamNum, false);
                return;
            }

            AnnounceBuyPrompt(pending);
            StartPendingBuyTimer(teamNum);
        }));
    }

    private void AnnounceBuyPrompt(PendingSkillPointBuy pending)
    {
        int missing = pending.RequiredSteamIds.Count - pending.AcceptedSteamIds.Count;
        PrintToTeam(
            pending.TeamNum,
            $"{ChatColors.Green}[技能点购买]{ChatColors.Default} {pending.RequesterName} 发起购买：是否支付 ${pending.CostPerPlayer} 来购买 {pending.Points} 点技能点？剩余 {pending.RemainingSeconds} 秒，还差 {missing} 人同意。输入 {ChatColors.Green}/sp buy y{ChatColors.Default} 同意，{ChatColors.Red}/sp buy n{ChatColors.Default} 拒绝。"
        );
    }

    private bool ValidateTeamCanPay(List<CCSPlayerController> players, int costPerPlayer, out string error)
    {
        foreach (var p in players)
        {
            var services = p.InGameMoneyServices;
            if (services == null)
            {
                error = $"{p.PlayerName} 没有经济账户。";
                return false;
            }

            if (services.Account < costPerPlayer)
            {
                error = $"{p.PlayerName} 金钱不足，需要 ${costPerPlayer}。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private List<CCSPlayerController> GetTeamPlayers(int teamNum)
    {
        return Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == teamNum)
            .ToList();
    }

    private List<CCSPlayerController> GetRequiredBuyPlayers(PendingSkillPointBuy pending)
    {
        return Utilities.GetPlayers()
            .Where(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == pending.TeamNum && pending.RequiredSteamIds.Contains(p.SteamID))
            .ToList();
    }

    private void PrintToTeam(int teamNum, string message)
    {
        foreach (var p in GetTeamPlayers(teamNum))
        {
            CaorenCupUtils.PrintToChat(p, message);
        }
    }

    private PendingSkillPointBuy? GetPendingBuy(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? _pendingBuyT :
        teamNum == (int)CsTeam.CounterTerrorist ? _pendingBuyCT :
        null;

    private void SetPendingBuy(int teamNum, PendingSkillPointBuy? pending)
    {
        if (teamNum == (int)CsTeam.Terrorist) _pendingBuyT = pending;
        else if (teamNum == (int)CsTeam.CounterTerrorist) _pendingBuyCT = pending;
    }

    private void SetPendingBuyTimer(int teamNum, Timer? timer)
    {
        if (teamNum == (int)CsTeam.Terrorist) _pendingBuyTimerT = timer;
        else if (teamNum == (int)CsTeam.CounterTerrorist) _pendingBuyTimerCT = timer;
    }

    private Timer? GetPendingBuyTimer(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? _pendingBuyTimerT :
        teamNum == (int)CsTeam.CounterTerrorist ? _pendingBuyTimerCT :
        null;

    private void CancelPendingBuy(int teamNum, bool announce)
    {
        var timer = GetPendingBuyTimer(teamNum);
        timer?.Kill();
        SetPendingBuyTimer(teamNum, null);
        SetPendingBuy(teamNum, null);

        if (announce)
        {
            PrintToTeam(teamNum, $"{ChatColors.Red}[技能点购买]{ChatColors.Default} 本次购买已终止。");
        }
    }

    private int GetSpentRound(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? _spentRoundT : _spentRoundCT;

    private RoundSpendMode GetRoundMode(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? _roundModeT : _roundModeCT;

    private int GetBoughtRound(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? _boughtRoundT : _boughtRoundCT;

    private int GetBuyCountHalf(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? _buyCountHalfT : _buyCountHalfCT;

    private static string TeamName(int teamNum) =>
        teamNum == (int)CsTeam.Terrorist ? "\u0007T\u0001" :
        teamNum == (int)CsTeam.CounterTerrorist ? "\u000CCT\u0001" :
        "未知";

    private sealed class PendingSkillPointBuy
    {
        public PendingSkillPointBuy(int teamNum, ulong requesterSteamId, string requesterName, int points, int costPerPlayer, int remainingSeconds, HashSet<ulong> requiredSteamIds)
        {
            TeamNum = teamNum;
            RequesterSteamId = requesterSteamId;
            RequesterName = requesterName;
            Points = points;
            CostPerPlayer = costPerPlayer;
            RemainingSeconds = remainingSeconds;
            RequiredSteamIds = requiredSteamIds;
        }

        public int TeamNum { get; }
        public ulong RequesterSteamId { get; }
        public string RequesterName { get; }
        public int Points { get; }
        public int CostPerPlayer { get; }
        public int RemainingSeconds { get; set; }
        public HashSet<ulong> RequiredSteamIds { get; }
        public HashSet<ulong> AcceptedSteamIds { get; } = new();
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
