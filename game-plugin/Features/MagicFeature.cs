using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace CaorenCup.Features;

public class MagicFeature : ICaorenFeature
{
    public string FeatureName => "魔法弹道(磁性吸附)模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;

    // 记录玩家被真实子弹打中的时间，防止魔法伤害与真实伤害在同一帧叠加
    private readonly Dictionary<uint, float> _lastRealHitTime = new Dictionary<uint, float>();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Pre);
        // 核心事件：监听子弹打在物理表面(墙/地)的瞬间，进行轨迹回溯
        _plugin.RegisterEventHandler<EventBulletImpact>(OnBulletImpact, HookMode.Post);

        _plugin.AddCommand("css_magic", "控制魔法弹道吸附", OnMagicCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload()
    {
        _lastRealHitTime.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        _config.Magic.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnMagicCommand(CCSPlayerController? player, CommandInfo info)
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
                CaorenCupUtils.PrintToChat(player, "用法: \u0004/magic <t/ct/all/0> [吸附半径] [单次伤害]");
                CaorenCupUtils.PrintToChat(player, "说明: \u0001接 \u00040\u0001 为禁用模块。");
                CaorenCupUtils.PrintToChat(player, "参考: \u0001半径 \u000430\u0001(微弱辅助) | \u000460\u0001(中等范围) | \u0004100\u0001(夸张吸附)");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            string msg = "已 \u0002禁用\u0001 魔法弹道模块。";
            if (player != null) CaorenCupUtils.PrintToChatAll(msg); else Console.WriteLine(msg);
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 \u0004t\u0001, \u0004ct\u0001, \u0004all\u0001，或使用 \u00040\u0001 禁用。");
            return;
        }

        float radius = _config.Magic.Radius;
        int damage = _config.Magic.Damage;

        if (argCount >= 3) float.TryParse(info.GetArg(2), out radius);
        if (argCount >= 4) int.TryParse(info.GetArg(3), out damage);

        _config.Magic.Enabled = true;
        _config.Magic.Target = targetArg;
        _config.Magic.Radius = radius;
        _config.Magic.Damage = damage;
        _plugin.SaveConfig();

        CaorenCupUtils.PrintToChatAll($"魔法弹道已改变 -> 阵营: \u0004{targetArg.ToUpper()}\u0001 | 半径: \u0004{radius}\u0001 | 伤害: \u0004{damage}\u0001");
    }

    // 记录真实的物理命中，用来防重叠
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (victim != null && victim.IsValid)
        {
            _lastRealHitTime[victim.Index] = Server.CurrentTime;
        }
        return HookResult.Continue;
    }

    // --- 核心：三维空间轨迹判定 ---
    private HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        if (!_config.Magic.Enabled) return HookResult.Continue;

        var shooter = @event.Userid;
        if (shooter == null || !shooter.IsValid || !shooter.PawnIsAlive) return HookResult.Continue;

        // 判断开枪者是否拥有魔法子弹特权
        bool isMatch = _config.Magic.Target == "all" ||
                      (_config.Magic.Target == "t" && shooter.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.Magic.Target == "ct" && shooter.TeamNum == (byte)CsTeam.CounterTerrorist);

        if (!isMatch) return HookResult.Continue;

        var shooterPawn = shooter.PlayerPawn.Value;
        if (shooterPawn == null) return HookResult.Continue;

        // A点: 枪口/眼睛坐标 (估算)
        float eyeZ = shooterPawn.AbsOrigin!.Z + 64f;
        if ((shooterPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0) eyeZ -= 18f; // 蹲下补偿
        Vector posA = new Vector(shooterPawn.AbsOrigin.X, shooterPawn.AbsOrigin.Y, eyeZ);

        // B点: 子弹落点坐标 (墙上)
        Vector posB = new Vector(@event.X, @event.Y, @event.Z);

        // 遍历所有活着的敌人，看他们是否在这个射线的圆柱体半径内
        foreach (var enemy in Utilities.GetPlayers())
        {
            if (enemy == null || !enemy.IsValid || !enemy.PawnIsAlive || enemy.TeamNum == shooter.TeamNum) continue;

            // 避免双重伤害：如果他刚才(0.05秒内)已经被这发子弹真打中了，就不触发魔法
            if (_lastRealHitTime.TryGetValue(enemy.Index, out float lastHit))
            {
                if (Server.CurrentTime - lastHit < 0.05f) continue;
            }

            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null) continue;

            // 提取敌人身体中心(Z+32)和头部(Z+64)的坐标
            Vector enemyBody = new Vector(enemyPawn.AbsOrigin!.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + 32f);
            Vector enemyHead = new Vector(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + 64f);

            // 计算敌人离弹道直线的距离
            float distToBody = GetPointToSegmentDistance(enemyBody, posA, posB);
            float distToHead = GetPointToSegmentDistance(enemyHead, posA, posB);

            // 只要头或身体任意一部位被“磁场”扫到，就触发吸附伤害
            if (distToBody <= _config.Magic.Radius || distToHead <= _config.Magic.Radius)
            {
                ApplyMagicDamage(enemyPawn, shooter, _config.Magic.Damage);

                // 给开枪者一个小提示，爽感拉满
                shooter.PrintToCenterHtml($"<font color='magenta'>⚡ 触发磁场吸附伤害 (-{_config.Magic.Damage}) ⚡</font>");
            }
        }

        return HookResult.Continue;
    }

    // 施加魔法伤害
    private void ApplyMagicDamage(CCSPlayerPawn victimPawn, CCSPlayerController shooter, int damage)
    {
        if (victimPawn.Health <= 0) return;

        int newHealth = victimPawn.Health - damage;

        // 为了防止非物理击杀导致的右上角击杀图标错乱 (变成骷髅头自杀图标)
        // 我们设定“魔法伤害不致死”。开启 /hpcap 时使用全局下限，否则最多打到 1 HP。
        int minHealth = CaorenCupUtils.GetHpCapMin(_plugin, 1);
        if (newHealth < minHealth)
        {
            newHealth = minHealth;
        }

        CaorenCupUtils.ApplyModuleHealth(_plugin, victimPawn, newHealth);

        // 播放受击音效增强反馈
        victimPawn.EmitSound("Player.Damage");
    }

    // --- 数学算法：点到三维线段的最短距离 ---
    private float GetPointToSegmentDistance(Vector P, Vector A, Vector B)
    {
        float abx = B.X - A.X;
        float aby = B.Y - A.Y;
        float abz = B.Z - A.Z;

        float apx = P.X - A.X;
        float apy = P.Y - A.Y;
        float apz = P.Z - A.Z;

        float ab2 = abx * abx + aby * aby + abz * abz;
        // 如果开枪点和落点重合（几乎不可能），直接返回点与点的距离
        if (ab2 == 0f) return (float)Math.Sqrt(apx * apx + apy * apy + apz * apz);

        // 投影求最近点在段上的比例 t
        float t = (apx * abx + apy * aby + apz * abz) / ab2;
        if (t < 0f) t = 0f; // 落点在枪管后方(舍弃)
        if (t > 1f) t = 1f; // 落点在墙后(舍弃，防穿墙魔法)

        // 计算线段上的最近点坐标
        float cx = A.X + t * abx;
        float cy = A.Y + t * aby;
        float cz = A.Z + t * abz;

        // 计算敌人与最近点的距离
        float dx = P.X - cx;
        float dy = P.Y - cy;
        float dz = P.Z - cz;

        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // --- 接口信息实现 ---
    public string GetHelpEntry() => "/magic - 魔法弹道(磁场吸附)控制";

    public string GetStatusInfo() => _config.Magic.Enabled ? $"已开启 (阵营:{_config.Magic.Target.ToUpper()} 半径:{_config.Magic.Radius} 伤害:{_config.Magic.Damage})" : "已禁用";

    public string? GetPublicConfigInfo() => _config.Magic.Enabled ? $"魔法弹道: \u0004{_config.Magic.Target.ToUpper()}\u0001 阵营的子弹带有磁性追踪" : null;

    public string GetFeatureDescription()
    {
        if (!_config.Magic.Enabled)
            return "武器弹道遵循严谨的物理定律。";

        string target = _config.Magic.Target.ToUpper() == "ALL" ? "所有" : _config.Magic.Target.ToUpper();

        return $"【魔法弹道机制】{target} 阵营配备了概念武器！即使你的准星略微偏离了目标，飞行的子弹依然会产生强大的「引力磁场」。只要子弹轨迹与敌人擦肩而过，就会自动吸附并造成隔空打击！\n(注：为保持对决的仪式感，魔法吸附伤害极其致命但不会直接致死，最多会将敌人血量锁定在 1 点，最后的一击必须由实弹完成！)";
    }
}