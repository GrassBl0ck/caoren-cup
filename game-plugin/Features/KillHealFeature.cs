using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using System;

namespace CaorenCup.Features;

public class KillHealFeature : ICaorenFeature
{
    public string FeatureName => "击杀状态变更 (KillHeal)";

    private KillHealSettings _settings = null!;
    private CaorenCupPlugin _plugin = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

        // 注册核心集成指令
        _plugin.AddCommand("css_kh", "击杀状态变更控制", OnCommandKh);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.KillHeal;
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnCommandKh(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        bool isChatCommand = info.CallingContext == CommandCallingContext.Chat;

        if (info.ArgCount == 1)
        {
            if (player != null)
            {
                if (isChatCommand)
                {
                    CaorenCupUtils.PrintToChat(player, $"\u0004当前状态: \u0001{GetStatusInfo()}");
                    CaorenCupUtils.PrintToChat(player, $"\u0004用法: \u0001/kh <t/ct/all/0> [数值/默认25]，上下限由 /hpcap 控制");
                    CaorenCupUtils.PrintToChat(player, $"\u0001详细说明已打印到控制台~");
                }
                else
                {
                    player.PrintToConsole("========== [草人杯] 击杀状态变更 (KillHeal) ==========");
                    player.PrintToConsole($"当前状态: {GetStatusInfo()}");
                    player.PrintToConsole("用法: css_kh <t/ct/all/vip/0> [变动数值]");
                    player.PrintToConsole("  输入 0 直接禁用此模块。");
                    player.PrintToConsole("  变动数值: 可为正数(回血)或负数(扣血)，默认 25。");
                    player.PrintToConsole("  血量上下限: 统一使用 css_hpcap <min> <max> 设置。");
                    player.PrintToConsole("范例1: css_hpcap 1 200；css_kh t 50 (T方每次击杀回50，最多到200血)");
                    player.PrintToConsole("范例2: css_hpcap 10 100；css_kh all -10 (所有人击杀反而扣10血，最低扣到10血)");
                    player.PrintToConsole("======================================================");
                }
            }
            return;
        }

        string arg1 = info.GetArg(1).ToLower();

        if (arg1 == "0" || arg1 == "off" || arg1 == "false")
        {
            SetEnabled(false);
            CaorenCupUtils.PrintToChatAll($"\u0002已禁用\u0001 击杀状态变更模块。");
            return;
        }

        // 解析目标
        int mode = -1;
        if (arg1 == "all") mode = 0;
        else if (arg1 == "t") mode = 1;
        else if (arg1 == "ct") mode = 2;
        else if (arg1 == "vip") mode = 3;

        if (mode == -1)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "参数错误: 目标必须是 t/ct/all/vip/0。");
            return;
        }

        // 解析数值 (缺省25)
        int amount = 25;
        if (info.ArgCount >= 3 && int.TryParse(info.GetArg(2), out int parsedAmount))
        {
            amount = parsedAmount;
        }

        if (info.ArgCount >= 4 && player != null)
        {
            CaorenCupUtils.PrintToChat(player, "提示：/kh 的极限区间参数已移到 /hpcap，此处第3个参数会被忽略。请使用 /hpcap <min> <max> 设置全局血量上下限。");
        }

        // 持久化保存
        _settings.Enabled = true;
        _settings.TargetMode = mode;
        _settings.HealAmount = amount;
        _plugin.SaveConfig();

        string actionType = amount >= 0 ? "回血" : "扣血";
        string capText = CaorenCupUtils.IsHpCapEnabled(_plugin) ? $"/hpcap:{_plugin.Config.HpCap.Min}-{_plugin.Config.HpCap.Max}" : "默认范围:1-100";
        CaorenCupUtils.PrintToChatAll($"击杀状态已更新 -> 目标: {GetModeString(mode)} | 击杀{actionType}: {amount}HP | {capText}");
    }

    // --- 游戏事件核心逻辑 ---
    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.HealAmount == 0) return HookResult.Continue;

        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || !attacker.IsValid || attacker.IsBot) return HookResult.Continue;
        if (victim == null || !victim.IsValid) return HookResult.Continue;

        // 排除自杀和队友伤害
        if (attacker == victim) return HookResult.Continue;
        if (attacker.TeamNum == victim.TeamNum) return HookResult.Continue;

        if (!IsEligible(attacker)) return HookResult.Continue;

        ApplyHpChange(attacker);

        return HookResult.Continue;
    }

    private bool IsEligible(CCSPlayerController player)
    {
        if (!player.PawnIsAlive) return false;

        return _settings.TargetMode switch
        {
            0 => true,
            1 => player.TeamNum == (int)CsTeam.Terrorist,
            2 => player.TeamNum == (int)CsTeam.CounterTerrorist,
            3 => AdminManager.PlayerHasPermissions(player, _settings.VipFlag),
            _ => false
        };
    }

    private void ApplyHpChange(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        int currentHp = pawn.Health;
        int newHp = currentHp;

        if (_settings.HealAmount > 0)
        {
            // 回血逻辑：/hpcap 开启后由全局上限接管；未开启时默认 100。
            int effectiveMax = CaorenCupUtils.GetHpCapMax(_plugin, 100);
            if (currentHp >= effectiveMax) return; // 已经满血或超血，不回
            newHp = Math.Min(currentHp + _settings.HealAmount, effectiveMax);
        }
        else if (_settings.HealAmount < 0)
        {
            // 扣血逻辑：/hpcap 开启后由全局下限接管；未开启时默认 1。
            int effectiveMin = CaorenCupUtils.GetHpCapMin(_plugin, 1);
            if (currentHp <= effectiveMin) return; // 已经跌破或等于下限，不扣
            newHp = Math.Max(currentHp + _settings.HealAmount, effectiveMin);
        }

        if (newHp != currentHp)
        {
            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, newHp);
            newHp = pawn.Health;

            if (_settings.ShowMessage)
            {
                string sign = _settings.HealAmount > 0 ? "+" : "";
                string color = _settings.HealAmount > 0 ? "\u0004" : "\u0002"; // 回血绿，扣血红
                CaorenCupUtils.PrintToChat(player, $"击杀结算: {color}{sign}{_settings.HealAmount} HP \u0001(当前: {newHp})");
            }
        }
    }

    private string GetModeString(int mode) => mode switch { 0 => "所有人", 1 => "T方", 2 => "CT方", 3 => "VIP", _ => "未知" };

    // --- 接口实现 ---
    public string GetHelpEntry() => "/kh - 击杀状态变更 (回血或扣血)";

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return "已禁用";
        string action = _settings.HealAmount >= 0 ? "回血" : "扣血";
        string cap = CaorenCupUtils.IsHpCapEnabled(_plugin) ? $"/hpcap:{_plugin.Config.HpCap.Min}-{_plugin.Config.HpCap.Max}" : "默认范围:1-100";
        return $"已开启 (对象:{GetModeString(_settings.TargetMode)}, {action}:{_settings.HealAmount}, {cap})";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled || _settings.HealAmount == 0) return null;
        string t = GetModeString(_settings.TargetMode);
        string action = _settings.HealAmount > 0 ? "恢复" : "损失";
        string cap = CaorenCupUtils.IsHpCapEnabled(_plugin) ? $"血量控制在 {_plugin.Config.HpCap.Min}~{_plugin.Config.HpCap.Max} 之间" : "默认控制在 1~100 之间";
        return $"[击杀变更] {t}击杀敌人后将{action} {Math.Abs(_settings.HealAmount)}HP ({cap})";
    }

    public string GetFeatureDescription()
    {
        return "【击杀状态变更】杀敌不一定会让你变得更强，也可能让你流血！\n" +
               "- 支持击杀恢复血量，甚至突破上限达到超人状态。\n" +
               "- 同样支持击杀扣血，对高手进行动态惩罚。\n" +
               "- 血量上下限统一由 /hpcap 控制，避免与其他模块互相打架。";
    }
}