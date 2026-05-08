using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features;

public class IncDmgFeature : ICaorenFeature
{
    public string FeatureName => "动态时间伤害模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;

    private float _roundStartTime = 0f;
    private bool _isRoundActive = false;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册回合生命周期事件以计时
        _plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        _plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        // 注册伤害事件进行血量补偿
        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

        // 注册控制指令
        _plugin.AddCommand("css_incdmg", "控制动态时间伤害", OnIncDmgCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload()
    {
        _isRoundActive = false;
    }

    public void SetEnabled(bool enabled)
    {
        _config.IncDmg.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnIncDmgCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "无权限执行此指令。");
            return;
        }

        int argCount = info.ArgCount;

        if (argCount == 1)
        {
            if (player != null)
            {
                CaorenCupUtils.PrintToChat(player, $"当前状态: \x04{GetStatusInfo()}");
                CaorenCupUtils.PrintToChat(player, "用法: \x04/incdmg <t/ct/all/0> [倍率]");
                CaorenCupUtils.PrintToChat(player, "说明: \x01接 \x040\x01 为禁用模块。倍率为正表增伤(如0.01)，为负表减伤(如-0.01)。");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            string msg = "已 \x02禁用\x01 动态时间伤害。";
            if (player != null) CaorenCupUtils.PrintToChatAll(msg); else Console.WriteLine(msg);
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 \x04t\x01, \x04ct\x01, \x04all\x01，或使用 \x040\x01 禁用。");
            return;
        }

        float rate = _config.IncDmg.Rate;
        if (argCount >= 3)
        {
            if (!float.TryParse(info.GetArg(2), out rate))
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "倍率格式错误，请输入有效的数字 (如 0.01 或 -0.01)。");
                return;
            }
        }

        _config.IncDmg.Enabled = true;
        _config.IncDmg.Target = targetArg;
        _config.IncDmg.Rate = rate;
        _plugin.SaveConfig();

        string msgAll = $"时间伤害 已开启 -> 影响阵营: \x04{targetArg.ToUpper()}\x01 | 5秒递增倍率: \x04{rate}";
        CaorenCupUtils.PrintToChatAll(msgAll);
    }

    // --- 核心时间伤害计算逻辑 ---
    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundStartTime = Server.CurrentTime;
        _isRoundActive = true;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _isRoundActive = false;
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_config.IncDmg.Enabled || !_isRoundActive) return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid) return HookResult.Continue;

        // 检查受害者阵营是否符合目标
        bool isMatch = _config.IncDmg.Target == "all" ||
                      (_config.IncDmg.Target == "t" && victim.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.IncDmg.Target == "ct" && victim.TeamNum == (byte)CsTeam.CounterTerrorist);

        if (!isMatch) return HookResult.Continue;

        // 获取经过的时间，计算系数
        float elapsedTime = Server.CurrentTime - _roundStartTime;
        if (elapsedTime < 5.0f) return HookResult.Continue; // 不足5秒不改变

        int intervals = (int)(elapsedTime / 5.0f);
        float multiplier = intervals * _config.IncDmg.Rate; // 如: (15秒/5) * 0.01 = 0.03

        if (multiplier == 0) return HookResult.Continue;

        int originalDamage = @event.DmgHealth;
        if (originalDamage <= 0) return HookResult.Continue;

        // 计算需要补偿调整的血量
        int diff = (int)Math.Round(originalDamage * multiplier);
        if (diff == 0) return HookResult.Continue;

        var pawn = victim.PlayerPawn?.Value;
        if (pawn == null) return HookResult.Continue;

        // 【增伤】diff 为正，需要进一步扣除血量
        if (diff > 0)
        {
            if (!victim.PawnIsAlive || pawn.Health <= 0) return HookResult.Continue;

            int newHealth = pawn.Health - diff;

            // 为了防止强行扣血导致模型卡死为僵尸，不在此将其强行处死。开启 /hpcap 时使用全局下限，否则保留 1 HP。
            int minHealth = CaorenCupUtils.GetHpCapMin(_plugin, 1);
            if (newHealth < minHealth) newHealth = minHealth;

            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, newHealth);
        }
        // 【减伤】diff 为负，需要补回减免的血量
        else
        {
            // 如果已经被一枪致死(如爆头)，减伤机制无法复活他，只能跳过
            if (!victim.PawnIsAlive || pawn.Health <= 0) return HookResult.Continue;

            int healAmount = -diff; // 变为正数
            int maxHealth = pawn.MaxHealth > 0 ? pawn.MaxHealth : 100;
            maxHealth = CaorenCupUtils.GetHpCapMax(_plugin, maxHealth);
            int newHealth = Math.Min(pawn.Health + healAmount, maxHealth);
            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, newHealth);
        }

        return HookResult.Continue;
    }

    // --- 接口信息实现 ---
    public string GetHelpEntry() => "/incdmg - 动态时间伤害模块管理";

    public string GetStatusInfo() => _config.IncDmg.Enabled ? $"已开启 (阵营:{_config.IncDmg.Target.ToUpper()} 倍率:{_config.IncDmg.Rate})" : "已禁用";

    public string? GetPublicConfigInfo() => _config.IncDmg.Enabled ? $"时间动态伤害: \x04{_config.IncDmg.Target.ToUpper()}\x01 阵营开启" : null;

    // 【聚焦于玩法】给玩家看的介绍，屏蔽代码实现和指令细节
    public string GetFeatureDescription()
    {
        if (!_config.IncDmg.Enabled)
            return "动态时间伤害机制目前处于关闭状态。";

        string target = _config.IncDmg.Target.ToUpper() == "ALL" ? "所有" : _config.IncDmg.Target.ToUpper();
        string effect = _config.IncDmg.Rate > 0 ? "提高，身板变得更加脆弱" : "降低，身板变得更加坚硬";

        return $"【时间伤害机制】在当前回合中，随着时间的流逝，每经过 5 秒钟，{target} 阵营受到的伤害将会{effect}。\n 已知问题：受到伤害的修改不会反映到玩家的造成伤害上。";
    }
}