using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars;
using CaorenCup;

namespace CaorenCup.Features;

public class OMAFeature : ICaorenFeature
{
    public string FeatureName => "One Man Army";

    private OMASettings _settings = new();
    private CaorenCupPlugin _plugin = null!;

    // 运行时状态
    private int _soloTeam = 0; // 0=Off, 2=T, 3=CT
    private CounterStrikeSharp.API.Modules.Timers.Timer? _wallhackTimer = null;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _radarUpdateTimer = null;
    private bool _isWallhackActive = false;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        // 事件
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        plugin.RegisterEventHandler<EventItemPickup>(OnItemPickup);
        plugin.RegisterEventHandler<EventItemPurchase>(OnItemPurchase);
        plugin.RegisterEventHandler<EventAnnouncePhaseEnd>(OnHalftime);

        // === 第三层入口 ===
        plugin.AddCommand("oma", "一人成军菜单", OnCommandHelpMenu);

        // === 第四层具体指令 ===
        plugin.AddCommand("oma_mode", "设置模式: /oma_mode <0/t/ct>", OnCommandMode);
        plugin.AddCommand("oma_solo", "设置Solo属性: /oma_solo <hp/ap/speed/dmg> <val>", OnCommandSolo);
        plugin.AddCommand("oma_team", "设置Team属性: /oma_team <hp/speed/ban_awp> <val>", OnCommandTeam);
        plugin.AddCommand("oma_wh", "透视设置: /oma_wh <0/1> [dur] [int]", OnCommandWh);
        plugin.AddCommand("oma_swap", "手动换边", OnCommandSwap);
    }

    public void OnConfigParsed(CaorenCup.CaorenCupConfig config)
    {
        _settings = config.OneManArmy;
        _soloTeam = _settings.DefaultMode; // 加载默认配置
    }

    public void OnUnload()
    {
        StopWallhackTimer();
        _soloTeam = 0;
    }

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/oma{ChatColors.Default} 一人成军(1vN)模式指令";
    }

    public string GetStatusInfo()
    {
        string mode = _soloTeam == 0 ? $"{ChatColors.Red}关闭" : (_soloTeam == 2 ? $"{ChatColors.Red}T Solo" : $"{ChatColors.Blue}CT Solo");
        return $" {ChatColors.Olive}OMA{ChatColors.Default}: {mode} | SoloHP:{_settings.SoloHealth} | WH:{_settings.WallhackEnabled}";
    }

    // --- 指令逻辑 ---

    private void OnCommandHelpMenu(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        CaorenCupUtils.PrintToChat(player, "=== OMA 指令 ===");
        player.PrintToChat($" {ChatColors.Green}/oma_mode <0/t/ct>{ChatColors.Default} 开启/关闭模式");
        player.PrintToChat($" {ChatColors.Green}/oma_solo <hp/ap/speed/dmg> <val>{ChatColors.Default} 设置Solo属性");
        player.PrintToChat($" {ChatColors.Green}/oma_team <hp/speed/ban_awp> <val>{ChatColors.Default} 设置Team属性");
        player.PrintToChat($" {ChatColors.Green}/oma_wh <0/1>{ChatColors.Default} 开关雷达透视");
        player.PrintToChat($" {ChatColors.Green}/oma_swap{ChatColors.Default} 强制换边");
    }

    private void OnCommandMode(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;
        if (info.ArgCount < 2) { CaorenCupUtils.PrintToChat(caller, "用法: /oma_mode <0/t/ct>"); return; }

        string arg = info.GetArg(1).ToLower();
        if (arg == "0") _soloTeam = 0;
        else if (arg == "t" || arg == "2") _soloTeam = 2;
        else if (arg == "ct" || arg == "3") _soloTeam = 3;
        else { CaorenCupUtils.PrintToChat(caller, "无效参数"); return; }

        string modeStr = _soloTeam == 0 ? "关闭" : (_soloTeam == 2 ? "T Solo" : "CT Solo");
        CaorenCupUtils.PrintToChatAll(CaorenCupUtils.FormatChangeMessage("OMA模式", "状态", modeStr));
    }

    private void OnCommandSolo(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;
        if (info.ArgCount < 3) { CaorenCupUtils.PrintToChat(caller, "用法: /oma_solo <hp/ap/speed/dmg> <val>"); return; }

        string key = info.GetArg(1).ToLower();
        string valStr = info.GetArg(2);

        if (key == "hp" && int.TryParse(valStr, out int hp)) { _settings.SoloHealth = hp; CaorenCupUtils.PrintToChatAll($"Solo血量设为 {hp}"); }
        else if (key == "ap" && int.TryParse(valStr, out int ap)) { _settings.SoloArmor = ap; CaorenCupUtils.PrintToChatAll($"Solo护甲设为 {ap}"); }
        else if (key == "speed" && float.TryParse(valStr, out float spd)) { _settings.SoloSpeed = spd; CaorenCupUtils.PrintToChatAll($"Solo速度设为 {spd}"); }
        else if (key == "dmg" && float.TryParse(valStr, out float dmg)) { _settings.SoloDamage = dmg; CaorenCupUtils.PrintToChatAll($"Solo伤害设为 {dmg}"); }
    }

    private void OnCommandTeam(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;
        if (info.ArgCount < 3) { CaorenCupUtils.PrintToChat(caller, "用法: /oma_team <hp/speed/ban_awp> <val>"); return; }

        string key = info.GetArg(1).ToLower();
        string valStr = info.GetArg(2);

        if (key == "hp" && int.TryParse(valStr, out int hp)) { _settings.TeamHealth = hp; CaorenCupUtils.PrintToChatAll($"Team血量设为 {hp}"); }
        else if (key == "speed" && float.TryParse(valStr, out float spd)) { _settings.TeamSpeed = spd; CaorenCupUtils.PrintToChatAll($"Team速度设为 {spd}"); }
        else if (key == "ban_awp") { _settings.BanTeamAWP = (valStr == "1"); CaorenCupUtils.PrintToChatAll($"Team禁狙: {_settings.BanTeamAWP}"); }
    }

    private void OnCommandWh(CCSPlayerController? caller, CommandInfo info)
    {
        if (caller == null) return;
        if (info.ArgCount < 2) { CaorenCupUtils.PrintToChat(caller, "用法: /oma_wh <0/1>"); return; }

        _settings.WallhackEnabled = (info.GetArg(1) == "1");
        CaorenCupUtils.PrintToChatAll($"雷达透视: {(_settings.WallhackEnabled ? "开启" : "关闭")}");
    }

    private void OnCommandSwap(CCSPlayerController? caller, CommandInfo info)
    {
        if (_soloTeam == 0) return;
        _soloTeam = (_soloTeam == 2) ? 3 : 2;
        CaorenCupUtils.PrintToChatAll($"强制换边！现在 {(_soloTeam == 2 ? "T" : "CT")} 是独勇者！");
    }

    // --- 游戏逻辑 ---

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_soloTeam == 0) return HookResult.Continue;

        // 设置 C4 时间
        var c4Cvar = ConVar.Find("mp_c4timer");
        if (c4Cvar != null)
        {
            if (_soloTeam == 3) c4Cvar.SetValue(_settings.C4TimerSoloCT);
            else c4Cvar.SetValue(_settings.C4TimerDefault);
        }

        // 启动透视循环
        if (_settings.WallhackEnabled)
        {
            StartWallhackCycle();
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        StopWallhackTimer();
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (_soloTeam == 0) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return HookResult.Continue;

        _plugin.AddTimer(0.2f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;

            if (player.TeamNum == _soloTeam)
            {
                // Solo 属性
                int soloHealth = CaorenCupUtils.ClampModuleSetHealth(_plugin, _settings.SoloHealth);
                CaorenCupUtils.ApplyModuleSetHealth(_plugin, pawn, soloHealth);
                pawn.MaxHealth = Math.Max(pawn.MaxHealth, soloHealth); // 保持 OMA 原有高血量行为
                try { Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth"); } catch { }
                pawn.ArmorValue = _settings.SoloArmor;
                pawn.VelocityModifier = _settings.SoloSpeed;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
            else
            {
                // Team 属性
                CaorenCupUtils.ApplyModuleSetHealth(_plugin, pawn, _settings.TeamHealth);
                pawn.VelocityModifier = _settings.TeamSpeed;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (_soloTeam == 0) return HookResult.Continue;

        // 处理伤害加成：只有 Solo 打别人才加成
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker != null && attacker.IsValid && attacker.TeamNum == _soloTeam && attacker.TeamNum != victim?.TeamNum)
        {
            if (_settings.SoloDamage > 1.0f)
            {
                // 已经造成的伤害是 DmgHealth，我们需要额外扣除 (Multiplier - 1) * DmgHealth
                int extra = (int)(@event.DmgHealth * (_settings.SoloDamage - 1.0f));
                if (extra > 0 && victim?.PlayerPawn.Value != null)
                {
                    var vp = victim.PlayerPawn.Value;
                    int newHp = vp.Health - extra;
                    if (newHp <= 0 && !CaorenCupUtils.IsHpCapEnabled(_plugin))
                    {
                        vp.Health = 0;
                        Utilities.SetStateChanged(vp, "CBaseEntity", "m_iHealth");
                        vp.CommitSuicide(false, true);
                    }
                    else
                    {
                        CaorenCupUtils.ApplyModuleHealth(_plugin, vp, newHp);
                    }
                }
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (_soloTeam == 0) return HookResult.Continue;

        // Solo 击杀回血
        var attacker = @event.Attacker;
        if (attacker != null && attacker.TeamNum == _soloTeam)
        {
            var p = attacker.PlayerPawn.Value;
            if (p != null)
            {
                int maxHp = CaorenCupUtils.GetHpCapMax(_plugin, _settings.SoloHealth);
                CaorenCupUtils.ApplyModuleHealth(_plugin, p, Math.Min(maxHp, p.Health + _settings.SoloHeal));
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (_soloTeam == 0 || !_settings.InfiniteAmmo) return HookResult.Continue;

        var p = @event.Userid;
        if (p != null && p.TeamNum == _soloTeam)
        {
            var w = p.Pawn.Value?.WeaponServices?.ActiveWeapon.Value;
            if (w != null)
            {
                w.Clip1 = 100; w.ReserveAmmo[0] = 100;
                Utilities.SetStateChanged(w, "CBasePlayerWeapon", "m_iClip1");
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnItemPurchase(EventItemPurchase @event, GameEventInfo info)
    {
        if (_soloTeam == 0) return HookResult.Continue;
        var player = @event.Userid;
        if (player != null && player.TeamNum != _soloTeam)
        {
            if (_settings.BanTeamAWP && @event.Weapon == "weapon_awp")
            {
                CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}挑战者禁止购买 AWP！");
                return HookResult.Stop;
            }
            if (_settings.BanTeamHelm && @event.Weapon == "item_assaultsuit")
            {
                CaorenCupUtils.PrintToChat(player, $"{ChatColors.Red}挑战者禁止购买头盔！");
                return HookResult.Stop;
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        if (_soloTeam == 0) return HookResult.Continue;
        var player = @event.Userid;

        if (_settings.BanTeamAWP && player != null && player.TeamNum != _soloTeam && (@event.Item == "awp" || @event.Item == "weapon_awp"))
        {
            player.ExecuteClientCommand("slot1");
            player.ExecuteClientCommand("drop");
        }
        return HookResult.Continue;
    }

    private HookResult OnHalftime(EventAnnouncePhaseEnd @event, GameEventInfo info)
    {
        if (_soloTeam == 0 || !_settings.AutoSwap) return HookResult.Continue;
        _soloTeam = (_soloTeam == 2) ? 3 : 2;
        string t = _soloTeam == 3 ? "CT方" : "T方";
        CaorenCupUtils.PrintToChatAll($"半场互换！{ChatColors.Green}{t}{ChatColors.Default} 成为独勇者！");
        return HookResult.Continue;
    }

    // --- 透视逻辑 ---
    private void StartWallhackCycle()
    {
        StopWallhackTimer();
        ToggleRadar(true);
        _wallhackTimer = _plugin.AddTimer(_settings.WallhackDuration, () =>
        {
            ToggleRadar(false);
            _wallhackTimer = _plugin.AddTimer(_settings.WallhackInterval, StartWallhackCycle);
        });
    }

    private void StopWallhackTimer()
    {
        if (_wallhackTimer != null) { _wallhackTimer.Kill(); _wallhackTimer = null; }
        ToggleRadar(false);
    }

    private void ToggleRadar(bool enable)
    {
        _isWallhackActive = enable;
        if (_radarUpdateTimer != null) { _radarUpdateTimer.Kill(); _radarUpdateTimer = null; }

        if (enable)
        {
            // 只有 Solo 玩家收到提示
            foreach (var p in Utilities.GetPlayers())
                if (p.TeamNum == _soloTeam) p.PrintToCenter("雷达透视已开启！");

            _radarUpdateTimer = _plugin.AddTimer(0.5f, UpdateRadarDots, TimerFlags.REPEAT);
        }
    }

    private void UpdateRadarDots()
    {
        if (!_isWallhackActive || _soloTeam == 0) return;

        foreach (var p in Utilities.GetPlayers())
        {
            // 让所有挑战者暴露
            if (p != null && p.IsValid && p.PawnIsAlive && p.TeamNum != _soloTeam)
            {
                var pawn = p.PlayerPawn.Value;
                if (pawn != null)
                {
                    pawn.EntitySpottedState.Spotted = true;
                    // CS2 机制：Spotted = true 会在雷达上显示红点
                }
            }
        }
    }
    public void SetEnabled(bool enabled)
    {
        // OMA 配置没有 Enabled 字段，它是靠 _soloTeam 判断的
        // 这里我们约定：开启=默认T Solo(2)，关闭=0
        if (enabled)
        {
            _soloTeam = 2; // 或者读取 _settings.DefaultMode
        }
        else
        {
            _soloTeam = 0;
            StopWallhackTimer();
        }
    }
    public string? GetPublicConfigInfo()
    {
        if (_soloTeam == 0) return null;
        string solo = _soloTeam == 2 ? "T" : "CT";
        return $"[一人成军] {solo}方为独勇者 (HP:{_settings.SoloHealth}, 伤害:{_settings.SoloDamage}x)";
    }
    public string GetFeatureDescription()
    {
        return " [OneManArmy] 非对称竞技模式。\n" +
               " 独勇者(Solo)拥有超高血量、伤害加成和透视能力。\n" +
               " 挑战者(Team)人数众多但装备受限。击杀独勇者即获胜。";
    }
}