using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CaorenCup.Features;

public class EcoGuessFeature : ICaorenFeature
{
    public string FeatureName => "经济谛听(回合经济竞猜)模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;

    // === 【核心输出接口】 ===
    // 未来的模块可以通过 EcoGuessFeature.OnEcoGuessWinnerDecided += 你的方法; 来接收获胜阵营
    // 参数1: 获胜阵营 (CsTeam.Terrorist / CounterTerrorist / None平局)
    // 参数2: 获胜者名称 (用于以后发奖励)
    // 参数3: 最小误差值
    public static Action<CsTeam, string, int>? OnEcoGuessWinnerDecided;

    // 状态机
    private bool _isGuessingOpen = false;
    private int _freezeTime = 15;

    // 存储玩家的猜测: PlayerIndex -> 猜测金额
    private readonly Dictionary<uint, int> _playerGuesses = new Dictionary<uint, int>();

    // 记录本回合最终的播报信息，等到冻结期结束后再打印
    private string _pendingAnnouncement = "";

    // 计时器
    private Timer? _closeTimer;
    private Timer? _calcTimer;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        _plugin.RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        // CS2 冻结期结束事件 (玩家可以开始移动的瞬间)
        _plugin.RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);

        _plugin.AddCommand("css_eco", "猜测敌方平均经济", OnEcoCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload()
    {
        ClearState();
    }

    public void SetEnabled(bool enabled)
    {
        _config.EcoGuess.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnEcoCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (info.ArgCount == 2 && info.GetArg(1) == "0")
        {
            if (player != null && AdminManager.PlayerHasPermissions(player, "@css/root"))
            {
                SetEnabled(false);
                CaorenCupUtils.PrintToChatAll("\u0002已禁用\u0001 经济谛听模块。");
            }
            return;
        }

        if (!_config.EcoGuess.Enabled) return;

        if (player == null || !player.IsValid) return;

        if (!_isGuessingOpen)
        {
            CaorenCupUtils.PrintToChat(player, "当前不在竞猜时间内！(回合冻结期结束前 3 秒截止)");
            return;
        }

        if (info.ArgCount < 2 || !int.TryParse(info.GetArg(1), out int guessValue))
        {
            CaorenCupUtils.PrintToChat(player, "用法: \u0004/eco <金额>\u0001 (如: /eco 3500)");
            return;
        }

        if (guessValue < 0) guessValue = 0;

        _playerGuesses[player.Index] = guessValue;
        CaorenCupUtils.PrintToChat(player, $"你已预测敌方的平均经济为: \u0004${guessValue}\u0001。");
    }

    // --- 核心时间轴：回合准备阶段 ---
    private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        ClearState();
        if (!_config.EcoGuess.Enabled) return HookResult.Continue;

        // 动态获取当前的冻结时间 (默认通常为 15 秒)
        var freezeCvar = ConVar.Find("mp_freezetime");
        if (freezeCvar != null) _freezeTime = freezeCvar.GetPrimitiveValue<int>();
        else _freezeTime = 15;

        // 如果冻结时间太短(小于4秒)，不足以支撑 -3 和 -2 的逻辑，则本回合直接放弃竞猜
        if (_freezeTime < 4) return HookResult.Continue;

        _isGuessingOpen = true;
        _pendingAnnouncement = "";

        CaorenCupUtils.PrintToChatAll($"\u000C[经济谛听] \u0001新回合开始！请使用 \u0004/eco <金额>\u0001 猜测敌方的平均经济！");

        // 定时器 1：倒数第 3 秒，关闭输入通道
        _closeTimer = _plugin.AddTimer(_freezeTime - 3.0f, () =>
        {
            _isGuessingOpen = false;
            CaorenCupUtils.PrintToChatAll($"\u000C[经济谛听] \u0002竞猜通道已关闭，系统正在进行核算...");
        });

        // 定时器 2：倒数第 2 秒，执行核心计算并触发返回值
        _calcTimer = _plugin.AddTimer(_freezeTime - 2.0f, CalculateWinner);

        return HookResult.Continue;
    }

    // --- 核心算法：田忌赛马式的准确度对决 ---
    private void CalculateWinner()
    {
        if (!_config.EcoGuess.Enabled) return;

        // 1. 计算双方当前的真实平均经济
        int tTotal = 0, tCount = 0;
        int ctTotal = 0, ctCount = 0;

        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || p.InGameMoneyServices == null) continue;

            int money = p.InGameMoneyServices.Account;
            if (p.TeamNum == (byte)CsTeam.Terrorist) { tTotal += money; tCount++; }
            else if (p.TeamNum == (byte)CsTeam.CounterTerrorist) { ctTotal += money; ctCount++; }
        }

        int tAvg = tCount > 0 ? tTotal / tCount : 0;
        int ctAvg = ctCount > 0 ? ctTotal / ctCount : 0;

        // 2. 将玩家的猜测分类并计算误差 (Diff = Abs(Guess - 对方平均值))
        var tGuesses = new List<(CCSPlayerController Player, int Guess, int Diff)>();
        var ctGuesses = new List<(CCSPlayerController Player, int Guess, int Diff)>();

        foreach (var kvp in _playerGuesses)
        {
            var p = Utilities.GetPlayerFromIndex((int)kvp.Key);
            if (p == null || !p.IsValid) continue;

            if (p.TeamNum == (byte)CsTeam.Terrorist)
            {
                tGuesses.Add((p, kvp.Value, Math.Abs(kvp.Value - ctAvg)));
            }
            else if (p.TeamNum == (byte)CsTeam.CounterTerrorist)
            {
                ctGuesses.Add((p, kvp.Value, Math.Abs(kvp.Value - tAvg)));
            }
        }

        // 按误差从小到大排序 (越准越靠前)
        tGuesses = tGuesses.OrderBy(g => g.Diff).ToList();
        ctGuesses = ctGuesses.OrderBy(g => g.Diff).ToList();

        // 3. 开始逐级比对 (Tie-breaker)
        CsTeam winningTeam = CsTeam.None;
        string bestPlayerName = "无";
        int bestDiff = -1;

        int maxLen = Math.Max(tGuesses.Count, ctGuesses.Count);

        if (maxLen == 0)
        {
            _pendingAnnouncement = "本回合没有任何人参与经济竞猜。";
        }
        else
        {
            for (int i = 0; i < maxLen; i++)
            {
                // 如果 T 没人猜了，CT 直接赢 (反之亦然)
                if (i >= tGuesses.Count)
                {
                    winningTeam = CsTeam.CounterTerrorist;
                    bestPlayerName = ctGuesses[i].Player.PlayerName;
                    bestDiff = ctGuesses[i].Diff;
                    break;
                }
                if (i >= ctGuesses.Count)
                {
                    winningTeam = CsTeam.Terrorist;
                    bestPlayerName = tGuesses[i].Player.PlayerName;
                    bestDiff = tGuesses[i].Diff;
                    break;
                }

                int tDiff = tGuesses[i].Diff;
                int ctDiff = ctGuesses[i].Diff;

                if (tDiff < ctDiff)
                {
                    winningTeam = CsTeam.Terrorist;
                    bestPlayerName = tGuesses[i].Player.PlayerName;
                    bestDiff = tDiff;
                    break;
                }
                else if (ctDiff < tDiff)
                {
                    winningTeam = CsTeam.CounterTerrorist;
                    bestPlayerName = ctGuesses[i].Player.PlayerName;
                    bestDiff = ctDiff;
                    break;
                }
                // 如果误差一模一样，循环继续 (i++)，看下一名玩家谁更准
            }
        }

        // 4. 组装最后要播报的字符串
        if (winningTeam != CsTeam.None)
        {
            string teamName = winningTeam == CsTeam.Terrorist ? "\u0007T阵营\u0001" : "\u000CCT阵营\u0001";
            _pendingAnnouncement = $"经济竞猜结果: {teamName} 的 \u0004{bestPlayerName}\u0001 拿下了最准预测！误差仅为 \u0004${bestDiff}\u0001！\n(真相: T平均 ${tAvg} | CT平均 ${ctAvg})";
        }
        else if (maxLen > 0)
        {
            _pendingAnnouncement = $"经济竞猜结果: 双方误差竟然完全一致，平局！\n(真相: T平均 ${tAvg} | CT平均 ${ctAvg})";
        }

        // 5. 触发外部接口调用 (将数值返还给未来的模块)
        OnEcoGuessWinnerDecided?.Invoke(winningTeam, bestPlayerName, bestDiff);
    }

    // --- 时间轴：冻结期结束 (公屏揭晓) ---
    private HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_config.EcoGuess.Enabled || string.IsNullOrEmpty(_pendingAnnouncement))
            return HookResult.Continue;

        // 战斗开始的瞬间，弹出竞猜结果
        CaorenCupUtils.PrintToChatAll(_pendingAnnouncement);

        return HookResult.Continue;
    }

    private void ClearState()
    {
        _isGuessingOpen = false;
        _playerGuesses.Clear();
        _pendingAnnouncement = "";

        _closeTimer?.Kill(); _closeTimer = null;
        _calcTimer?.Kill(); _calcTimer = null;
    }

    // --- 接口实现 ---
    public string GetHelpEntry() => "/eco - 经济谛听(经济竞猜)控制与参与";

    public string GetStatusInfo() => _config.EcoGuess.Enabled ? "已开启" : "已禁用";

    public string? GetPublicConfigInfo() => _config.EcoGuess.Enabled ? "经济谛听: 回合冻结期间可使用 /eco 猜测敌方平均经济" : null;

    public string GetFeatureDescription()
    {
        if (!_config.EcoGuess.Enabled) return "暂无场外博弈。";

        return "【经济谛听】回合买枪期间，所有人均可输入 \u0004/eco <金额>\u0001 盲猜敌方当前的平均经济！\n系统将自动比对双方最准的玩家（误差一样则顺延比对第二名），以此决定哪一方拥有最强的大脑！在冻结时间结束时将揭晓真相！";
    }
}