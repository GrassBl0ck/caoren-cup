using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CaorenCup.Features;

public class LhImmFeature : ICaorenFeature
{
    public string FeatureName => "名刀无敌 (LhImm)";

    private CaorenCupPlugin _plugin = null!;

    private readonly LhImmRuntimeSettings _settings = new();

    private readonly Dictionary<uint, ImmuneState> _immunePlayers = new();
    private readonly HashSet<uint> _usedThisLife = new();
    private readonly Dictionary<uint, LastAliveState> _lastAliveStates = new();

    private Timer? _tickTimer;

    private sealed class LhImmRuntimeSettings
    {
        public bool Enabled { get; set; } = false;
        public string Target { get; set; } = "all";
        public float ImmuneTime { get; set; } = 3.0f;
        public float ExtraSpeedPercent { get; set; } = 50.0f;
    }

    private sealed class ImmuneState
    {
        public float ExpireAt { get; set; }
        public float SpeedMultiplier { get; set; }
        public float RestoreVelocityModifier { get; set; }
    }

    private sealed class LastAliveState
    {
        public Vector Position { get; set; } = new(0, 0, 0);
        public QAngle Angle { get; set; } = new(0, 0, 0);
        public int TeamNum { get; set; }
    }

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        plugin.AddCommand("css_lhimm", "名刀无敌: /lhimm <t/ct/all/0> <Time> <extraSpeed>", OnCommandLhImm);
        plugin.AddCommand("lhimm", "名刀无敌: /lhimm <t/ct/all/0> <Time> <extraSpeed>", OnCommandLhImm);

        /*
         * 关键修复：
         * 不再使用 OnPlayerTakeDamagePre。
         * 改用 OnEntityTakeDamagePre，再判断 entity 是否为 player。
         * 这个钩子对 CS2 伤害修改更稳。
         */
        plugin.RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);

        plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);
        plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        // 独立运行版，不写入 CaorenCup.json。
    }

    public void OnUnload()
    {
        StopTickTimer();
        ClearImmunePlayers(restoreSpeed: true);
        _usedThisLife.Clear();
        _lastAliveStates.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;

        if (!enabled)
        {
            StopTickTimer();
            ClearImmunePlayers(restoreSpeed: true);
            _usedThisLife.Clear();
            _lastAliveStates.Clear();
        }
    }

    private void OnCommandLhImm(CCSPlayerController? player, CommandInfo info)
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

        string targetArg = info.GetArg(1).Trim().ToLowerInvariant();

        if (targetArg == "0" || targetArg == "off" || targetArg == "disable")
        {
            SetEnabled(false);
            CaorenCupUtils.PrintToChatAll($"{ChatColors.Red}名刀无敌已禁用。");
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            Reply(player, "目标错误，请使用 t / ct / all / 0。");
            return;
        }

        if (info.ArgCount < 4)
        {
            PrintUsage(player);
            return;
        }

        if (!float.TryParse(info.GetArg(2), out float immuneTime))
        {
            Reply(player, "无效的无敌时间，请输入秒数。");
            return;
        }

        if (!float.TryParse(info.GetArg(3), out float extraSpeed))
        {
            Reply(player, "无效的额外速度，请输入百分比，例如 50 表示额外 50%。");
            return;
        }

        immuneTime = Math.Clamp(immuneTime, 0.0f, 60.0f);
        extraSpeed = Math.Clamp(extraSpeed, 0.0f, 500.0f);

        _settings.Enabled = true;
        _settings.Target = targetArg;
        _settings.ImmuneTime = immuneTime;
        _settings.ExtraSpeedPercent = extraSpeed;

        ClearImmunePlayers(restoreSpeed: true);
        _usedThisLife.Clear();
        _lastAliveStates.Clear();

        EnsureTickTimer();

        CaorenCupUtils.PrintToChatAll(
            $"{ChatColors.Green}名刀无敌已启用！{ChatColors.Default} " +
            $"目标:{ChatColors.Green}{GetTargetDisplayName(targetArg)}{ChatColors.Default} | " +
            $"无敌:{ChatColors.Green}{immuneTime:F1}秒{ChatColors.Default} | " +
            $"额外速度:{ChatColors.Green}+{extraSpeed:F0}%{ChatColors.Default}"
        );
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "=== 名刀无敌 LhImm ===");
        Reply(player, $"{ChatColors.Green}/lhimm 0{ChatColors.Default} : 关闭名刀无敌");
        Reply(player, $"{ChatColors.Green}/lhimm <t/ct/all> <Time> <extraSpeed>{ChatColors.Default}");
        Reply(player, $"示例: {ChatColors.Green}/lhimm t 3 50{ChatColors.Default}  T方触发名刀后3秒无敌，额外50%速度");
        Reply(player, $"示例: {ChatColors.Green}/lhimm all 2.5 0{ChatColors.Default}  全体触发名刀后2.5秒无敌，不加速");
        Reply(player, "说明: 每名玩家每条命只能触发一次名刀，重生后重置。");
        Reply(player, $"当前状态: {GetStatusInfo()}");
    }

    /*
     * 核心伤害拦截。
     * 这版使用 OnEntityTakeDamagePre。
     */
    private HookResult OnEntityTakeDamagePre(CEntityInstance entity, CTakeDamageInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;
        if (entity == null || !entity.IsValid) return HookResult.Continue;
        if (info == null || info.Damage <= 0.0f) return HookResult.Continue;

        if (entity.DesignerName != "player") return HookResult.Continue;

        CCSPlayerPawn victimPawn;

        try
        {
            victimPawn = entity.As<CCSPlayerPawn>();
        }
        catch
        {
            return HookResult.Continue;
        }

        if (victimPawn == null || !victimPawn.IsValid) return HookResult.Continue;

        var victim = ResolveController(victimPawn);
        if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return HookResult.Continue;
        if (!IsTarget(victim)) return HookResult.Continue;

        uint idx = victim.Index;

        // 已经处于名刀无敌中：所有伤害直接抹掉。
        if (_immunePlayers.TryGetValue(idx, out var immune))
        {
            if (Server.CurrentTime < immune.ExpireAt)
            {
                info.Damage = 0.0f;
                ForceHealth(victimPawn, 1);
                return HookResult.Continue;
            }

            EndImmune(victim, idx, notify: false);
        }

        if (_usedThisLife.Contains(idx)) return HookResult.Continue;

        int hp = victimPawn.Health;
        if (hp <= 0) return HookResult.Continue;

        if (!CouldBeLethal(victimPawn, info))
            return HookResult.Continue;

        /*
         * 触发名刀：
         * 1. 本次伤害清零；
         * 2. 强制保留 1 HP；
         * 3. 进入无敌与加速。
         */
        info.Damage = 0.0f;
        _usedThisLife.Add(idx);

        StartImmune(victim, victimPawn, fromDeathFallback: false);

        return HookResult.Continue;
    }

    /*
     * 受伤后兜底：
     * 如果已经处于无敌状态，但某些伤害仍然落地，这里下一帧强制拉回 1 HP。
     */
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return HookResult.Continue;

        uint idx = victim.Index;

        if (_immunePlayers.ContainsKey(idx))
        {
            _plugin.AddTimer(0.01f, () =>
            {
                if (!victim.IsValid || !victim.PawnIsAlive) return;

                var pawn = victim.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) return;

                ForceHealth(pawn, 1);
            });
        }

        return HookResult.Continue;
    }

    /*
     * 死亡兜底：
     * 理论上名刀应该在 OnEntityTakeDamagePre 里挡住死亡。
     * 但如果伤害来自其他插件直接改血、地图触发器、或某些特殊伤害没有经过正常 DamageInfo，
     * 这里会尝试立刻原地复活并进入名刀状态。
     *
     * 注意：这个兜底会出现一次死亡事件/击杀提示，属于“防漏底线”，不是主逻辑。
     */
    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid) return HookResult.Continue;
        if (!IsTarget(victim)) return HookResult.Continue;

        uint idx = victim.Index;
        if (_usedThisLife.Contains(idx)) return HookResult.Continue;

        _usedThisLife.Add(idx);

        LastAliveState? last = null;
        _lastAliveStates.TryGetValue(idx, out last);

        _plugin.AddTimer(0.05f, () =>
        {
            if (!_settings.Enabled) return;
            if (!victim.IsValid) return;

            victim.Respawn();

            _plugin.AddTimer(0.05f, () =>
            {
                if (!victim.IsValid || !victim.PawnIsAlive) return;

                var pawn = victim.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) return;

                if (last != null)
                {
                    pawn.Teleport(last.Position, last.Angle, new Vector(0, 0, 0));
                }

                StartImmune(victim, pawn, fromDeathFallback: true);
            });
        });

        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        _immunePlayers.Remove(player.Index);
        _usedThisLife.Remove(player.Index);
        _lastAliveStates.Remove(player.Index);

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ClearImmunePlayers(restoreSpeed: true);
        _usedThisLife.Clear();
        _lastAliveStates.Clear();

        if (_settings.Enabled)
            EnsureTickTimer();

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        ClearImmunePlayers(restoreSpeed: true);
        _usedThisLife.Clear();
        _lastAliveStates.Clear();
        return HookResult.Continue;
    }

    /*
     * 比上一版更稳的致命判定：
     * - info.Damage >= 当前血量：肯定危险；
     * - info.Damage * 4 >= 当前血量：考虑爆头、部位倍率等；
     * - info.Damage >= 当前血量 + 护甲：兼容部分武器/护甲计算；
     * - 血量 <= 1 时任何伤害都视为致命。
     */
    private bool CouldBeLethal(CCSPlayerPawn pawn, CTakeDamageInfo info)
    {
        int hp = pawn.Health;
        int armor = Math.Max(0, pawn.ArmorValue);
        float dmg = Math.Max(0.0f, info.Damage);

        if (hp <= 1 && dmg > 0.0f) return true;
        if (dmg >= hp) return true;
        if (dmg * 4.0f >= hp) return true;
        if (dmg >= hp + armor) return true;

        return false;
    }

    private void StartImmune(CCSPlayerController player, CCSPlayerPawn pawn, bool fromDeathFallback)
    {
        if (player == null || !player.IsValid || pawn == null || !pawn.IsValid) return;

        uint idx = player.Index;

        float immuneTime = Math.Max(0.0f, _settings.ImmuneTime);
        float speedMultiplier = 1.0f + Math.Max(0.0f, _settings.ExtraSpeedPercent) / 100.0f;

        float restoreVelocity = pawn.VelocityModifier;
        if (restoreVelocity <= 0.0f) restoreVelocity = 1.0f;

        ForceHealth(pawn, 1);

        if (immuneTime <= 0.0f)
        {
            CaorenCupUtils.PrintToChat(player, $"{ChatColors.Green}[名刀]{ChatColors.Default} 已触发，致命伤害被抵消，保留 1 HP。");
            return;
        }

        _immunePlayers[idx] = new ImmuneState
        {
            ExpireAt = Server.CurrentTime + immuneTime,
            SpeedMultiplier = speedMultiplier,
            RestoreVelocityModifier = restoreVelocity
        };

        ApplySpeed(pawn, speedMultiplier);
        EnsureTickTimer();

        if (fromDeathFallback)
        {
            CaorenCupUtils.PrintToChat(
                player,
                $"{ChatColors.Green}[名刀]{ChatColors.Default} 已通过兜底复活触发：保留 1 HP，并获得 {ChatColors.Green}{immuneTime:F1}{ChatColors.Default} 秒无敌。"
            );
        }
        else
        {
            CaorenCupUtils.PrintToChat(
                player,
                $"{ChatColors.Green}[名刀]{ChatColors.Default} 已触发！你保留了 1 HP，并获得 {ChatColors.Green}{immuneTime:F1}{ChatColors.Default} 秒无敌。" +
                (_settings.ExtraSpeedPercent > 0.0f ? $" 移速提升 {ChatColors.Green}+{_settings.ExtraSpeedPercent:F0}%{ChatColors.Default}。" : "")
            );
        }
    }

    private void Tick()
    {
        if (!_settings.Enabled)
        {
            StopTickTimer();
            ClearImmunePlayers(restoreSpeed: true);
            return;
        }

        SaveLastAliveStates();
        TickImmunePlayers();
    }

    private void SaveLastAliveStates()
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (!IsTarget(p)) continue;

            var pawn = p.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;
            if (pawn.AbsOrigin == null || pawn.AbsRotation == null) continue;

            _lastAliveStates[p.Index] = new LastAliveState
            {
                Position = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z),
                Angle = new QAngle(pawn.AbsRotation.X, pawn.AbsRotation.Y, pawn.AbsRotation.Z),
                TeamNum = p.TeamNum
            };
        }
    }

    private void TickImmunePlayers()
    {
        if (_immunePlayers.Count == 0) return;

        float now = Server.CurrentTime;

        foreach (var kv in _immunePlayers.ToList())
        {
            uint idx = kv.Key;
            ImmuneState state = kv.Value;

            var player = FindPlayerByIndex(idx);
            if (player == null || !player.IsValid || !player.PawnIsAlive)
            {
                _immunePlayers.Remove(idx);
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
            {
                _immunePlayers.Remove(idx);
                continue;
            }

            if (now >= state.ExpireAt)
            {
                EndImmune(player, idx, notify: true);
                continue;
            }

            if (pawn.Health < 1)
                ForceHealth(pawn, 1);

            ApplySpeed(pawn, state.SpeedMultiplier);
        }
    }

    private void EndImmune(CCSPlayerController player, uint idx, bool notify)
    {
        if (!_immunePlayers.TryGetValue(idx, out var state))
            return;

        _immunePlayers.Remove(idx);

        if (player != null && player.IsValid && player.PawnIsAlive)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid)
            {
                if (pawn.VelocityModifier <= state.SpeedMultiplier + 0.05f)
                {
                    ApplySpeed(pawn, Math.Max(0.01f, state.RestoreVelocityModifier));
                }
            }

            if (notify)
            {
                CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}[名刀]{ChatColors.Default} 无敌时间结束。");
            }
        }
    }

    private void ClearImmunePlayers(bool restoreSpeed)
    {
        if (restoreSpeed)
        {
            foreach (var kv in _immunePlayers.ToList())
            {
                var player = FindPlayerByIndex(kv.Key);
                if (player == null || !player.IsValid || !player.PawnIsAlive) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                if (pawn.VelocityModifier <= kv.Value.SpeedMultiplier + 0.05f)
                {
                    ApplySpeed(pawn, Math.Max(0.01f, kv.Value.RestoreVelocityModifier));
                }
            }
        }

        _immunePlayers.Clear();
    }

    private void EnsureTickTimer()
    {
        if (_tickTimer != null) return;

        _tickTimer = _plugin.AddTimer(
            0.1f,
            Tick,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE
        );
    }

    private void StopTickTimer()
    {
        if (_tickTimer == null) return;

        _tickTimer.Kill();
        _tickTimer = null;
    }

    private CCSPlayerController? ResolveController(CCSPlayerPawn pawn)
    {
        try
        {
            var controller = pawn.Controller.Value as CCSPlayerController;
            if (controller != null && controller.IsValid)
                return controller;
        }
        catch
        {
            // ignored
        }

        try
        {
            var controller = pawn.OriginalController.Value as CCSPlayerController;
            if (controller != null && controller.IsValid)
                return controller;
        }
        catch
        {
            // ignored
        }

        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid) continue;

            var pp = p.PlayerPawn.Value;
            if (pp == null || !pp.IsValid) continue;

            if (pp.Handle == pawn.Handle)
                return p;
        }

        return null;
    }

    private CCSPlayerController? FindPlayerByIndex(uint index)
    {
        return Utilities.GetPlayers().FirstOrDefault(p => p != null && p.IsValid && p.Index == index);
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;

        if (_settings.Target == "t" &&
            player.TeamNum == (byte)CsTeam.Terrorist)
            return true;

        if (_settings.Target == "ct" &&
            player.TeamNum == (byte)CsTeam.CounterTerrorist)
            return true;

        return false;
    }

    private static void ForceHealth(CCSPlayerPawn pawn, int hp)
    {
        if (pawn == null || !pawn.IsValid) return;

        int finalHp = Math.Max(1, hp);

        if (pawn.MaxHealth < finalHp)
        {
            pawn.MaxHealth = finalHp;
            try { Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth"); } catch { }
        }

        pawn.Health = finalHp;

        try { Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth"); } catch { }
    }

    private static void ApplySpeed(CCSPlayerPawn pawn, float speedMultiplier)
    {
        if (pawn == null || !pawn.IsValid) return;

        pawn.VelocityModifier = Math.Max(0.01f, speedMultiplier);

        try
        {
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
        catch
        {
            // 部分 CSS 版本不需要手动刷新。
        }
    }

    private string GetTargetDisplayName(string target)
    {
        return target switch
        {
            "t" => "T方",
            "ct" => "CT方",
            "all" => "全体玩家",
            _ => target.ToUpperInvariant()
        };
    }

    private void Reply(CCSPlayerController? player, string message)
    {
        if (player != null && player.IsValid)
            CaorenCupUtils.PrintToChat(player, message);
        else
            Console.WriteLine($"[草人杯] {message}");
    }

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/lhimm{ChatColors.Default} : 名刀无敌，受到致命伤害时保留 1 HP";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled)
            return $"LhImm: {ChatColors.Red}已禁用{ChatColors.Default}";

        return $"LhImm: {ChatColors.Green}已启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 无敌:{_settings.ImmuneTime:F1}s | 速度:+{_settings.ExtraSpeedPercent:F0}%";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;

        return $"[名刀无敌] {GetTargetDisplayName(_settings.Target)}受到致命伤害时保留 1 HP，获得 {_settings.ImmuneTime:F1} 秒无敌，速度 +{_settings.ExtraSpeedPercent:F0}%";
    }

    public string GetFeatureDescription()
    {
        return "【名刀无敌】受到致命伤害时，本次伤害不会致死，玩家保留 1 HP。\n" +
               "触发后进入短暂无敌时间，期间受到的普通伤害会被清零。\n" +
               "指令: /lhimm <t/ct/all/0> <Time> <extraSpeed>\n" +
               "Time 是无敌时间，单位秒；extraSpeed 是额外速度百分比，例如 50 表示额外 50%。\n" +
               "每名玩家每条命只能触发一次名刀，重生后重置。\n" +
               "如果特殊伤害没有被预伤害钩子拦截，会尝试用死亡兜底复活，但可能出现一次死亡提示。";
    }
}