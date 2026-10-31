using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features;

public class AmmoFeature : ICaorenFeature
{
    public string FeatureName => "无中生有(弹药/道具概率保留)模块";

    private CaorenCupPlugin _plugin = null!;
    private CaorenCupConfig _config = null!;
    private readonly Random _rnd = new Random();

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 监听开火事件 (处理枪械子弹)
        _plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);

        // 监听投掷物扔出事件 (处理道具保留)
        _plugin.RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown, HookMode.Post);

        _plugin.AddCommand("css_ammo", "控制弹药与道具的消耗概率", OnAmmoCommand);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _config = config;
    }

    public void OnUnload() { }

    public void SetEnabled(bool enabled)
    {
        _config.Ammo.Enabled = enabled;
        _plugin.SaveConfig();
    }

    // --- 集成指令处理器 ---
    private void OnAmmoCommand(CCSPlayerController? player, CommandInfo info)
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
                CaorenCupUtils.PrintToChat(player, "用法: \u0004/ammo <t/ct/all/0> [枪械概率] [道具概率]");
                CaorenCupUtils.PrintToChat(player, "说明: \u0001接 \u00040\u0001 为禁用模块。概率请填写 \u00040~100\u0001 的数字。");
                CaorenCupUtils.PrintToChat(player, "范例: \u0004/ammo all 50 20 \u0001(代表所有人开枪50%不耗弹，扔雷20%不消耗)");
            }
            return;
        }

        string targetArg = info.GetArg(1).ToLower();

        if (targetArg == "0")
        {
            SetEnabled(false);
            string msg = "已 \u0002禁用\u0001 无中生有(弹药)模块。";
            if (player != null) CaorenCupUtils.PrintToChatAll(msg); else Console.WriteLine(msg);
            return;
        }

        if (targetArg != "t" && targetArg != "ct" && targetArg != "all")
        {
            if (player != null) CaorenCupUtils.PrintToChat(player, "无效阵营，请使用 \u0004t\u0001, \u0004ct\u0001, \u0004all\u0001，或使用 \u00040\u0001 禁用。");
            return;
        }

        float bulletProb = _config.Ammo.BulletChance;
        float nadeProb = _config.Ammo.GrenadeChance;

        if (argCount >= 3) float.TryParse(info.GetArg(2), out bulletProb);
        if (argCount >= 4) float.TryParse(info.GetArg(3), out nadeProb);

        // 限制在 0-100 之间
        bulletProb = Math.Clamp(bulletProb, 0f, 100f);
        nadeProb = Math.Clamp(nadeProb, 0f, 100f);

        _config.Ammo.Enabled = true;
        _config.Ammo.Target = targetArg;
        _config.Ammo.BulletChance = bulletProb;
        _config.Ammo.GrenadeChance = nadeProb;
        _plugin.SaveConfig();

        string msgAll = $"弹药规则已改变 -> 阵营: \u0004{targetArg.ToUpper()}\u0001 | 不耗弹: \u0004{bulletProb}%\u0001 | 不耗雷: \u0004{nadeProb}%\u0001";
        CaorenCupUtils.PrintToChatAll(msgAll);
    }

    // --- 核心逻辑 1：枪械子弹返还与 UI 同步 ---
    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!_config.Ammo.Enabled || _config.Ammo.BulletChance <= 0) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;

        // 检查阵营
        bool isMatch = _config.Ammo.Target == "all" ||
                      (_config.Ammo.Target == "t" && player.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.Ammo.Target == "ct" && player.TeamNum == (byte)CsTeam.CounterTerrorist);

        if (!isMatch) return HookResult.Continue;

        // 排除非枪械武器 (刀、电击枪、手雷等)
        string weaponName = @event.Weapon;
        if (weaponName.Contains("knife") || weaponName.Contains("bayonet") ||
            weaponName.Contains("grenade") || weaponName.Contains("flashbang") ||
            weaponName.Contains("molotov") || weaponName.Contains("decoy") || weaponName.Contains("taser"))
        {
            return HookResult.Continue;
        }

        // RNG 掷骰子判定 (生成 0~100 的随机数)
        if (_rnd.NextDouble() * 100.0 <= _config.Ammo.BulletChance)
        {
            var pawn = player.PlayerPawn.Value;
            var activeWeapon = pawn?.WeaponServices?.ActiveWeapon.Value;

            if (activeWeapon != null && activeWeapon.IsValid)
            {
                // CS2 引擎已经在开火时扣除了 1 发子弹，我们在此刻给它加回来
                activeWeapon.Clip1 += 1;

                // 【核心同步机制】强制通知客户端 UI 更新，防止右下角数字抖动卡死
                Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_iClip1");
            }
        }

        return HookResult.Continue;
    }

    // --- 核心逻辑 2：道具扔出后重新发放 ---
    private HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
    {
        if (!_config.Ammo.Enabled || _config.Ammo.GrenadeChance <= 0) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;

        bool isMatch = _config.Ammo.Target == "all" ||
                      (_config.Ammo.Target == "t" && player.TeamNum == (byte)CsTeam.Terrorist) ||
                      (_config.Ammo.Target == "ct" && player.TeamNum == (byte)CsTeam.CounterTerrorist);

        if (!isMatch) return HookResult.Continue;

        // RNG 掷骰子
        if (_rnd.NextDouble() * 100.0 <= _config.Ammo.GrenadeChance)
        {
            string thrownWeapon = @event.Weapon;
            // 补齐前缀 (例如 "hegrenade" 转换为 "weapon_hegrenade")
            if (!thrownWeapon.StartsWith("weapon_"))
            {
                thrownWeapon = "weapon_" + thrownWeapon;
            }

            // 【核心防打断机制】
            // 如果在道具刚离手的瞬间立刻发武器，会强制打断玩家的投掷后摇手部动作。
            // 延迟 0.1 秒发枪是最佳的平滑过渡方案，UI会像魔术一样重新刷出一个雷。
            _plugin.AddTimer(0.1f, () =>
            {
                if (player.IsValid && player.PawnIsAlive)
                {
                    player.GiveNamedItem(thrownWeapon);
                }
            });
        }

        return HookResult.Continue;
    }

    // --- 接口信息实现 ---
    public string GetHelpEntry() => "/ammo - 无中生有(弹药/道具概率保留)控制";

    public string GetStatusInfo() => _config.Ammo.Enabled ? $"已开启 (阵营:{_config.Ammo.Target.ToUpper()} 不耗弹:{_config.Ammo.BulletChance}% 不耗雷:{_config.Ammo.GrenadeChance}%)" : "已禁用";

    public string? GetPublicConfigInfo() => _config.Ammo.Enabled ? $"无中生有: \u0004{_config.Ammo.Target.ToUpper()}\u0001 阵营部分弹药不消耗" : null;

    public string GetFeatureDescription()
    {
        if (!_config.Ammo.Enabled)
            return "物资配给目前一切正常。";

        string target = _config.Ammo.Target.ToUpper() == "ALL" ? "所有" : _config.Ammo.Target.ToUpper();

        return $"【无中生有】{target} 阵营的玩家在扣动扳机时，有概率不消耗任何子弹；扔出道具时，也有概率不消耗。";
    }
}