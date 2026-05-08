using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CaorenCup.Features;

public class OneHpFeature : ICaorenFeature
{
    public string FeatureName => "秽土转生/亡语模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;

    // 追踪每回合触发过的玩家
    private readonly HashSet<uint> _triggeredPlayers = new HashSet<uint>();

    // 追踪锁血无敌名单
    private readonly Dictionary<uint, int> _lockedHpPlayers = new Dictionary<uint, int>();

    // 追踪回合状态，防止回合结束后还强制复活人
    private bool _isRoundActive = false;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        _plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        _plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Pre);
        _plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);

        _plugin.AddCommand("css_1hp", "控制秽土转生与亡语效果", OnOneHpCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload()
    {
        _triggeredPlayers.Clear();
        _lockedHpPlayers.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        _config.OneHp.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 回合生命周期 ---
    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _triggeredPlayers.Clear();
        _lockedHpPlayers.Clear();
        _isRoundActive = true;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _isRoundActive = false;
        return HookResult.Continue;
    }

    // --- 集成指令处理器 ---
    private void OnOneHpCommand(CCSPlayerController? player, CommandInfo info)
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
                CaorenCupUtils.PrintToChat(player, "用法: \u0004/1hp <t/ct/all/0> <模式1/2> [值1] [值2] [值3] [值4]");
                CaorenCupUtils.PrintToChat(player, " \u0004模式 1\u0001 (转生): [值1]=\u000D0原地/1己家/2敌家\u0001, [值2]=\u000D延迟秒数\u0001, [值3]=\u000D复活血量\u0001, [值4]=\u000D无敌秒数");
                CaorenCupUtils.PrintToChat(player, " \u0004模式 2\u0001 (自爆): [值1]=\u000D爆炸伤害\u0001, [值2]=\u000D爆炸半径");
                CaorenCupUtils.PrintToChat(player, "说明: \u0001接 \u00040\u0001 为直接禁用本模块。");
                CaorenCupUtils.PrintToChat(player, "半径参考: \u0001近身\u000464\u0001 | 霰弹\u0004300\u0001 | 炸雷\u0004350\u0001 | C4\u0004500~1500");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            string msg = "已 \u0002禁用\u0001 秽土转生模块。";
            if (player != null) CaorenCupUtils.PrintToChatAll(msg); else Console.WriteLine(msg);
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 \u0004t\u0001, \u0004ct\u0001, \u0004all\u0001，或使用 \u00040\u0001 禁用。");
            return;
        }

        if (argCount < 3 || !int.TryParse(info.GetArg(2), out int mode) || mode < 1 || mode > 2)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "模式代码错误，请使用 1 (转生) 或 2 (自爆)。");
            return;
        }

        float arg1 = _config.OneHp.Arg1;
        float arg2 = _config.OneHp.Arg2;
        float arg3 = _config.OneHp.Arg3;
        float arg4 = _config.OneHp.Arg4;

        if (argCount >= 4) float.TryParse(info.GetArg(3), out arg1);
        if (argCount >= 5) float.TryParse(info.GetArg(4), out arg2);
        if (argCount >= 6) float.TryParse(info.GetArg(5), out arg3);
        if (argCount >= 7) float.TryParse(info.GetArg(6), out arg4);

        _config.OneHp.Enabled = true;
        _config.OneHp.Target = targetArg;
        _config.OneHp.Mode = mode;
        _config.OneHp.Arg1 = arg1;
        _config.OneHp.Arg2 = arg2;
        _config.OneHp.Arg3 = arg3;
        _config.OneHp.Arg4 = arg4;
        _plugin.SaveConfig();

        string modeStr = mode == 1 ? "秽土转生" : "死后自爆";
        CaorenCupUtils.PrintToChatAll($"效果已更新 -> 阵营: \u0004{targetArg.ToUpper()}\u0001 | 模式: \u0004{modeStr}\u0001");
    }

    // --- 锁血机制 ---
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_config.OneHp.Enabled) return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid) return HookResult.Continue;

        if (_lockedHpPlayers.TryGetValue(victim.Index, out int targetHp))
        {
            var pawn = victim.PlayerPawn?.Value;
            if (pawn != null && victim.PawnIsAlive)
            {
                CaorenCupUtils.ApplyModuleSetHealth(_plugin, pawn, targetHp);
            }
        }
        return HookResult.Continue;
    }

    // --- 核心事件逻辑 ---
    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_config.OneHp.Enabled || !_isRoundActive) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        if (_triggeredPlayers.Contains(player.Index)) return HookResult.Continue;

        bool isMatch = _config.OneHp.Target == "all" ||
                      (_config.OneHp.Target == "t" && player.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.OneHp.Target == "ct" && player.TeamNum == (byte)CsTeam.CounterTerrorist);

        if (!isMatch) return HookResult.Continue;

        int mode = _config.OneHp.Mode;

        if (mode == 1) // 模式1：秽土转生
        {
            // 判定是否是全队最后一人，最后一人不能复活，否则回合直接就输了
            int aliveCount = Utilities.GetPlayers().Count(p =>
                p != null && p.IsValid && p.TeamNum == player.TeamNum && p.PawnIsAlive && p.Index != player.Index);

            if (aliveCount > 0)
            {
                ExecuteResurrection(player, (int)_config.OneHp.Arg1, _config.OneHp.Arg2, (int)_config.OneHp.Arg3, _config.OneHp.Arg4);
                _triggeredPlayers.Add(player.Index);
            }
        }
        else if (mode == 2) // 模式2：正常死，自爆
        {
            ExecuteExplosion(player, (int)_config.OneHp.Arg1, _config.OneHp.Arg2);
            _triggeredPlayers.Add(player.Index);
        }

        return HookResult.Continue;
    }

    // --- 秽土转生与神装发放逻辑 ---
    private void ExecuteResurrection(CCSPlayerController player, int locationType, float delay, int targetHp, float invincibilityTime)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        // 提前预存目标坐标 (默认 null 代表使用自带出生点)
        Vector? targetPos = null;
        QAngle? targetAng = null;

        // 如果是 0:原地
        if (locationType == 0)
        {
            targetPos = new Vector(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            targetAng = new QAngle(pawn.AbsRotation!.X, pawn.AbsRotation.Y, pawn.AbsRotation.Z);
        }
        // 如果是 2:敌方出生点
        else if (locationType == 2)
        {
            string enemySpawnType = player.TeamNum == (byte)CsTeam.CounterTerrorist ? "info_player_terrorist" : "info_player_counterterrorist";
            var spawns = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(enemySpawnType).ToList();
            if (spawns.Count > 0)
            {
                // 随机抽取一个敌方出生点
                var randomSpawn = spawns[new Random().Next(spawns.Count)];
                targetPos = new Vector(randomSpawn.AbsOrigin!.X, randomSpawn.AbsOrigin.Y, randomSpawn.AbsOrigin.Z);
                targetAng = new QAngle(randomSpawn.AbsRotation!.X, randomSpawn.AbsRotation.Y, randomSpawn.AbsRotation.Z);
            }
        }
        // 如果是 1:己方出生点，不需要处理 targetPos，因为 Respawn 本身就是己方出生点

        // 开始倒计时转生
        _plugin.AddTimer(delay, () =>
        {
            // 如果玩家退出了或者回合已经结束了，取消转生
            if (!player.IsValid || !_isRoundActive) return;

            player.Respawn(); // 执行复活

            // 延迟一帧，等待模型物理实体构建完毕
            _plugin.AddTimer(0.1f, () =>
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
                var newPawn = player.PlayerPawn.Value;
                if (newPawn == null) return;

                // 1. 传送坐标
                if (targetPos != null && targetAng != null)
                {
                    newPawn.Teleport(targetPos, targetAng, new Vector(0, 0, 0));
                }

                // 2. 发放满配神装
                player.RemoveWeapons();
                player.GiveNamedItem("item_assaultsuit"); // 全甲

                Random rnd = new Random();
                if (player.TeamNum == (byte)CsTeam.Terrorist)
                {
                    player.GiveNamedItem("weapon_knife");
                    player.GiveNamedItem("weapon_glock");
                    player.GiveNamedItem("weapon_ak47");
                    string[] nades = { "weapon_smokegrenade", "weapon_molotov", "weapon_hegrenade", "weapon_flashbang" };
                    player.GiveNamedItem(nades[rnd.Next(nades.Length)]);
                }
                else if (player.TeamNum == (byte)CsTeam.CounterTerrorist)
                {
                    player.GiveNamedItem("weapon_knife");
                    player.GiveNamedItem("weapon_usp_silencer");
                    player.GiveNamedItem("weapon_m4a1_silencer");
                    string[] nades = { "weapon_smokegrenade", "weapon_incgrenade", "weapon_hegrenade", "weapon_flashbang" };
                    player.GiveNamedItem(nades[rnd.Next(nades.Length)]);
                }

                // 3. 设置血量与锁血无敌
                int finalRespawnHp = CaorenCupUtils.ClampModuleSetHealth(_plugin, targetHp);
                CaorenCupUtils.ApplyModuleSetHealth(_plugin, newPawn, finalRespawnHp);

                if (invincibilityTime > 0)
                {
                    _lockedHpPlayers[player.Index] = finalRespawnHp;
                    // 如果给了无敌时间，给玩家一个屏幕提示更爽
                    CaorenCupUtils.PrintToChat(player, $"[\u0006秽土转生\u0001] 你已复活，并获得了 \u0004{invincibilityTime}\u0001 秒的无敌时间！");

                    _plugin.AddTimer(invincibilityTime, () =>
                    {
                        if (player.IsValid)
                        {
                            _lockedHpPlayers.Remove(player.Index);
                            CaorenCupUtils.PrintToChat(player, "[\u0006秽土转生\u0001] 无敌时间结束！");
                        }
                    });
                }
                else
                {
                    CaorenCupUtils.PrintToChat(player, "[\u0006秽土转生\u0001] 你已复苏，重返战场！");
                }
            });
        });
    }

    // --- 模式 2：死亡自爆 ---
    private void ExecuteExplosion(CCSPlayerController player, int damage, float radius)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        Vector center = new Vector(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);

        foreach (var enemy in Utilities.GetPlayers())
        {
            if (enemy == null || !enemy.IsValid || !enemy.PawnIsAlive || enemy.TeamNum == player.TeamNum) continue;

            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null) continue;

            float distance = (enemyPawn.AbsOrigin! - center).Length();
            if (distance <= radius)
            {
                int newHp = enemyPawn.Health - damage;
                if (newHp <= 0 && !CaorenCupUtils.IsHpCapEnabled(_plugin))
                {
                    enemyPawn.Health = 1;
                    enemy.CommitSuicide(false, true);
                }
                else
                {
                    CaorenCupUtils.ApplyModuleHealth(_plugin, enemyPawn, newHp);
                }
            }
        }
    }

    // --- 接口信息实现 ---
    public string GetHelpEntry() => "/1hp - 秽土转生与亡语自爆管理";

    public string GetStatusInfo() => _config.OneHp.Enabled ? $"已开启 (阵营:{_config.OneHp.Target.ToUpper()} 模式:{_config.OneHp.Mode})" : "已禁用";

    public string? GetPublicConfigInfo() => _config.OneHp.Enabled ? $"命运法则: \u0004{_config.OneHp.Target.ToUpper()}\u0001 阵营触发特殊亡语" : null;

    public string GetFeatureDescription()
    {
        if (!_config.OneHp.Enabled)
            return "战场目前没有奇迹降临，倒下即是结束。";

        string target = _config.OneHp.Target.ToUpper() == "ALL" ? "所有" : _config.OneHp.Target.ToUpper();
        string detail = "";

        if (_config.OneHp.Mode == 1)
        {
            string loc = _config.OneHp.Arg1 == 0 ? "原地" : (_config.OneHp.Arg1 == 1 ? "己方老家" : "敌人老家");
            detail = $"受到致命伤倒下后，你的灵魂不会消散！\n在等待 {Math.Round(_config.OneHp.Arg2, 1)} 秒后，你将在 \u0004[{loc}]\u0001 带着全甲与随机道具秽土转生！\n重生后拥有 {Math.Round(_config.OneHp.Arg3)} 点血量及 {Math.Round(_config.OneHp.Arg4, 1)} 秒无敌！";
        }
        else if (_config.OneHp.Mode == 2)
        {
            detail = "倒下时将引爆体内的复仇之力，对周围一定范围内的敌人造成伤害。";
        }

        return $"【命运转生法则】当 {target} 阵营的玩家受到致命伤害时将触发奇迹：\n{detail}\n(注：此效果每位玩家每回合仅限触发一次，且若倒下时为全队最后一名存活者，奇迹将无法发动。)";
    }
}