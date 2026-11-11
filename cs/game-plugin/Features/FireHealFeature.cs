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
        plugin.RegisterListener<Listeners.OnEntityTakeDamagePre>(OnTakeDamage);
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
            CaorenCup.Contracts.CaorenCupChat.Reply(player, "无效的阵营，请使用 t, ct 或 all。");
            return;
        }

        if (!CaorenCupUtils.TryParseOptionalFloat(info.GetArg(2), _settings.Scale, out float scale))
        {
            CaorenCup.Contracts.CaorenCupChat.Reply(player, "无效的倍率数值。");
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
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/fh 0{ChatColors.Default} : 一键禁用此模块");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/fh <t/ct/all> <倍率>{ChatColors.Default}");
        CaorenCupUtils.PrintToChat(player, $"   倍率 0 : 踩火免疫伤害");
        CaorenCupUtils.PrintToChat(player, $"   倍率 1 : 正常受到伤害");
        CaorenCupUtils.PrintToChat(player, $"   倍率 -1: 正常伤害转为回血");
        CaorenCupUtils.PrintToChat(player, $"   倍率 2 : 受到双倍伤害 (类似可推 -2 为双倍回血)");
        CaorenCupUtils.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // 在引擎扣血前缩放火焰伤害，免疫与回血同样覆盖致命伤。

    private HookResult OnTakeDamage(CEntityInstance entity, CTakeDamageInfo damage)
    {
        if (!entity.IsValid || entity.DesignerName != "player") return HookResult.Continue;
        var pawn = entity.As<CCSPlayerPawn>();
        var victim = pawn.Controller.Value?.As<CCSPlayerController>();
        if (victim is not { IsValid: true } || !victim.PawnIsAlive) return HookResult.Continue;
        if (!IsFireDamage(damage.Inflictor.Value?.DesignerName)) return HookResult.Continue;

        var personal = CaorenCup.Contracts.CaorenCupPlayerEffectsAccess.Get(victim);
        if (personal is null && (!_settings.Enabled || !IsTarget(victim))) return HookResult.Continue;
        var scale = personal?.FireScale ?? _settings.Scale;
        if (scale < 0 && damage.Damage > 0)
        {
            var healed = pawn.Health + (int)Math.Round(damage.Damage * -scale);
            if (!CaorenCupUtils.IsHpCapEnabled(_plugin)) healed = Math.Min(healed, pawn.MaxHealth);
            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, healed);
        }
        damage.Damage = Math.Max(0, damage.Damage * scale);
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
