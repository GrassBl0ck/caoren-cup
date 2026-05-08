using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace CaorenCup.Features;

public class BladeAuraFeature : ICaorenFeature
{
    public string FeatureName => "无极剑气(挥刀波)模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;

    private const float BaseKnifeRange = 64.0f;
    private const float LightSlashFront = 34f;
    private const float LightSlashBack = 90f;
    private const float HeavyStabFront = 65f;
    private const float HeavyStabBack = 153f;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;
        _plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
        _plugin.AddCommand("css_aura", "控制剑气效果", OnAuraCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _config.Aura.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnAuraCommand(CCSPlayerController? player, CommandInfo info)
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
                CaorenCupUtils.PrintToChat(player, "用法: \u0004/aura <t/ct/all/0> [击退联动1/0] [衰减倍率] [最低生效伤害]");
                CaorenCupUtils.PrintToChat(player, "说明: \u0001接 \u00040\u0001 为禁用。衰减指每超出原版刀距一倍，伤害衰减多少。");
                CaorenCupUtils.PrintToChat(player, "范例: \u0004/aura all 1 0.5 15 \u0001(全员开启，带击退，50%衰减，伤害衰减到15以下则剑气消散)");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            string msg = "已 \u0002禁用\u0001 剑气模块。";
            if (player != null) CaorenCupUtils.PrintToChatAll(msg); else Console.WriteLine(msg);
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 \u0004t\u0001, \u0004ct\u0001, \u0004all\u0001，或使用 \u00040\u0001 禁用。");
            return;
        }

        bool allowKb = _config.Aura.AllowKb;
        float decay = _config.Aura.DecayRate;
        int minDmg = _config.Aura.MinDamage;

        if (argCount >= 3) allowKb = info.GetArg(2) == "1";
        if (argCount >= 4) float.TryParse(info.GetArg(3), out decay);
        if (argCount >= 5) int.TryParse(info.GetArg(4), out minDmg);

        _config.Aura.Enabled = true;
        _config.Aura.Target = targetArg;
        _config.Aura.AllowKb = allowKb;
        _config.Aura.DecayRate = Math.Clamp(decay, 0f, 1f);
        _config.Aura.MinDamage = Math.Max(1, minDmg); // 至少为 1
        _plugin.SaveConfig();

        string kbStr = allowKb ? "开启" : "关闭";
        CaorenCupUtils.PrintToChatAll($"剑气已更新 -> 阵营: \u0004{targetArg.ToUpper()}\u0001 | 击退: \u0004{kbStr}\u0001 | 折减: \u0004{decay * 100}%\u0001 | 溃散阈值: \u0004{minDmg}\u0001");
    }

    // --- 核心：剑气挥出逻辑 ---
    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!_config.Aura.Enabled) return HookResult.Continue;

        var attacker = @event.Userid;
        if (attacker == null || !attacker.IsValid || !attacker.PawnIsAlive) return HookResult.Continue;

        string weaponName = @event.Weapon;
        if (!weaponName.Contains("knife") && !weaponName.Contains("bayonet")) return HookResult.Continue;

        bool isMatch = _config.Aura.Target == "all" ||
                      (_config.Aura.Target == "t" && attacker.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.Aura.Target == "ct" && attacker.TeamNum == (byte)CsTeam.CounterTerrorist);
        if (!isMatch) return HookResult.Continue;

        var aPawn = attacker.PlayerPawn.Value;
        if (aPawn == null) return HookResult.Continue;

        bool isHeavy = (attacker.Buttons & PlayerButtons.Attack2) != 0;

        float eyeZ = aPawn.AbsOrigin!.Z + ((aPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46f : 64f);
        Vector eyePos = new Vector(aPawn.AbsOrigin.X, aPawn.AbsOrigin.Y, eyeZ);

        float yaw = aPawn.EyeAngles!.Y * (float)(Math.PI / 180.0);
        float pitch = aPawn.EyeAngles.X * (float)(Math.PI / 180.0);

        Vector aForward = new Vector(
            (float)(Math.Cos(yaw) * Math.Cos(pitch)),
            (float)(Math.Sin(yaw) * Math.Cos(pitch)),
            (float)(-Math.Sin(pitch))
        );

        foreach (var enemy in Utilities.GetPlayers())
        {
            if (enemy == null || !enemy.IsValid || !enemy.PawnIsAlive || enemy.TeamNum == attacker.TeamNum) continue;

            var ePawn = enemy.PlayerPawn.Value;
            if (ePawn == null) continue;

            Vector enemyBody = new Vector(ePawn.AbsOrigin!.X, ePawn.AbsOrigin.Y, ePawn.AbsOrigin.Z + 32f);

            Vector dirToEnemy = new Vector(enemyBody.X - eyePos.X, enemyBody.Y - eyePos.Y, enemyBody.Z - eyePos.Z);
            float distToEnemy = (float)Math.Sqrt(dirToEnemy.X * dirToEnemy.X + dirToEnemy.Y * dirToEnemy.Y + dirToEnemy.Z * dirToEnemy.Z);

            if (distToEnemy == 0) continue;
            dirToEnemy = new Vector(dirToEnemy.X / distToEnemy, dirToEnemy.Y / distToEnemy, dirToEnemy.Z / distToEnemy);

            float dotCone = (aForward.X * dirToEnemy.X) + (aForward.Y * dirToEnemy.Y) + (aForward.Z * dirToEnemy.Z);
            if (dotCone < 0.95f) continue;

            float eYaw = ePawn.EyeAngles!.Y * (float)(Math.PI / 180.0);
            Vector eForward = new Vector((float)Math.Cos(eYaw), (float)Math.Sin(eYaw), 0);
            Vector aForward2D = new Vector((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0);

            float backstabDot = (aForward2D.X * eForward.X) + (aForward2D.Y * eForward.Y);
            bool isBackstab = backstabDot > 0.5f;

            float baseDamage = isHeavy ? (isBackstab ? HeavyStabBack : HeavyStabFront) : (isBackstab ? LightSlashBack : LightSlashFront);

            float multiplier = 1.0f;
            if (distToEnemy > BaseKnifeRange)
            {
                float excessRatios = (distToEnemy - BaseKnifeRange) / BaseKnifeRange;
                multiplier = (float)Math.Pow(1.0f - _config.Aura.DecayRate, excessRatios);
            }

            int finalDamage = (int)Math.Round(baseDamage * multiplier);

            // 【核心修改点：伤害截断】
            // 如果伤害经过长距离衰减后，低于设定的最低阈值（如15），则判定为“剑气溃散”，不再造成任何效果！
            if (finalDamage < _config.Aura.MinDamage) continue;

            // 施加伤害
            ApplyAuraDamage(ePawn, finalDamage);

            // 爽快反馈给开枪者
            attacker.PrintToCenterHtml($"<font color='cyan'>☄ 击中敌方! 剑气伤害: {finalDamage}</font>");

            // 【核心修改点：消除莫名其妙感的信息反馈】
            // 给受击者发送警告提示，让他们知道自己是被什么东西打中的
            enemy.PrintToCenterHtml($"<font color='red'>⚠ 警告：遭到穿墙剑气波及 (-{finalDamage} HP) ⚠</font>");

            // 联动击退模块
            if (_config.Aura.AllowKb && _config.Kb.Enabled)
            {
                ApplyLinkedKnockback(ePawn, aPawn, finalDamage);
            }
        }

        return HookResult.Continue;
    }

    private void ApplyAuraDamage(CCSPlayerPawn victimPawn, int damage)
    {
        if (victimPawn.Health <= 0) return;

        int newHp = victimPawn.Health - damage;
        int minHp = CaorenCupUtils.GetHpCapMin(_plugin, 1);
        if (newHp < minHp)
        {
            newHp = minHp;
        }
        CaorenCupUtils.ApplyModuleHealth(_plugin, victimPawn, newHp);
        victimPawn.EmitSound("Player.Damage");
    }

    private void ApplyLinkedKnockback(CCSPlayerPawn victimPawn, CCSPlayerPawn attackerPawn, int damage)
    {
        float scale = (damage / 50.0f) * _config.Kb.Multiplier;

        float dx = victimPawn.AbsOrigin!.X - attackerPawn.AbsOrigin!.X;
        float dy = victimPawn.AbsOrigin.Y - attackerPawn.AbsOrigin.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);

        Vector pushDir;
        if (length < 1.0f)
            pushDir = new Vector(1, 0, 0);
        else
            pushDir = new Vector(dx / length, dy / length, 0);

        float finalHorizontal = _config.Kb.Horizontal * scale;
        Vector newVelocity = new Vector(
            pushDir.X * finalHorizontal,
            pushDir.Y * finalHorizontal,
            _config.Kb.Vertical
        );

        victimPawn.Teleport(null, null, newVelocity);
    }

    public string GetHelpEntry() => "/aura - 无极剑气(挥刀远程伤害)管理";

    public string GetStatusInfo() => _config.Aura.Enabled ? $"已开启 (阵营:{_config.Aura.Target.ToUpper()} 击退:{_config.Aura.AllowKb} 阈值:{_config.Aura.MinDamage})" : "已禁用";

    public string? GetPublicConfigInfo() => _config.Aura.Enabled ? $"无极剑气: \u0004{_config.Aura.Target.ToUpper()}\u0001 阵营可挥出无视墙体的剑气" : null;

    public string GetFeatureDescription()
    {
        if (!_config.Aura.Enabled)
            return "近战武器依然只能在贴身肉搏中发挥作用。";

        string target = _config.Aura.Target.ToUpper() == "ALL" ? "所有" : _config.Aura.Target.ToUpper();
        string kbInfo = _config.Aura.AllowKb && _config.Kb.Enabled ? "\n若动能打击模块激活，被剑气扫中同样会被隔空震飞！" : "";

        return $"【无极剑气法则】此乃修仙绝学！{target} 阵营在挥刀时，会顺着准星劈出一道无视掩体的直线剑气！\n原版的轻重刀与背刺倍率均对剑气生效。但剑气飞行距离越远，威力越弱；当伤害衰减至低于 \u0004{_config.Aura.MinDamage}\u0001 点时，剑气便会彻底溃散。{kbInfo}";
    }
}