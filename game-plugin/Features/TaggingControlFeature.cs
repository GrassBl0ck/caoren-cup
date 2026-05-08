using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features;

public class TaggingControlFeature : ICaorenFeature
{
    public string FeatureName => "受击速度管理 (TaggingControl)";

    private CaorenCupPlugin _plugin = null!;
    private TaggingControlSettings _settings = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 核心：在 Hurt 事件之后修改速度倍率
        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);

        // 注册唯一控制指令
        _plugin.AddCommand("css_tag", "受击速度控制", OnCommandTag);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.TaggingControl;
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnCommandTag(CCSPlayerController? player, CommandInfo info)
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
                    CaorenCupUtils.PrintToChat(player, $"\u0004用法: \u0001/tag <t/ct/all/0> <0.0~1.0/df>");
                    CaorenCupUtils.PrintToChat(player, $"\u0001详细帮助已打印到控制台~");
                }
                else
                {
                    player.PrintToConsole("========== [草人杯] 受击速度管理 (Tagging) ==========");
                    player.PrintToConsole($"当前状态: {GetStatusInfo()}");
                    player.PrintToConsole("用法: css_tag <目标/0> <倍率/df>");
                    player.PrintToConsole("  输入 0 直接禁用此模块。");
                    player.PrintToConsole("  目标: t, ct, all");
                    player.PrintToConsole("  倍率: 0.0(定身) 到 1.0(无减速)，输入 df 恢复官方默认减速。");
                    player.PrintToConsole("范例1: css_tag t 1.0 (T方被击中时完全不减速)");
                    player.PrintToConsole("范例2: css_tag ct df (CT方恢复官方受击减速)");
                    player.PrintToConsole("======================================================");
                }
            }
            return;
        }

        string arg1 = info.GetArg(1).ToLower();

        // 核心规范：0键禁用
        if (arg1 == "0" || arg1 == "off" || arg1 == "false")
        {
            SetEnabled(false);
            CaorenCupUtils.PrintToChatAll($"\u0002已禁用\u0001 受击速度管理模块。");
            return;
        }

        if (arg1 == "t" || arg1 == "ct" || arg1 == "all")
        {
            if (info.ArgCount < 3)
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "参数不足，请指定数值 (0.0~1.0) 或 df。");
                return;
            }

            string valStr = info.GetArg(2).ToLower();
            bool isCustom = true;
            float val = 1.0f;

            if (valStr == "df" || valStr == "default")
            {
                isCustom = false;
            }
            else if (float.TryParse(valStr, out float parsedVal))
            {
                val = Math.Clamp(parsedVal, 0.0f, 1.0f);
            }
            else
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "数值错误，请输入 0.0~1.0 之间的数字，或 df。");
                return;
            }

            bool applyT = (arg1 == "t" || arg1 == "all");
            bool applyCT = (arg1 == "ct" || arg1 == "all");

            if (applyT) { _settings.CustomT = isCustom; _settings.ValueT = val; }
            if (applyCT) { _settings.CustomCT = isCustom; _settings.ValueCT = val; }

            _settings.Enabled = true;
            _plugin.SaveConfig();

            string actionDesc = isCustom ? $"锁定为 \u0004{val}x\u0001" : $"恢复为 \u0004原生默认减速\u0001";
            string teamName = arg1.ToUpper() == "ALL" ? "全体玩家" : $"{arg1.ToUpper()}方";
            CaorenCupUtils.PrintToChatAll($"受击速度已更新 -> \u0007{teamName}\u0001 的受击倍率已{actionDesc}。");
        }
        else
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "目标错误，请使用 t / ct / all / 0。");
        }
    }

    // --- 游戏逻辑 ---

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var hurtPlayer = @event.Userid;
        if (hurtPlayer == null || !hurtPlayer.IsValid || !hurtPlayer.PawnIsAlive) return HookResult.Continue;

        int team = hurtPlayer.TeamNum;
        bool useCustom = false;
        float targetValue = 1.0f;

        if (team == (byte)CsTeam.Terrorist)
        {
            useCustom = _settings.CustomT;
            targetValue = _settings.ValueT;
        }
        else if (team == (byte)CsTeam.CounterTerrorist)
        {
            useCustom = _settings.CustomCT;
            targetValue = _settings.ValueCT;
        }

        // 如果是原生设定，直接跳过，不干涉游戏底层
        if (!useCustom) return HookResult.Continue;

        // 必须在 NextFrame 执行，否则会被游戏底层本身的减速逻辑覆盖
        Server.NextFrame(() =>
        {
            if (hurtPlayer == null || !hurtPlayer.IsValid || !hurtPlayer.PawnIsAlive) return;
            var pawn = hurtPlayer.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.VelocityModifier = targetValue;
        });

        return HookResult.Continue;
    }

    // --- 接口实现 ---

    public string GetHelpEntry() => "/tag - 自由受击速度倍率管理";

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return "已禁用";
        string tState = _settings.CustomT ? $"{_settings.ValueT:F1}x" : "原生";
        string ctState = _settings.CustomCT ? $"{_settings.ValueCT:F1}x" : "原生";
        return $"已开启 (T:{tState} | CT:{ctState})";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled || (!_settings.CustomT && !_settings.CustomCT)) return null;
        string tState = _settings.CustomT ? $"{_settings.ValueT}x" : "原生";
        string ctState = _settings.CustomCT ? $"{_settings.ValueCT}x" : "原生";
        return $"[受击粘滞] T方[{tState}] CT方[{ctState}] (1.0x代表被击中不减速)";
    }

    public string GetFeatureDescription()
    {
        return "【受击减速修改】改变被子弹击中时的减速(Tagging)惩罚。\n" +
               "- 可设置 0.0(中弹定身) 到 1.0(中弹毫无减速，跑男专属) 之间的任意倍率。\n" +
               "- 支持阵营独立设置。";
    }
}