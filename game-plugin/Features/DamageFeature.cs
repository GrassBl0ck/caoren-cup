using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Collections.Generic;

namespace CaorenCup.Features;

public class DamageFeature : ICaorenFeature
{
    public string FeatureName => "Damage Control (伤害与锁血)";

    private CaorenCupPlugin _plugin = null!;
    private DamageSettings _settings = null!;

    private sealed class DamageWindowEntry
    {
        public float Time { get; set; }
        public float Damage { get; set; }
    }

    // 记录玩家最近 N 秒内的真实受击数据：SteamID -> [(受击时间, 真实扣血)]
    private readonly Dictionary<ulong, List<DamageWindowEntry>> _damageWindow = new();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型指令
        plugin.AddCommand("css_dmg", "伤害控制: css_dmg <t/ct/all/0> <倍率/-> <上限Cap> <时间窗口秒>", OnCommandDmg);

        // 拦截基础伤害 (处理易伤与防猝死)
        plugin.RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);

        // 监听最终伤害 (处理精准锁血返还)
        plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

        // 回合开始清理累积数据
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.Damage;
    }

    public void OnUnload()
    {
        _damageWindow.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled) _damageWindow.Clear();
    }

    // --- 核心指令逻辑 ---

    private void OnCommandDmg(CCSPlayerController? player, CommandInfo info)
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
        if (info.ArgCount < 5)
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

        // 解析易伤倍率 ("-" 代表默认 1.0)
        float multiplier = 1.0f;
        string arg2 = info.GetArg(2);
        if (arg2 != "-")
        {
            if (!float.TryParse(arg2, out multiplier) || multiplier < 0)
            {
                if (player != null) CaorenCupUtils.PrintToChat(player, "无效的易伤数值。");
                return;
            }
        }

        // 解析 Cap
        if (!int.TryParse(info.GetArg(3), out int cap) || cap < 0)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的伤害上限(Cap)数值。");
            return;
        }

        // 解析 Window
        if (!float.TryParse(info.GetArg(4), out float window) || window <= 0)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的时间窗口数值。");
            return;
        }

        // 应用配置 (后覆盖机制)
        _settings.Target = target;
        _settings.Multiplier = multiplier;
        _settings.Cap = cap;
        _settings.CapWindow = window;
        _settings.Enabled = true;

        _plugin.SaveConfig();
        _damageWindow.Clear(); // 切换设置时清空旧的累积数据

        string multStr = multiplier == 1.0f ? "正常" : $"{multiplier}x";
        string capStr = cap == 0 ? "无限制" : $"{cap}HP / {window}秒";

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用!");
        CaorenCupUtils.PrintToChatAll($" 目标:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} | 易伤:{multStr} | 锁血上限:{capStr}");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== Damage Control 指令说明 ===");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/dmg 0{ChatColors.Default} : 一键禁用");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/dmg <t/ct/all> <易伤倍数/-> <上限Cap> <时间窗口秒>{ChatColors.Default}");
        CaorenCupUtils.PrintToChat(player, $" 示例: /dmg t 2 50 1.5 (T阵营受到2倍伤害，但在1.5秒内最多只掉50血)");
        CaorenCupUtils.PrintToChat(player, $" 示例: /dmg all - 100 5 (全员伤害正常，但每5秒最多只掉100血)");
        CaorenCupUtils.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏核心逻辑 ---

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _damageWindow.Clear();
        return HookResult.Continue;
    }

    // 【第一层防线】处理倍率，并防止爆头溢出致死
    private HookResult OnPlayerTakeDamagePre(CCSPlayerPawn pawn, CTakeDamageInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var controller = pawn.Controller.Value as CCSPlayerController;
        if (controller == null || !IsTarget(controller)) return HookResult.Continue;

        // 1. 应用易伤倍率。若开启 /hpcap，只限制“倍率额外增加”的那部分伤害，不吞掉原版基础伤害。
        if (Math.Abs(_settings.Multiplier - 1.0f) > 0.01f)
        {
            float baseDamage = info.Damage;
            float scaledDamage = baseDamage * _settings.Multiplier;

            if (CaorenCupUtils.IsHpCapEnabled(_plugin) && _settings.Multiplier > 1.0f)
            {
                float extraDamage = scaledDamage - baseDamage;
                float maxExtraDamage = pawn.Health - _plugin.Config.HpCap.Min - baseDamage;
                scaledDamage = baseDamage + Math.Max(0, Math.Min(extraDamage, maxExtraDamage));
            }

            info.Damage = scaledDamage;
        }

        // 2. 伤害窗口保护 (仅处理 Cap > 0 的情况)
        if (_settings.Cap > 0)
        {
            ulong steamId = controller.SteamID;
            float now = Server.CurrentTime;

            float recentDamage = GetRecentDamageTotal(steamId, now);
            float remainingQuota = _settings.Cap - recentDamage;

            if (remainingQuota <= 0)
            {
                info.Damage = 0;
            }
            else
            {
                int minHp = CaorenCupUtils.GetHpCapMin(_plugin, 1);
                float maxHealthLoss = Math.Max(0, pawn.Health - minHp);
                float allowedFinalDamage = Math.Min(remainingQuota, maxHealthLoss);

                if (allowedFinalDamage <= 0)
                {
                    info.Damage = 0;
                }
                else
                {
                    if (info.Damage > allowedFinalDamage)
                    {
                        info.Damage = allowedFinalDamage;
                    }
                }
            }
        }

        return HookResult.Continue;
    }

    // 【第二层防线】绝对精准的数值返还
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_settings.Enabled || _settings.Cap <= 0) return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid || !IsTarget(victim)) return HookResult.Continue;

        var pawn = victim.PlayerPawn.Value;
        if (pawn == null) return HookResult.Continue;

        int dmg = @event.DmgHealth; // 这是经过引擎所有倍率(包括爆头)计算后的最终扣血量
        if (dmg <= 0) return HookResult.Continue;

        ulong steamId = victim.SteamID;
        float now = Server.CurrentTime;

        float recentDamage = GetRecentDamageTotal(steamId, now);
        float newAccum = recentDamage + dmg;

        // 如果本次伤害导致总伤害超出了设定的上限 Cap
        if (newAccum > _settings.Cap)
        {
            // 计算多扣了多少血
            int excess = (int)Math.Round(newAccum - _settings.Cap);
            if (excess > dmg) excess = dmg; // 最多只能返还本次扣除的血量

            if (excess > 0 && pawn.Health > 0)
            {
                CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, pawn.Health + excess);
            }

            // 累积量封顶
            dmg -= excess;
        }

        if (dmg > 0)
        {
            AddDamageWindowEntry(steamId, now, dmg);
        }

        return HookResult.Continue;
    }

    private float GetRecentDamageTotal(ulong steamId, float now)
    {
        if (!_damageWindow.TryGetValue(steamId, out var entries)) return 0;

        PruneDamageWindow(entries, now);
        if (entries.Count == 0)
        {
            _damageWindow.Remove(steamId);
            return 0;
        }

        float total = 0;
        foreach (var entry in entries)
        {
            total += entry.Damage;
        }

        return total;
    }

    private void AddDamageWindowEntry(ulong steamId, float now, float damage)
    {
        if (!_damageWindow.TryGetValue(steamId, out var entries))
        {
            entries = new List<DamageWindowEntry>();
            _damageWindow[steamId] = entries;
        }

        PruneDamageWindow(entries, now);
        entries.Add(new DamageWindowEntry { Time = now, Damage = damage });
    }

    private void PruneDamageWindow(List<DamageWindowEntry> entries, float now)
    {
        entries.RemoveAll(entry => now - entry.Time > _settings.CapWindow);
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;
        if (_settings.Target == "t" && player.Team == CsTeam.Terrorist) return true;
        if (_settings.Target == "ct" && player.Team == CsTeam.CounterTerrorist) return true;
        return false;
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/dmg{ChatColors.Default} : 查看并设置 伤害倍率与锁血 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"Damage: {ChatColors.Red}已禁用{ChatColors.Default}";
        string capInfo = _settings.Cap > 0 ? $"上限:{_settings.Cap}HP/{_settings.CapWindow}s" : "无锁血";
        return $"Damage: {ChatColors.Green}启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 易伤:{_settings.Multiplier}x | {capInfo}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        string target = _settings.Target == "all" ? "全员" : $"{_settings.Target.ToUpper()} 阵营";

        List<string> desc = new();
        if (_settings.Multiplier != 1.0f) desc.Add($"受到 {_settings.Multiplier} 倍真实伤害");
        if (_settings.Cap > 0) desc.Add($"在 {_settings.CapWindow} 秒内最多损失 {_settings.Cap} 点生命值");

        if (desc.Count == 0) return null;
        return $"[异变体质] {target} {string.Join("，且", desc)}。";
    }

    public string GetFeatureDescription()
    {
        return " [Damage Scaler] 全局修改玩家受伤倍率以及时间窗口内的受伤上限。\n";
               
    }
}
