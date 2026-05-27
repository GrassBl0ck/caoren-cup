using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Collections.Generic;

namespace CaorenCup.Features;

public class MoneyFeature : ICaorenFeature
{
    public string FeatureName => "Money Multiplier (经济倍率)";

    private CaorenCupPlugin _plugin = null!;
    private MoneySettings _settings = null!;
    private readonly Dictionary<ulong, int> _moneyBeforeDeath = new();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型控制台指令
        plugin.AddCommand("css_cash", "设置经济倍率: css_cash <t/ct/all/0> <倍数> [1/0回合奖励]", OnCommandCash);

        // 事件监听
        plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeathPre, HookMode.Pre);
        plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeathPost, HookMode.Post);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.Money;
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
    }

    // --- 核心指令逻辑 ---

    private void OnCommandCash(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
            return;
        }

        if (info.ArgCount == 1)
        {
            PrintUsage(player);
            return;
        }

        string arg1 = info.GetArg(1).ToLower();

        // 1. 一键禁用
        if (arg1 == "0" || arg1 == "off")
        {
            SetEnabled(false);
            _plugin.SaveConfig();
            CaorenCupUtils.PrintToChatAll(CaorenCupUtils.FormatChangeMessage("模块控制", FeatureName, $"{ChatColors.Red}已禁用"));
            return;
        }

        // 2. 参数解析
        if (info.ArgCount < 3)
        {
            if (player == null) Console.WriteLine("[草人杯] 参数不足。"); else PrintUsage(player);
            return;
        }

        string target = arg1;
        if (target != "t" && target != "ct" && target != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的阵营，请使用 t, ct 或 all。");
            return;
        }

        if (!float.TryParse(info.GetArg(2), out float multiplier) || multiplier < 0)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的倍数数值 (需大于等于0)。");
            return;
        }

        bool enableRoundBonus = false;
        if (info.ArgCount >= 4)
        {
            if (int.TryParse(info.GetArg(3), out int rbInt) && rbInt == 1)
            {
                enableRoundBonus = true;
            }
        }

        // 应用配置 (后覆盖机制)
        _settings.Target = target;
        _settings.Multiplier = multiplier;
        _settings.EnableRoundBonus = enableRoundBonus;
        _settings.Enabled = true;

        _plugin.SaveConfig();

        string roundStr = enableRoundBonus ? "包含回合奖励" : "仅击杀奖励";
        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用! 阵营:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} | 获得金钱变为 {multiplier}倍 ({roundStr})");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== Cash (经济倍率) 指令说明 ===");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/cash 0{ChatColors.Default} : 一键禁用并恢复正常经济");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/cash <t/ct/all> <倍率> [1/0是否修改回合奖励]{ChatColors.Default}");
        CaorenCupUtils.PrintToChat(player, $" 示例: /cash all 2 (全员击杀获得双倍金钱)");
        CaorenCupUtils.PrintToChat(player, $" 示例: /cash t 0.5 1 (T阵营击杀和回合结束都只能拿到一半钱)");
        CaorenCupUtils.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏逻辑 1: 击杀奖励 ---

    private HookResult OnPlayerDeathPre(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.Multiplier == 1.0f) return HookResult.Continue;

        var killer = @event.Attacker;
        // 如果自杀或没有击杀者，不处理
        if (killer == null || !killer.IsValid || killer == @event.Userid) return HookResult.Continue;

        // 判断阵营
        if (!IsTarget(killer)) return HookResult.Continue;

        var services = killer.InGameMoneyServices;
        if (services != null)
        {
            _moneyBeforeDeath[killer.SteamID] = services.Account;
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeathPost(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.Multiplier == 1.0f) return HookResult.Continue;

        var killer = @event.Attacker;
        if (killer == null || !killer.IsValid || killer == @event.Userid) return HookResult.Continue;
        if (!IsTarget(killer)) return HookResult.Continue;

        ulong steamId = killer.SteamID;
        if (!_moneyBeforeDeath.TryGetValue(steamId, out int beforeMoney)) return HookResult.Continue;
        _moneyBeforeDeath.Remove(steamId);

        _plugin.AddTimer(0.1f, () =>
        {
            if (!killer.IsValid) return;

            var result = ApplyMultiplierFromActualAward(killer, beforeMoney, _settings.Multiplier);
            if (result.Changed)
            {
                PrintCashAdjustment(killer, "击杀奖励", result.BaseAward, _settings.Multiplier, result.ActualDelta, result.NewMoney);
            }
        });

        return HookResult.Continue;
    }

    // --- 游戏逻辑 2: 回合结束奖励 ---

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!_settings.Enabled || !_settings.EnableRoundBonus || _settings.Multiplier == 1.0f) return HookResult.Continue;

        int winnerTeam = @event.Winner; // 2=T, 3=CT
        if (winnerTeam != 2 && winnerTeam != 3) return HookResult.Continue; // 平局不处理

        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || p.TeamNum < 2 || p.InGameMoneyServices == null) continue;

            if (IsTarget(p))
            {
                int beforeMoney = p.InGameMoneyServices.Account;

                // 为了防止在引擎刚刚发完钱的一瞬间冲突，延时 0.5 秒补发差额
                _plugin.AddTimer(0.5f, () =>
                {
                    if (!p.IsValid) return;

                    var result = ApplyMultiplierFromActualAward(p, beforeMoney, _settings.Multiplier);
                    if (result.Changed)
                    {
                        string reason = p.TeamNum == winnerTeam ? "回合胜利奖励" : "回合失败奖励";
                        PrintCashAdjustment(p, reason, result.BaseAward, _settings.Multiplier, result.ActualDelta, result.NewMoney);
                    }
                });
            }
        }

        return HookResult.Continue;
    }

    // --- 辅助函数 ---

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;
        if (_settings.Target == "t" && player.Team == CsTeam.Terrorist) return true;
        if (_settings.Target == "ct" && player.Team == CsTeam.CounterTerrorist) return true;
        return false;
    }

    private CashAdjustmentResult ApplyMultiplierFromActualAward(CCSPlayerController player, int beforeMoney, float multiplier)
    {
        var services = player.InGameMoneyServices;
        if (services == null) return CashAdjustmentResult.NoChange;

        int current = services.Account;
        int actualAward = current - beforeMoney;
        if (actualAward == 0) return CashAdjustmentResult.NoChange;

        // 差额 = 游戏实际已经发的钱 * (倍率 - 1)
        int extra = (int)Math.Round(actualAward * (multiplier - 1.0f));
        if (extra == 0) return CashAdjustmentResult.NoChange;

        int newMoney = current + extra;
        int maxMoney = GetMaxMoney();

        if (newMoney < 0) newMoney = 0;
        if (newMoney > maxMoney) newMoney = maxMoney;

        int actualDelta = newMoney - current;
        if (actualDelta == 0) return CashAdjustmentResult.NoChange;

        services.Account = newMoney;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        return new CashAdjustmentResult(true, actualAward, actualDelta, newMoney);
    }

    private static int GetMaxMoney()
    {
        try
        {
            var cvar = ConVar.Find("mp_maxmoney");
            int value = cvar?.GetPrimitiveValue<int>() ?? 16000;
            return Math.Max(0, value);
        }
        catch
        {
            return 16000;
        }
    }

    private static void PrintCashAdjustment(CCSPlayerController player, string reason, int baseAmount, float multiplier, int actualDelta, int newMoney)
    {
        string sign = actualDelta > 0 ? "+" : "-";
        int absDelta = Math.Abs(actualDelta);

        CaorenCupUtils.PrintToChat(
            player,
            $"{ChatColors.Green}[Cash]{ChatColors.Default} {reason}倍率生效：基础 ${baseAmount}，倍率 {multiplier:0.###}x，本次修正 {sign}${absDelta}，当前金钱 ${newMoney}"
        );
    }

    private readonly record struct CashAdjustmentResult(bool Changed, int BaseAward, int ActualDelta, int NewMoney)
    {
        public static CashAdjustmentResult NoChange => new(false, 0, 0, 0);
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/cash{ChatColors.Default} : 查看并设置 经济倍率 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"Cash: {ChatColors.Red}已禁用{ChatColors.Default}";
        string roundInfo = _settings.EnableRoundBonus ? "含回合奖" : "仅击杀";
        return $"Cash: {ChatColors.Green}启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 倍数:{_settings.Multiplier}x ({roundInfo})";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled || _settings.Multiplier == 1.0f) return null;
        string target = _settings.Target == "all" ? "全员" : $"{_settings.Target.ToUpper()} 阵营";
        string range = _settings.EnableRoundBonus ? "一切收入" : "击杀收入";
        return $"[经济变动] {target} 的{range}变为 {_settings.Multiplier} 倍。";
    }

    public string GetFeatureDescription()
    {
        return " [Cash] 修改比赛获得的金钱倍率。\n" +
               " - 默认只修改击杀获得的奖励倍率。\n" +
               " - 开启回合奖励修改后，每回合结束时结算的胜负奖金也会翻倍。";
    }
}
