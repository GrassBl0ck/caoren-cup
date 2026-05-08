using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;

namespace CaorenCup.Features;

public class MoneyFeature : ICaorenFeature
{
    public string FeatureName => "Money Multiplier (经济倍率)";

    private CaorenCupPlugin _plugin = null!;
    private MoneySettings _settings = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型控制台指令
        plugin.AddCommand("css_cash", "设置经济倍率: css_cash <t/ct/all/0> <倍数> [1/0回合奖励]", OnCommandCash);

        // 事件监听
        plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
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

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.Multiplier == 1.0f) return HookResult.Continue;

        var killer = @event.Attacker;
        // 如果自杀或没有击杀者，不处理
        if (killer == null || !killer.IsValid || killer == @event.Userid) return HookResult.Continue;

        // 判断阵营
        if (!IsTarget(killer)) return HookResult.Continue;

        // 基础击杀奖励估算 (基于武器)
        int baseReward = 300;
        string weapon = @event.Weapon ?? "";
        if (weapon.Contains("awp")) baseReward = 100;
        else if (weapon.Contains("knife")) baseReward = 1500;
        else if (weapon.Contains("shotgun") || weapon.Contains("xm1014") || weapon.Contains("mag7") || weapon.Contains("nova") || weapon.Contains("sawedoff")) baseReward = 900;
        else if (weapon.Contains("smg") || weapon.Contains("mac10") || weapon.Contains("mp9") || weapon.Contains("mp7") || weapon.Contains("ump45") || weapon.Contains("bizon")) baseReward = 600;

        if (weapon == "p90") baseReward = 300; // P90 特例

        // 补发差额
        GiveExtraMoney(killer, baseReward, _settings.Multiplier);
        return HookResult.Continue;
    }

    // --- 游戏逻辑 2: 回合结束奖励 ---

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!_settings.Enabled || !_settings.EnableRoundBonus || _settings.Multiplier == 1.0f) return HookResult.Continue;

        int winnerTeam = @event.Winner; // 2=T, 3=CT
        if (winnerTeam != 2 && winnerTeam != 3) return HookResult.Continue; // 平局不处理

        int winBase = 3250; // 标准胜利奖金估算
        int lossBase = 1900; // 标准连败奖金估算

        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || p.TeamNum < 2) continue;

            if (IsTarget(p))
            {
                int baseMoney = (p.TeamNum == winnerTeam) ? winBase : lossBase;

                // 为了防止在引擎刚刚发完钱的一瞬间冲突，延时 0.5 秒补发差额
                _plugin.AddTimer(0.5f, () =>
                {
                    if (p.IsValid) GiveExtraMoney(p, baseMoney, _settings.Multiplier);
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

    private void GiveExtraMoney(CCSPlayerController player, int baseAmount, float multiplier)
    {
        // 差额 = 目标总额 - 引擎已经发的总额
        int extra = (int)Math.Round(baseAmount * (multiplier - 1.0f));
        if (extra == 0) return;

        var services = player.InGameMoneyServices;
        if (services != null)
        {
            int current = services.Account;
            int newMoney = current + extra;

            if (newMoney < 0) newMoney = 0;
            if (newMoney > 16000) newMoney = 16000; // 默认最大经济上限

            services.Account = newMoney;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }
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