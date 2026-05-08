using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;

namespace CaorenCup.Features;

public class FireHealFeature : ICaorenFeature
{
    public string FeatureName => "FireHeal (火疗与伤害修改)";

    private CaorenCupPlugin _plugin = null!;
    private FireHealSettings _settings = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型控制台指令
        plugin.AddCommand("css_fh", "火疗/火焰伤害设置: css_fh <t/ct/all/0> <倍率>", OnCommandFh);

        // 伤害事件监听
        plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.FireHeal;
    }

    public void OnUnload()
    {
        // 无需清理 Timer
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
    }

    // --- 核心指令逻辑 ---

    private void OnCommandFh(CCSPlayerController? player, CommandInfo info)
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

        // 2. 参数不足检查
        if (info.ArgCount < 3)
        {
            if (player == null) Console.WriteLine("[草人杯] 参数不足。"); else PrintUsage(player);
            return;
        }

        // 3. 解析并应用后覆盖机制
        string target = arg1;
        if (target != "t" && target != "ct" && target != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的阵营，请使用 t, ct 或 all。");
            return;
        }

        if (!float.TryParse(info.GetArg(2), out float scale))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的倍率数值。");
            return;
        }

        _settings.Target = target;
        _settings.Scale = scale;
        _settings.Enabled = true;

        _plugin.SaveConfig();

        // 根据倍率生成直观的提示文本
        string effectDesc = scale switch
        {
            0 => $"{ChatColors.Green}免疫伤害",
            1 => $"{ChatColors.Default}正常伤害",
            < 0 => $"{ChatColors.Green}回血 {Math.Abs(scale)}x",
            > 0 => $"{ChatColors.Red}受到伤害 {scale}x",
            _ => $"{ChatColors.Default}未知效果"
        };

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用! 目标:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} 效果:{effectDesc}{ChatColors.Default}");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== FireHeal 指令说明 ===");
        player.PrintToChat($" {ChatColors.Green}/fh 0{ChatColors.Default} : 一键禁用此模块");
        player.PrintToChat($" {ChatColors.Green}/fh <t/ct/all> <倍率>{ChatColors.Default}");
        player.PrintToChat($"   倍率 0 : 踩火免疫伤害");
        player.PrintToChat($"   倍率 1 : 正常受到伤害");
        player.PrintToChat($"   倍率 -1: 正常伤害转为回血");
        player.PrintToChat($"   倍率 2 : 受到双倍伤害 (类似可推 -2 为双倍回血)");
        player.PrintToChat($" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏逻辑 (严格受击补偿计算) ---

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid) return HookResult.Continue;

        // 队伍检查
        if (!IsTarget(victim)) return HookResult.Continue;

        // 伤害类型检查 (燃烧弹/燃烧瓶)
        if (!IsFireDamage(@event.Weapon)) return HookResult.Continue;

        var pawn = victim.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return HookResult.Continue;

        int currentHp = pawn.Health; // 这是已经被引擎扣血【后】的血量
        int dmg = @event.DmgHealth;  // 这次事件造成的真实伤害值
        if (dmg <= 0) return HookResult.Continue;

        // 如果玩家已经被火刚好烧死了，并且我们设定的不是回血/免疫(<=0)，那就不干预，让他死。
        // 注：受制于CS2底层机制，如果残血瞬间被烧致死(Hp <= 0)，我们无法在PlayerHurt里复活他。
        //     但对于绝大多数情况，我们的公式都能完美修正血量。
        if (currentHp <= 0 && _settings.Scale > 0) return HookResult.Continue;

        /*
         * 【核心代数补偿公式】
         * 引擎已经执行了: 实际血量 = 原始血量 - dmg
         * 我们的目标是:   期望血量 = 原始血量 - (dmg * Scale)
         * 那么我们需要给玩家补偿: 补偿量 = 期望血量 - 实际血量 = dmg * (1 - Scale)
         * 最终赋值:       新血量 = 实际血量 + 补偿量
         */
        float adjustment = dmg * (1 - _settings.Scale);
        int finalHp = currentHp + (int)Math.Round(adjustment);

        // 极限致死与溢出处理。开启 /hpcap 后，回血/扣血边界完全交给全局 cap。
        if (finalHp <= 0 && !CaorenCupUtils.IsHpCapEnabled(_plugin))
        {
            if (currentHp > 0) pawn.CommitSuicide(false, true); // 如果倍率过高(比如2x)导致该死了，强行处决
        }
        else
        {
            if (!CaorenCupUtils.IsHpCapEnabled(_plugin))
            {
                int maxHp = pawn.MaxHealth > 0 ? pawn.MaxHealth : 100;
                finalHp = Math.Min(finalHp, maxHp);
            }

            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, finalHp);
        }

        return HookResult.Continue;
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;
        if (_settings.Target == "t" && player.Team == CsTeam.Terrorist) return true;
        if (_settings.Target == "ct" && player.Team == CsTeam.CounterTerrorist) return true;
        return false;
    }

    private bool IsFireDamage(string? weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return false;
        string w = weaponName.ToLower();
        return w.Contains("molotov") || w.Contains("inc") || w.Contains("inferno");
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/fh{ChatColors.Default} : 查看并设置 火疗/火焰伤害 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"FireHeal: {ChatColors.Red}已禁用{ChatColors.Default}";
        string effect = _settings.Scale < 0 ? $"回血 {Math.Abs(_settings.Scale)}x" : (_settings.Scale == 0 ? "免疫" : $"受伤 {_settings.Scale}x");
        string cap = CaorenCupUtils.IsHpCapEnabled(_plugin) ? $" | /hpcap:{_plugin.Config.HpCap.Min}-{_plugin.Config.HpCap.Max}" : "";
        return $"FireHeal: {ChatColors.Green}启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 效果:{effect}{cap}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        string target = _settings.Target == "all" ? "全体玩家" : $"{_settings.Target.ToUpper()} 阵营";
        string effect = _settings.Scale < 0 ? $"恢复 {Math.Abs(_settings.Scale)} 倍生命" : (_settings.Scale == 0 ? "免疫伤害" : $"受到 {_settings.Scale} 倍伤害");
        return $"[踩火特效] {target} 在火焰中将会 {effect}。";
    }

    public string GetFeatureDescription()
    {
        return " [FireHeal] 站在燃烧弹/燃烧瓶的火焰中，根据设定的倍率修改受到的伤害。\n" +
               " 战术博弈功能：可以免疫火伤、双倍伤害、甚至是利用敌人的火来回血！";
    }
}