using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System;

namespace CaorenCup.Features;

public class FriendlyFireFeature : ICaorenFeature
{
    public string FeatureName => "Friendly Fire (友伤控制)";

    private CaorenCupPlugin _plugin = null!;
    private FriendlyFireSettings _settings = null!;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 注册整合型指令
        plugin.AddCommand("css_ffire", "设置友伤: css_ffire <t/ct/all/0> <倍率> <1/0是否允许击杀>", OnCommandFfire);

        // 核心伤害拦截钩子
        plugin.RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnTakeDamagePre);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.FriendlyFire;
    }

    public void OnUnload()
    {
        // 无需清理定时器
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
    }

    // --- 核心指令逻辑 ---

    private void OnCommandFfire(CCSPlayerController? player, CommandInfo info)
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
            CaorenCupUtils.PrintToChatAll(CaorenCupUtils.FormatChangeMessage("模块控制", FeatureName, $"{ChatColors.Red}已禁用 (恢复官方默认友伤规则)"));
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

        if (!float.TryParse(info.GetArg(2), out float multiplier))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的倍数数值。");
            return;
        }

        if (!int.TryParse(info.GetArg(3), out int allowKillInt) || (allowKillInt != 0 && allowKillInt != 1))
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效的保护参数，只能输入 1(允许击杀) 或 0(禁止击杀留1血)。");
            return;
        }

        bool allowKill = (allowKillInt == 1);

        // 应用配置 (后覆盖机制)
        _settings.Target = target;
        _settings.Multiplier = multiplier;
        _settings.AllowKill = allowKill;
        _settings.Enabled = true;

        _plugin.SaveConfig();

        // 格式化输出文本
        string effectStr = multiplier > 0 ? $"{ChatColors.Red}{multiplier}x 伤害" : (multiplier < 0 ? $"{ChatColors.Green}{Math.Abs(multiplier)}x 回血" : $"{ChatColors.Blue}无伤害");
        string killStr = allowKill ? $"{ChatColors.Red}可以致死" : $"{ChatColors.Green}丝血保护";

        CaorenCupUtils.PrintToChatAll($" {ChatColors.Green}[草人杯]{ChatColors.Default} {FeatureName} 启用!");
        CaorenCupUtils.PrintToChatAll($" 阵营:{ChatColors.Green}{target.ToUpper()}{ChatColors.Default} | 效果:{effectStr}{ChatColors.Default} | 保护:{killStr}{ChatColors.Default}");
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== Friendly Fire 指令说明 ===");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/ffire 0{ChatColors.Default} : 一键禁用并恢复官方规则");
        CaorenCupUtils.PrintToChat(player, $" {ChatColors.Green}/ffire <t/ct/all> <倍数> <1/0是否可击杀>{ChatColors.Default}");
        CaorenCupUtils.PrintToChat(player, $" 示例: /ffire all 1 0 (全员打队友1倍全额伤害，但打不死会留1血)");
        CaorenCupUtils.PrintToChat(player, $" 示例: /ffire t -1 1 (T阵营打队友变成回血，击杀参数无所谓)");
        CaorenCupUtils.PrintToChat(player, $" 注意: 官方默认倍率为 0.33。输入 0 为免疫友伤。");
        CaorenCupUtils.PrintToChat(player, $" 当前状态: {GetStatusInfo()}");
    }

    // --- 游戏核心拦截逻辑 ---

    private HookResult OnTakeDamagePre(CCSPlayerPawn victimPawn, CTakeDamageInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;
        if (victimPawn == null || !victimPawn.IsValid) return HookResult.Continue;

        var victim = victimPawn.Controller.Value as CCSPlayerController;
        var attackerPawn = info.Attacker.Value?.As<CCSPlayerPawn>();
        var attacker = attackerPawn?.Controller.Value as CCSPlayerController;

        // 排除非玩家互相伤害、自己打自己等情况
        if (victim == null || attacker == null || victim == attacker) return HookResult.Continue;

        // 我们只处理友伤 (同阵营)
        if (victim.TeamNum != attacker.TeamNum) return HookResult.Continue;

        // 检查攻击者是否在我们的管控阵营目标内
        if (!IsTarget(attacker)) return HookResult.Continue;

        // 1. 识别官方引擎自带的减伤系数
        float engineCoeff = 0.33f; // 默认子弹友伤系数
        var ability = info.Ability.Value;
        string designerName = ability?.DesignerName ?? "";

        if (designerName.Contains("grenade") || designerName.Contains("molotov") || designerName.Contains("flash"))
            engineCoeff = 0.85f;
        else if (designerName.Contains("knife") || designerName.Contains("taser"))
            engineCoeff = 0.40f;

        // 2. 还原真实的未衰减伤害
        // 注意：传进来的 info.Damage 已经被引擎乘过 engineCoeff 了
        float originalRawDmg = info.Damage / engineCoeff;

        // 3. 乘上我们自定义的倍率
        float realDmg = originalRawDmg * _settings.Multiplier;

        // --- 处理免疫 (倍率 == 0) ---
        if (_settings.Multiplier == 0)
        {
            info.Damage = 0;
            return HookResult.Continue;
        }

        // --- 处理回血 (倍率 < 0) ---
        if (_settings.Multiplier < 0)
        {
            info.Damage = 0; // 阻止原有的伤害扣血

            // 计算该回多少血
            int healAmount = (int)Math.Round(Math.Abs(realDmg));
            if (healAmount > 0)
            {
                // 为了安全，延迟 0.1 秒回血，防止与引擎底层扣血事件冲突
                _plugin.AddTimer(0.1f, () =>
                {
                    if (victimPawn.IsValid && victimPawn.Health > 0)
                    {
                        int maxHp = victimPawn.MaxHealth > 0 ? victimPawn.MaxHealth : 100;
                        maxHp = CaorenCupUtils.GetHpCapMax(_plugin, maxHp);
                        int newHp = Math.Min(victimPawn.Health + healAmount, maxHp);
                        CaorenCupUtils.ApplyModuleHealth(_plugin, victimPawn, newHp);
                    }
                });
            }
            return HookResult.Continue;
        }

        // --- 处理扣血 (倍率 > 0) ---
        if (_settings.Multiplier > 0)
        {
            // /hpcap 开启时，友伤模块造成的扣血不能低于全局下限。未开启时保留原来的 AllowKill 行为。
            if (CaorenCupUtils.IsHpCapEnabled(_plugin))
            {
                realDmg = CaorenCupUtils.ClampModuleDamageByHpCap(_plugin, victimPawn, realDmg);
            }
            else if (!_settings.AllowKill)
            {
                if (realDmg >= victimPawn.Health)
                {
                    // 如果算出的伤害足以致死，且玩家当前血量 > 1，则将其锁在 1 血
                    if (victimPawn.Health > 1)
                    {
                        realDmg = victimPawn.Health - 1;
                    }
                    else
                    {
                        // 如果玩家本来就只有 1 血了，直接强制伤害为 0，防止打死
                        realDmg = 0;
                    }
                }
            }

            // 覆盖最终伤害
            info.Damage = realDmg;
        }

        return HookResult.Continue;
    }

    private bool IsTarget(CCSPlayerController attacker)
    {
        if (_settings.Target == "all") return true;
        if (_settings.Target == "t" && attacker.Team == CsTeam.Terrorist) return true;
        if (_settings.Target == "ct" && attacker.Team == CsTeam.CounterTerrorist) return true;
        return false;
    }

    // --- 接口实现 ---

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/ffire{ChatColors.Default} : 查看并设置 友军伤害 模块";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled) return $"FFire: {ChatColors.Red}已禁用 (官方默认){ChatColors.Default}";
        string kill = _settings.AllowKill ? "允许击杀" : "免死保护";
        return $"FFire: {ChatColors.Green}启用{ChatColors.Default} | 目标:{_settings.Target.ToUpper()} | 倍率:{_settings.Multiplier}x | 保护:{kill}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;
        string target = _settings.Target == "all" ? "全员" : $"{_settings.Target.ToUpper()} 阵营";
        string killText = _settings.AllowKill ? "" : " (受到队友致命伤时会保留1HP)";

        string effect;
        if (_settings.Multiplier == 0) effect = "免疫队友伤害";
        else if (_settings.Multiplier < 0) effect = $"受队友攻击将恢复 {Math.Abs(_settings.Multiplier)}倍 生命值";
        else effect = $"受队友伤害变为真实伤害的 {_settings.Multiplier}倍";

        return $"[友伤规则] {target} {effect}{killText}。";
    }

    public string GetFeatureDescription()
    {
        return " [FriendlyFire Control] 深度自定义队伍内的伤害规则。\n" +
               " - 越过官方强制的 33% 减伤，实现真正全额(或多倍)的队友伤害。\n" +
               " - 倍率为负数时：枪口对准队友开火即可为队友加血！\n" +
               " - 丝血保护：当允许击杀设置为0时，无论多高伤害都无法杀掉队友。";
    }
}