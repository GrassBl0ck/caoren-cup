using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CaorenCup.Features;

public class SmokeFeature : ICaorenFeature
{
    public string FeatureName => "Smoke (高级烟雾控制)";

    private CaorenCupPlugin _plugin = null!;
    private SmokeSettings _settings = null!;

    private CounterStrikeSharp.API.Modules.Timers.Timer? _effectTimer = null;
    private const string PainSoundPath = "sounds/player/damage/male/pain01.vsnd";

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型控制台指令
        plugin.AddCommand("css_smoke", "设置烟雾弹: css_smoke <t/ct/all/0> <持续时间/-> <正回血负扣血>", OnCommandSmoke);

        plugin.RegisterEventHandler<EventSmokegrenadeDetonate>(OnSmokeDetonate);
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.Smoke;
    }

    public void OnUnload()
    {
        StopEffectTimer();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled) StopEffectTimer();
    }

    // --- 核心指令逻辑 ---

    private void OnCommandSmoke(CCSPlayerController? player, CommandInfo info)
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

        // 2. 参数解析
        if (info.ArgCount < 4)
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

        // 解析时长
        float duration = -1.0f;
        string arg2 = info.GetArg(2);
        if (arg2 != "-")
        {
            if (!float.TryParse(arg2, out duration) || duration <= 0)
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "无效的持续时间。");
                return;
            }
        }

        // 解析血量变动 (正数回血，负数扣血)
        if (!int.TryParse(info.GetArg(3), out int hpChange))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的血量变化数值。");
            return;
        }

        // 应用配置
        _settings.Target = target;
        _settings.Duration = duration;
        _settings.HealthChangePerSecond = hpChange;
        _settings.Enabled = true;

        _plugin.SaveConfig();

        StartEffectTimer();

        string durationStr = duration > 0 ? $"{duration}秒" : "默认时长";
        string effectStr = hpChange > 0 ? $"{ChatColors.Green}恢复生命 {hpChange}/秒" : (hpChange < 0 ? $"{ChatColors.Red}受��伤害 {Math.Abs(hpChange)}/秒" : "无影响");

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用!");
        CaorenCupUtils.PrintToChatAll($" 目标:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} | 时长:{durationStr} | 效果:{effectStr}{ChatColors.Default}");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== Smoke 指令说明 ===");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/smoke 0{ChatColors.Default} : 一键禁用");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/smoke <t/ct/all> <持续时间/-> <每秒血量变动>{ChatColors.Default}");
        CaorenCupUtils.PrintToChat(player, $" 示例: /smoke t - -5 (T的烟默认时长，T进烟每秒扣5血)");
        CaorenCupUtils.PrintToChat(player, $" 示例: /smoke ct 10 5 (CT的烟10秒散去，CT进烟每秒回5血)");
        CaorenCupUtils.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏核心逻辑 ---

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        StartEffectTimer();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        StopEffectTimer();
        return HookResult.Continue;
    }

    private HookResult OnSmokeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.Duration <= 0) return HookResult.Continue;

        var thrower = @event.Userid;
        if (thrower == null || !thrower.IsValid) return HookResult.Continue;

        if (!IsTarget(thrower)) return HookResult.Continue;

        Vector pos = new Vector(@event.X, @event.Y, @event.Z);

        _plugin.AddTimer(0.1f, () =>
        {
            var smokes = Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile");
            CSmokeGrenadeProjectile? targetEntity = null;
            float minDist = 50.0f;

            foreach (var s in smokes)
            {
                if (s != null && s.IsValid && s.DidSmokeEffect && s.AbsOrigin != null)
                {
                    float dist = GetDistance(s.AbsOrigin, pos);
                    if (dist < minDist) { minDist = dist; targetEntity = s; }
                }
            }

            if (targetEntity != null)
            {
                int entIdx = (int)targetEntity.Index;
                if (_settings.Duration < 21.5f)
                {
                    _plugin.AddTimer(_settings.Duration, () =>
                    {
                        var ent = Utilities.GetEntityFromIndex<CSmokeGrenadeProjectile>(entIdx);
                        if (ent != null && ent.IsValid) ent.Remove();
                    });
                }
            }
        });

        return HookResult.Continue;
    }

    private void StartEffectTimer()
    {
        StopEffectTimer();
        if (!_settings.Enabled || _settings.HealthChangePerSecond == 0) return;
        _effectTimer = _plugin.AddTimer(1.0f, OnEffectTick, TimerFlags.REPEAT);
    }

    private void StopEffectTimer()
    {
        _effectTimer?.Kill();
        _effectTimer = null;
    }

    private void OnEffectTick()
    {
        if (!_settings.Enabled || _settings.HealthChangePerSecond == 0) return;

        var activeSmokes = new List<Vector>();
        var smokes = Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile");
        foreach (var s in smokes)
        {
            if (s != null && s.IsValid && s.DidSmokeEffect && s.AbsOrigin != null)
                activeSmokes.Add(s.AbsOrigin);
        }

        if (activeSmokes.Count == 0) return;

        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive) continue;
            if (!IsTarget(p)) continue;

            var pawn = p.PlayerPawn.Value;
            if (pawn == null || pawn.AbsOrigin == null) continue;

            Vector pPos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 40);
            bool inSmoke = false;

            foreach (var sPos in activeSmokes)
            {
                if (GetDistance(pPos, sPos) <= _settings.BaseRadius)
                {
                    inSmoke = true;
                    break;
                }
            }

            if (inSmoke)
            {
                ApplyHealthChange(p, pawn, _settings.HealthChangePerSecond);
            }
        }
    }

    private void ApplyHealthChange(CCSPlayerController player, CCSPlayerPawn pawn, int change)
    {
        int currentHp = pawn.Health;
        int newHp = currentHp + change; // 核心：+ 为回血，- 为扣血

        if (change > 0) // 回血逻辑
        {
            int maxHp = pawn.MaxHealth > 0 ? pawn.MaxHealth : 100;
            maxHp = CaorenCupUtils.GetHpCapMax(_plugin, maxHp);
            if (currentHp >= maxHp) return;
            if (newHp > maxHp) newHp = maxHp;
        }
        else if (change < 0)
        {
            int minHp = CaorenCupUtils.GetHpCapMin(_plugin, 0);
            if (CaorenCupUtils.IsHpCapEnabled(_plugin) && currentHp <= minHp) return;
            if (CaorenCupUtils.IsHpCapEnabled(_plugin) && newHp < minHp) newHp = minHp;
        }

        if (newHp <= 0 && !CaorenCupUtils.IsHpCapEnabled(_plugin)) // 致死逻辑
        {
            pawn.CommitSuicide(false, true);
            CaorenCupUtils.PrintToChatAll($" {ChatColors.Red}☠ {player.PlayerName} {ChatColors.Default}在烟雾中窒息而亡！");
        }
        else if (newHp != currentHp)
        {
            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, newHp);
            newHp = pawn.Health;

            // 只有扣血(change < 0)时才播放受伤音效
            if (change < 0 && _settings.PlaySound)
            {
                try { player.ExecuteClientCommand($"play {PainSoundPath}"); } catch { }
            }
        }
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;
        if (_settings.Target == "t" && player.Team == CsTeam.Terrorist) return true;
        if (_settings.Target == "ct" && player.Team == CsTeam.CounterTerrorist) return true;
        return false;
    }

    private float GetDistance(Vector v1, Vector v2)
    {
        if (v1 == null || v2 == null) return 99999f;
        float dx = v1.X - v2.X;
        float dy = v1.Y - v2.Y;
        float dz = v1.Z - v2.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/smoke{ChatColors.Default} : 查看并设置 高级烟雾 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"Smoke: {ChatColors.Red}已禁用{ChatColors.Default}";
        string dur = _settings.Duration > 0 ? $"{_settings.Duration}s" : "默认";
        string dmg = _settings.HealthChangePerSecond > 0 ? $"回血{_settings.HealthChangePerSecond}/s" : (_settings.HealthChangePerSecond < 0 ? $"扣血{Math.Abs(_settings.HealthChangePerSecond)}/s" : "无");
        return $"Smoke: {ChatColors.Green}启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 时长:{dur} | 效果:{dmg}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;

        string durText = _settings.Duration > 0 ? $"时长变为 {_settings.Duration}秒" : "";
        string dmgText = "";

        if (_settings.HealthChangePerSecond < 0) dmgText = $"在烟中每秒扣除 {Math.Abs(_settings.HealthChangePerSecond)} HP";
        else if (_settings.HealthChangePerSecond > 0) dmgText = $"在烟中每秒恢复 {_settings.HealthChangePerSecond} HP";

        string combined = string.Join("，且", new[] { durText, dmgText }.Where(s => !string.IsNullOrEmpty(s)));
        string target = _settings.Target == "all" ? "全员" : $"{_settings.Target.ToUpper()} 阵营";

        return string.IsNullOrEmpty(combined) ? null : $"[异变烟雾] {target} 的烟雾{combined}。";
    }

    public string GetFeatureDescription()
    {
        return " [Smoke] 高级烟雾控制模块。\n" +
               " - 可以控制特定阵营投掷的烟雾弹的存在时长。\n" +
               " - 可以将烟雾变为'毒烟'(进烟扣血) 或 '奶烟'(进烟回血)。\n" +
               " - 负数为扣血，正数为回血。利用此功能改变烟雾掩体地位！";
    }
}