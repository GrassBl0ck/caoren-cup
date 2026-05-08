using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Linq;

namespace CaorenCup.Features;

public class FOVFeature : ICaorenFeature
{
    public string FeatureName => "Player FOV (视角广度)";

    private FOVSettings _settings = null!;
    private CaorenCupPlugin _plugin = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型控制台指令
        plugin.AddCommand("css_fov", "设置玩家FOV: css_fov <t/ct/all/0> <30-150>", OnCommandFov);

        // 注册事件
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.FOV;
    }

    public void OnUnload()
    {
        ResetAllPlayers();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled)
        {
            ResetAllPlayers(); // 禁用时立即重置所有人
        }
    }

    // --- 核心指令逻辑 ---

    private void OnCommandFov(CCSPlayerController? player, CommandInfo info)
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

        // 1. 一键禁用并重置
        if (arg1 == "0" || arg1 == "off")
        {
            SetEnabled(false);
            _plugin.SaveConfig();
            CaorenCupUtils.PrintToChatAll(CaorenCupUtils.FormatChangeMessage("模块控制", FeatureName, $"{ChatColors.Red}已禁用 (全员FOV恢复90)"));
            return;
        }

        // 2. 检查参数
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

        if (!int.TryParse(info.GetArg(2), out int fovVal))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的 FOV 数值。");
            return;
        }

        // 限制数值范围 30 - 150
        int clampedFov = Math.Clamp(fovVal, 30, 150);

        // 应用配置
        _settings.Target = target;
        _settings.FovValue = clampedFov;
        _settings.Enabled = true;

        _plugin.SaveConfig();

        // 立即应用到场上玩家 (含清理非目标玩家逻辑)
        ApplyToTargets();

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用! 目标:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} 广度:{clampedFov}");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== FOV 指令说明 ===");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/fov 0{ChatColors.Default} : 一键禁用并恢复全员FOV至90");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/fov <t/ct/all> <30-150>{ChatColors.Default}");
        CaorenCupUtils.PrintToChat(player, $" 示例: /fov t 120 (T阵营广角视野)");
        CaorenCupUtils.PrintToChat(player, $" 示例: /fov all 60 (全员拉近视野)");
        CaorenCupUtils.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏逻辑 ---

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        // 如果模块未启用，顺手确保他复活是默认 90
        if (!_settings.Enabled)
        {
            _plugin.AddTimer(0.1f, () => { if (player.IsValid) SetPlayerFov(player, 90); });
            return HookResult.Continue;
        }

        // 延迟一帧确保 Pawn 初始化完成
        _plugin.AddTimer(0.1f, () =>
        {
            if (player == null || !player.IsValid) return;

            if (IsTarget(player))
            {
                SetPlayerFov(player, (uint)_settings.FovValue);
            }
            else
            {
                SetPlayerFov(player, 90); // 确保非目标阵营玩家维持正常视野
            }
        });

        return HookResult.Continue;
    }

    // 智能应用：符合条件的设为目标值，不符合的设回 90
    private void ApplyToTargets()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;

            if (_settings.Enabled && IsTarget(player))
            {
                SetPlayerFov(player, (uint)_settings.FovValue);
            }
            else
            {
                SetPlayerFov(player, 90);
            }
        }
    }

    // 强制重置所有人为 90
    private void ResetAllPlayers()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;
            SetPlayerFov(player, 90);
        }
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;
        if (_settings.Target == "ct" && player.Team == CsTeam.CounterTerrorist) return true;
        if (_settings.Target == "t" && player.Team == CsTeam.Terrorist) return true;
        return false;
    }

    private void SetPlayerFov(CCSPlayerController player, uint fov)
    {
        if (!player.PawnIsAlive) return;

        player.DesiredFOV = fov;
        Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/fov{ChatColors.Default} : 查看并设置 玩家FOV 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"FOV: {ChatColors.Red}已禁用{ChatColors.Default}";
        return $"FOV: {ChatColors.Green}启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 数值:{_settings.FovValue}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        string target = _settings.Target == "all" ? "全员" : $"{_settings.Target.ToUpper()} 阵营";
        return $"[异变视野] {target} 的视角广度被强制锁定为 {_settings.FovValue}。";
    }

    public string GetFeatureDescription()
    {
        return " [FOV] 调整玩家的第一人称视角广度(FOV)。\n" +
               " - 数值越大(>90)视野越宽(类似鱼眼镜头，边缘会拉伸)。\n" +
               " - 数值越小(<90)视野越窄(自带望远镜效果，但丧失周围视野)。\n" +
               " - 输入 /fov 0 即可一键恢复所有人到正常的 90 视野。";
    }
}