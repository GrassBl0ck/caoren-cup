using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Collections.Generic;
using System.Text;
using CaorenCup.Contracts;

namespace CaorenCup.QOL.SimpleHP;

public sealed class CaorenCupSimpleHpPlugin : BasePlugin
{
    public override string ModuleName => "CaorenCup Simple HP";
    public override string ModuleVersion => "1.10.0";
    public override string ModuleAuthor => "Graslock + AI";
    private const string FeatureName = "Simple HP (伤害查询)";

    private ICaorenCupCoreApi Core => CaorenCupCoreAccess.TryGet()
        ?? throw new InvalidOperationException("CaorenCupCore is unavailable.");
    private SimpleHpSettings _settings = null!;

    // 伤害数据存储：AttackerIndex -> VictimIndex -> Info
    private Dictionary<int, Dictionary<int, DamageInfo>> _damageData = new();

    private class DamageInfo
    {
        public int Hits { get; set; }
        public int Damage { get; set; }
    }

    public override void Load(bool hotReload)
    {
        // 管理员控制指令
        AddCommand("css_hp_set", "设置HP模块: css_hp_set <t/ct/all/0> [1/0必须死亡]", OnCommandHpSet);

        // 玩家查询指令 (/crhp；.hp 由聊天监听处理)
        AddCommand("css_crhp", "查询本回合造成的伤害", OnCommandHpQuery);

        // 事件监听
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        AddCommandListener("say", OnHpSayCommand, HookMode.Pre);
        AddCommandListener("say_team", OnHpSayCommand, HookMode.Pre);
        RegisterEventHandler<EventPlayerChat>(OnPlayerChat); // 用于兼容 .hp
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _ = Core;
        _settings = Core.Config.SimpleHp;
        CaorenCupModuleRegistry.Register(new CaorenCupModuleDescriptor(
            this, "simplehp", FeatureName, "SimpleHpFeature",
            GetHelpEntry, GetStatusInfo, GetFeatureDescription, GetPublicConfigInfo,
            () => SetEnabled(false)));
    }

    public override void Unload(bool hotReload)
    {
        _damageData.Clear();
        CaorenCupModuleRegistry.Unregister(this);
    }

    private void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (!enabled) _damageData.Clear();
    }

    // --- 管理员指令逻辑 ---

    private void OnCommandHpSet(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupChat.PrintToChat(player, "你没有权限使用此指令。");
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
            Core.SaveConfig();
            CaorenCupChat.PrintToChatAll(CaorenCupChat.FormatChangeMessage("模块控制", FeatureName, $"{ChatColors.Red}已禁用"));
            return;
        }

        // 2. 参数解析
        string target = arg1;
        if (target != "t" && target != "ct" && target != "all")
        {
            if (player != null) CaorenCupChat.PrintToChat(player, "无效的阵营，请使用 t, ct 或 all。");
            return;
        }

        bool mustBeDead = true;
        if (info.ArgCount >= 3)
        {
            if (int.TryParse(info.GetArg(2), out int deadInt)) mustBeDead = (deadInt == 1);
        }

        // 应用配置
        _settings.Target = target;
        _settings.MustBeDead = mustBeDead;
        _settings.Enabled = true;

        Core.SaveConfig();

        string deadStr = mustBeDead ? "必须死亡后查询" : "随时可查询";
        CaorenCupChat.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用! 允许阵营:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} ({deadStr})");
        CaorenCupChat.PrintToChatAll($" {ChatColors.Default}玩家输入 {ChatColors.Green}.hp{ChatColors.Default} 即可查看伤害信息。");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupChat.PrintToChat(player, "=== Simple HP 指令说明 ===");
        CaorenCupChat.PrintToChat(player, $" {ChatColors.Green}/hp_set 0{ChatColors.Default} : 一键禁用查询功能");
        CaorenCupChat.PrintToChat(player, $" {ChatColors.Green}/hp_set <t/ct/all> [1/0是否必须死后查询]{ChatColors.Default}");
        CaorenCupChat.PrintToChat(player, $" 示例: /hp_set all 1 (所有人死亡后可输入 .hp 查询伤害)");
        CaorenCupChat.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏数据记录逻辑 ---

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _damageData.Clear();
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || victim == null || !attacker.IsValid || !victim.IsValid) return HookResult.Continue;
        if (attacker.IsBot && victim.IsBot) return HookResult.Continue;
        if (attacker.Index == victim.Index) return HookResult.Continue;
        if (attacker.TeamNum == victim.TeamNum) return HookResult.Continue; // 不记录友伤

        int attIdx = (int)attacker.Index;
        int vicIdx = (int)victim.Index;

        if (!_damageData.ContainsKey(attIdx)) _damageData[attIdx] = new Dictionary<int, DamageInfo>();
        if (!_damageData[attIdx].ContainsKey(vicIdx)) _damageData[attIdx][vicIdx] = new DamageInfo();

        int dmg = @event.DmgHealth;
        if (dmg < 0) dmg = 0; // 防止负面伤害(回血)干扰统计

        _damageData[attIdx][vicIdx].Hits++;
        _damageData[attIdx][vicIdx].Damage += dmg;

        return HookResult.Continue;
    }

    // --- 玩家查询逻辑 ---

    private void OnCommandHpQuery(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) PerformHpQuery(player);
    }

    // 普通玩家的 .hp 在被其他插件当作 /hp 设置血量指令前拦截。
    // 管理员继续原有聊天流程，保留两份回复。
    private HookResult OnHpSayCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_settings.Enabled || player == null || !player.IsValid ||
            !IsHpChatText(info.ArgString) || AdminManager.PlayerHasPermissions(player, "@css/root"))
            return HookResult.Continue;

        AddTimer(0.1f, () =>
        {
            if (player.IsValid) PerformHpQuery(player);
        });
        return HookResult.Handled;
    }

    private static bool IsHpChatText(string? text) =>
        (text ?? string.Empty).Trim().Trim('"').Equals(".hp", StringComparison.OrdinalIgnoreCase);

    // 兼容玩家输入 ".hp"
    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        if (IsHpChatText(@event.Text))
        {
            var player = Utilities.GetPlayerFromUserid(@event.Userid);
            if (player != null && player.IsValid)
            {
                // 普通玩家由 say 监听查询；即使引擎继续产生聊天事件，也不重复发送。
                if (AdminManager.PlayerHasPermissions(player, "@css/root"))
                {
                    AddTimer(0.1f, () =>
                    {
                        if (player.IsValid) PerformHpQuery(player);
                    });
                }
                return HookResult.Handled; // 拦截消息，不在聊天框里刷屏 .hp
            }
        }
        return HookResult.Continue;
    }

    private void PerformHpQuery(CCSPlayerController player)
    {
        if (!_settings.Enabled) return;

        // 1. 检查队伍权限
        if (!IsTarget(player))
        {
            CaorenCupChat.PrintToChat(player, "你的队伍目前不允许使用伤害查询功能。");
            return;
        }

        // 2. 检查死亡状态
        if (_settings.MustBeDead && player.PawnIsAlive)
        {
            CaorenCupChat.PrintToChat(player, "必须在死亡后才能查询伤害数据。");
            return;
        }

        int pIdx = (int)player.Index;
        if (!_damageData.ContainsKey(pIdx) || _damageData[pIdx].Count == 0)
        {
            CaorenCupChat.PrintToChat(player, "本回合未对任何敌人造成伤害。");
            return;
        }

        var targets = _damageData[pIdx];
        bool foundAny = false;
        StringBuilder sb = new StringBuilder();

        CaorenCupChat.PrintToChat(player, "=== 本回合伤害报告 ===");

        foreach (var kvp in targets)
        {
            int vicIdx = kvp.Key;
            var dmgInfo = kvp.Value;
            var victim = Utilities.GetPlayerFromIndex(vicIdx);

            if (victim != null && victim.IsValid)
            {
                bool isAlive = victim.PawnIsAlive;
                if (!_settings.ShowKilled && !isAlive) continue;

                int currentHp = 0;
                if (isAlive && victim.PlayerPawn.Value != null)
                {
                    currentHp = victim.PlayerPawn.Value.Health;
                    if (currentHp < 0) currentHp = 0;
                }

                sb.Clear();
                sb.Append(" 命中 ");

                string name = _settings.ShowId ? $"{ChatColors.Red}{victim.PlayerName}{ChatColors.Default}" : "敌人";
                sb.Append(name);

                if (_settings.ShowHits) sb.Append($" {ChatColors.Green}{dmgInfo.Hits}{ChatColors.Default}次");
                if (_settings.ShowDmg) sb.Append($"，共 {ChatColors.Green}{dmgInfo.Damage}{ChatColors.Default}伤害");

                if (_settings.ShowHp)
                {
                    if (isAlive) sb.Append($"，剩余 {ChatColors.Green}{currentHp}{ChatColors.Default} HP");
                    else sb.Append($"，{ChatColors.Red}已阵亡{ChatColors.Default}");
                }

                CaorenCupChat.PrintToChat(player, sb.ToString());
                foundAny = true;
            }
        }

        if (!foundAny)
        {
            CaorenCupChat.PrintToChat(player, "本回合没有符合显示条件的伤害记录。");
        }
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
        return $" {ChatColors.Green}/hp_set{ChatColors.Default} : 管理员设置伤害查询模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"SimpleHp: {ChatColors.Red}已禁用{ChatColors.Default}";
        string deadInfo = _settings.MustBeDead ? "死后可查" : "随时可查";
        return $"SimpleHp: {ChatColors.Green}启用{ChatColors.Default} | 允许:{_settings.Target.ToUpper()} | 条件:{deadInfo}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        string target = _settings.Target == "all" ? "全员" : $"{_settings.Target.ToUpper()} 阵营";
        string cond = _settings.MustBeDead ? "被击杀后" : "随时";
        return $"[伤害查询] {target} {cond}输入 .hp 或 /crhp 查看造成的伤害。";
    }

    public string GetFeatureDescription()
    {
        return " [Simple HP] 伤害信息查询功能。\n" +
               " - 被授予权限的玩家在游戏内聊天框输入 '.hp' 即可查看本回合对敌人造成的伤害。\n" +
               " - 可以统计命中次数、造成的总伤害，以及敌人剩余的血量。";
    }
}
