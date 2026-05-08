using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Linq;

namespace CaorenCup.Features;

public class BleedFeature : ICaorenFeature
{
    public string FeatureName => "Bleed/Regen";

    private CaorenCupPlugin _plugin = null!;
    private BleedSettings _settings = null!;

    private CounterStrikeSharp.API.Modules.Timers.Timer? _healthTimer = null;
    private bool _isRunning = false;

    // 保留原插件里的音效路径
    private const string PainSoundPath = "sounds/player/damage/male/pain01.vsnd";

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型控制台指令 (支持 /bleed)
        plugin.AddCommand("css_bleed", "流血/回血设置: css_bleed <t/ct/all/0> <秒> <数值>，上下限由 /hpcap 控制", OnCommandBleed);

        // 事件保持原有的优秀设计：冻结结束才开始，回合结束/开始时停止
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        plugin.RegisterEventHandler<EventRoundFreezeEnd>(OnFreezeEnd);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.Bleed;
    }

    public void OnUnload()
    {
        StopTimer();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled) StopTimer();
    }

    // --- 核心指令逻辑 ---

    private void OnCommandBleed(CCSPlayerController? player, CommandInfo info)
    {
        // 权限检查
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
            return;
        }

        // 无参数，显示帮助
        if (info.ArgCount == 1)
        {
            PrintUsage(player);
            return;
        }

        string arg1 = info.GetArg(1).ToLower();

        // 1. 输入 0，一键禁用
        if (arg1 == "0" || arg1 == "off")
        {
            SetEnabled(false);
            _plugin.SaveConfig();
            CaorenCupUtils.PrintToChatAll(CaorenCupUtils.FormatChangeMessage("模块控制", FeatureName, $"{ChatColors.Red}已禁用"));
            return;
        }

        // 2. 参数不足：极限值已经统一移到 /hpcap，不再由 /bleed 单独传入。
        if (info.ArgCount < 4)
        {
            if (player == null) Console.WriteLine("[草人杯] 参数不足。"); else PrintUsage(player);
            return;
        }

        // 3. 解析并应用后覆盖机制
        string target = arg1;
        if (target != "t" && target != "ct" && target != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的阵营，请使用 t, ct 或 all。");
            return;
        }

        if (!float.TryParse(info.GetArg(2), out float interval) || interval <= 0)
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的时间间隔。");
            return;
        }

        if (!int.TryParse(info.GetArg(3), out int amount))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的血量变化数值。");
            return;
        }

        if (info.ArgCount >= 5 && player != null)
        {
            CaorenCupUtils.PrintToChat(player, "提示：/bleed 的极限值参数已移到 /hpcap，此处第4个参数会被忽略。请使用 /hpcap <min> <max> 设置全局血量上下限。");
        }

        // 保存配置
        _settings.TargetTeam = target;
        _settings.Interval = interval;
        _settings.Amount = amount;
        _settings.Enabled = true;

        _plugin.SaveConfig();

        // 如果在回合进行中修改，立即重启定时器生效
        StopTimer();
        StartTimer();

        string action = amount > 0 ? $"{ChatColors.Green}回血{ChatColors.Default}" : $"{ChatColors.Red}扣血{ChatColors.Default}";
        string capText = CaorenCupUtils.IsHpCapEnabled(_plugin)
            ? $"全局范围:{_plugin.Config.HpCap.Min}-{_plugin.Config.HpCap.Max}HP"
            : "未启用 /hpcap，使用默认范围:1-100HP";
        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用! 目标:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} 每{interval}s {action} {Math.Abs(amount)}HP ({capText})");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== Bleed/Regen 指令说明 ===");
        player.PrintToChat($" {ChatColors.Green}/bleed 0{ChatColors.Default} : 一键禁用");
        player.PrintToChat($" {ChatColors.Green}/bleed <t/ct/all> <秒> <正回负扣>{ChatColors.Default}");
        player.PrintToChat($" 血量上下限统一使用 {ChatColors.Green}/hpcap <min> <max>{ChatColors.Default} 设置");
        player.PrintToChat($" 示例: /hpcap 1 100；/bleed t 1 -5 (T每秒扣5血，最低剩1血)");
        player.PrintToChat($" 示例: /hpcap 1 150；/bleed all 2 10 (全体每2秒回10血，最高150血)");
        player.PrintToChat($" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏逻辑 ---

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        StopTimer();
        return HookResult.Continue;
    }

    private HookResult OnFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (_settings.Enabled)
        {
            StartTimer();
            CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[Bleed]{ChatColors.Default} 自动启动生命值调整 (目标:{_settings.TargetTeam.ToUpper()})");
        }
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        StopTimer();
        return HookResult.Continue;
    }

    private void StartTimer()
    {
        if (_isRunning || !_settings.Enabled) return;

        float safeInterval = Math.Max(0.1f, _settings.Interval);
        _isRunning = true;
        _healthTimer = _plugin.AddTimer(safeInterval, OnTimerTick, TimerFlags.REPEAT);
    }

    private void StopTimer()
    {
        _isRunning = false;
        if (_healthTimer != null)
        {
            _healthTimer.Kill();
            _healthTimer = null;
        }
    }

    private void OnTimerTick()
    {
        if (!_isRunning || !_settings.Enabled) { StopTimer(); return; }

        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) continue;
            if (!IsTarget(player)) continue;

            ApplyChange(player);
        }
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.TargetTeam == "all") return true;
        if (_settings.TargetTeam == "t" && player.Team == CsTeam.Terrorist) return true;
        if (_settings.TargetTeam == "ct" && player.Team == CsTeam.CounterTerrorist) return true;
        return false;
    }

    private void ApplyChange(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        int currentHp = pawn.Health;
        int delta = _settings.Amount;
        int newHp = currentHp + delta;

        // 限制检查：/bleed 的极限值已统一移到 /hpcap。
        // 未启用 /hpcap 时，为了兼容旧玩法，回血默认最高 100，扣血默认最低 1。
        if (delta > 0) // 回血逻辑
        {
            int effectiveMax = CaorenCupUtils.GetHpCapMax(_plugin, 100);
            if (currentHp >= effectiveMax) return;
            if (newHp > effectiveMax) newHp = effectiveMax;
        }
        else if (delta < 0) // 扣血逻辑
        {
            int effectiveMin = CaorenCupUtils.GetHpCapMin(_plugin, 1);
            if (currentHp <= effectiveMin) return;
            if (newHp < effectiveMin) newHp = effectiveMin;
        }
        else return; // delta == 0

        if (newHp != currentHp)
        {
            CaorenCupUtils.ApplyModuleHealth(_plugin, pawn, newHp);
            newHp = pawn.Health;

            // 播放受伤音效
            if (_settings.PlaySound && delta < 0)
            {
                try { player.ExecuteClientCommand($"play {PainSoundPath}"); } catch { }
            }
        }
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/bleed{ChatColors.Default} : 查看并设置 流血/回血 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"Bleed: {ChatColors.Red}已禁用{ChatColors.Default}";
        string run = _isRunning ? $"{ChatColors.Green}运行中" : $"{ChatColors.Red}休眠中";
        string action = _settings.Amount > 0 ? "回血" : "扣血";
        string cap = CaorenCupUtils.IsHpCapEnabled(_plugin) ? $"/hpcap:{_plugin.Config.HpCap.Min}-{_plugin.Config.HpCap.Max}" : "默认范围:1-100";
        return $"Bleed: {ChatColors.Green}启用{ChatColors.Default} | 状态:{run} | 目标:{_settings.TargetTeam.ToUpper()} | 每{_settings.Interval}s {action}{Math.Abs(_settings.Amount)}HP | {cap}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        string action = _settings.Amount > 0 ? "恢复" : "扣除";
        string target = _settings.TargetTeam == "all" ? "全体玩家" : $"{_settings.TargetTeam.ToUpper()} 阵营";
        string cap = CaorenCupUtils.IsHpCapEnabled(_plugin)
            ? $"全局范围 {_plugin.Config.HpCap.Min}~{_plugin.Config.HpCap.Max} HP"
            : "默认范围 1~100 HP";

        return $"[持续生命变化] {target} 每 {_settings.Interval}秒 {action} {Math.Abs(_settings.Amount)} HP（{cap}）。";
    }

    public string GetFeatureDescription()
    {
        return " [Bleed] 强制指定阵营按固定间隔扣除或恢复生命值。\n" +
               " - 血量上下限不再由 /bleed 单独设置，统一交给 /hpcap 控制。\n" +
               " - 模块会在准备阶段(FreezeTime)结束后自动启动。";
    }
}
