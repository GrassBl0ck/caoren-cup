using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace CaorenCup.Features;

public class KbFeature : ICaorenFeature
{
    public string FeatureName => "动能打击(物理击退)模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;

    // 记录玩家上次受击飞的时间，防止霰弹枪一枪8个弹丸把人推到外太空
    private readonly Dictionary<uint, float> _lastKbTime = new Dictionary<uint, float>();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);
        _plugin.AddCommand("css_kb", "控制动能击退效果", OnKbCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload()
    {
        _lastKbTime.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        _config.Kb.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnKbCommand(CCSPlayerController? player, CommandInfo info)
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
                CaorenCupUtils.PrintToChat(player, $"当前状态: \u0004{GetStatusInfo()}");
                CaorenCupUtils.PrintToChat(player, "用法: \u0004/kb <t/ct/all/0> [水平力] [垂直力] [友军生效1/0] [伤害倍数]");
                CaorenCupUtils.PrintToChat(player, "说明: \u0001接 \u00040\u0001 为禁用模块。");
                CaorenCupUtils.PrintToChat(player, "参数解构: \u000D友军生效\u0001设为1时，打队友和自己扔雷都会被炸飞。");
                CaorenCupUtils.PrintToChat(player, "范例: \u0004/kb all 400 250 1 2.0\u0001 (全员生效，水平400，垂直250，友伤开，倍数2)");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            string msg = "已 \u0002禁用\u0001 动能打击模块。";
            if (player != null) CaorenCupUtils.PrintToChatAll(msg); else Console.WriteLine(msg);
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 \u0004t\u0001, \u0004ct\u0001, \u0004all\u0001，或使用 \u00040\u0001 禁用。");
            return;
        }

        // 读取参数，提供默认容错
        float horiz = _config.Kb.Horizontal;
        float vert = _config.Kb.Vertical;
        bool friendly = _config.Kb.Friendly;
        float mult = _config.Kb.Multiplier;

        if (argCount >= 3) float.TryParse(info.GetArg(2), out horiz);
        if (argCount >= 4) float.TryParse(info.GetArg(3), out vert);
        if (argCount >= 5) friendly = info.GetArg(4) == "1";
        if (argCount >= 6) float.TryParse(info.GetArg(5), out mult);

        _config.Kb.Enabled = true;
        _config.Kb.Target = targetArg;
        _config.Kb.Horizontal = horiz;
        _config.Kb.Vertical = vert;
        _config.Kb.Friendly = friendly;
        _config.Kb.Multiplier = mult;
        _plugin.SaveConfig();

        string frStr = friendly ? "开启" : "关闭";
        CaorenCupUtils.PrintToChatAll($"动能打击已更新 -> 发起方: \u0004{targetArg.ToUpper()}\u0001 | 水平:\u0004{horiz}\u0001 垂直:\u0004{vert}\u0001 | 友伤:\u0004{frStr}\u0001 | 伤害系数:\u0004{mult}\u0001");
    }

    // --- 核心：三维击退矢量计算 ---
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_config.Kb.Enabled) return HookResult.Continue;

        var attacker = @event.Attacker;
        var victim = @event.Userid;

        // 基础验证，确保被打的人还活着（不击飞尸体，防止物理引擎崩溃）
        if (attacker == null || !attacker.IsValid || victim == null || !victim.IsValid || !victim.PawnIsAlive)
            return HookResult.Continue;

        var victimPawn = victim.PlayerPawn.Value;
        if (victimPawn == null || victimPawn.Health <= 0) return HookResult.Continue;

        // 阵营验证：开枪者是否拥有“动能弹”权限
        bool isMatch = _config.Kb.Target == "all" ||
                      (_config.Kb.Target == "t" && attacker.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.Kb.Target == "ct" && attacker.TeamNum == (byte)CsTeam.CounterTerrorist);
        if (!isMatch) return HookResult.Continue;

        // 友军与自我伤害验证
        if (!_config.Kb.Friendly && attacker.TeamNum == victim.TeamNum)
            return HookResult.Continue;

        // 霰弹枪/手雷多重判定冷却 (0.05秒内只受一次力)
        if (_lastKbTime.TryGetValue(victim.Index, out float lastTime))
        {
            if (Server.CurrentTime - lastTime < 0.05f) return HookResult.Continue;
        }
        _lastKbTime[victim.Index] = Server.CurrentTime;

        // === 【数学换算区】 ===

        // 1. 伤害倍率缩放 (完全还原你提供的算法)
        // 以 25 伤害为 1 倍基准。伤害 / 50.0 * 乘数 = 最终倍率
        int damage = @event.DmgHealth;
        float scale = (damage / 50.0f) * _config.Kb.Multiplier;

        // 2. 向量方向计算
        var aPos = attacker.PlayerPawn.Value?.AbsOrigin;
        var vPos = victimPawn.AbsOrigin;
        if (aPos == null || vPos == null) return HookResult.Continue;

        float dx = vPos.X - aPos.X;
        float dy = vPos.Y - aPos.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);

        Vector pushDir;

        // 如果距离极近（或者炸弹炸自己），强制让他们往“自己面向的反方向”飞
        if (length < 1.0f)
        {
            float yaw = victimPawn.EyeAngles!.Y * (float)(Math.PI / 180.0);
            pushDir = new Vector(-(float)Math.Cos(yaw), -(float)Math.Sin(yaw), 0);
        }
        else
        {
            pushDir = new Vector(dx / length, dy / length, 0); // 纯水平单位向量
        }

        // 3. 计算最终速度矢量
        // 水平力 = 基础水平 * 伤害缩放倍率
        // 垂直力 = 固定基础垂直 (让你设计的只影响水平的思路完美落地)
        float finalHorizontal = _config.Kb.Horizontal * scale;
        Vector newVelocity = new Vector(
            pushDir.X * finalHorizontal,
            pushDir.Y * finalHorizontal,
            _config.Kb.Vertical
        );

        // 4. 施加动能 (通过 Teleport 设置速度矢量)
        victimPawn.Teleport(null, null, newVelocity);

        return HookResult.Continue;
    }

    // --- 接口信息实现 ---
    public string GetHelpEntry() => "/kb - 动能打击(伤害物理击退)管理";

    public string GetStatusInfo() => _config.Kb.Enabled ? $"已开启 (阵营:{_config.Kb.Target.ToUpper()} 水平:{_config.Kb.Horizontal} 友军击退:{_config.Kb.Friendly})" : "已禁用";

    public string? GetPublicConfigInfo() => _config.Kb.Enabled ? $"动能打击: \u0004{_config.Kb.Target.ToUpper()}\u0001 阵营的子弹带有夸张的物理击退力" : null;

    public string GetFeatureDescription()
    {
        if (!_config.Kb.Enabled)
            return "武器动能恢复正常，不再产生物理击退。";

        string target = _config.Kb.Target.ToUpper() == "ALL" ? "所有" : _config.Kb.Target.ToUpper();
        string friendlyText = _config.Kb.Friendly ? "而且这种恐怖的动能连\u0004队友甚至自己\u0001都无法免疫（你可以尝试用高爆手雷进行火箭跳）！" : "此效果对己方人员免疫。";

        return $"【动能打击机制】{target} 阵营配备了超重型动能弹药！\n当敌人被击中时，巨大的冲击力会将他们直接掀飞！受到的\u0004伤害越高，被击退的距离就越远\u0001（比如被狙击枪打中会像炮弹一样飞出去）。\n{friendlyText}";
    }
}