using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace CaorenCup.Features;

public class WeaponSpeedFeature : ICaorenFeature
{
    public string FeatureName => "武器速度控制 (WeaponSpeed)";

    private CaorenCupPlugin _plugin = null!;
    private readonly WeaponSpeedRuntimeSettings _settings = new();
    private readonly PlayerWeaponSpeedState[] _states = new PlayerWeaponSpeedState[65];

    private sealed class WeaponSpeedRuntimeSettings
    {
        public bool Enabled { get; set; } = false;
        public string Target { get; set; } = "all";

        // 100 = 原版；200 = 2倍；50 = 半速
        public float SwitchSpeedPercent { get; set; } = 100.0f;
        public float FireSpeedPercent { get; set; } = 100.0f;
    }

    private sealed class PlayerWeaponSpeedState
    {
        public string LastWeaponName { get; set; } = "";

        // 记录上一次由本插件写入的攻击 Tick，避免每 Tick 反复压缩同一段冷却
        public int LastAppliedPrimaryTick { get; set; } = 0;
        public int LastAppliedSecondaryTick { get; set; } = 0;

        // 后坐力补偿用：记录开火前一帧的后坐力状态
        public bool HasLastAimPunchAngle { get; set; } = false;
        public QAngle LastAimPunchAngle { get; set; } = new(0, 0, 0);

        public bool HasLastAimPunchVelocity { get; set; } = false;
        public QAngle LastAimPunchVelocity { get; set; } = new(0, 0, 0);

        public bool HasLastViewPunchAngle { get; set; } = false;
        public QAngle LastViewPunchAngle { get; set; } = new(0, 0, 0);

        public int RecoilShotToken { get; set; } = 0;
        public int LastCompensatedRecoilToken { get; set; } = 0;
    }

    private const int MaxScalableAttackDelayTicks = 192;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        for (int i = 0; i < _states.Length; i++)
            _states[i] = new PlayerWeaponSpeedState();

        _plugin.AddCommand("css_wspd", "武器速度控制: /wspd <t/ct/all/0> <switchSpeed> <fireSpeed>", OnCommand);
        _plugin.AddCommand("wspd", "武器速度控制: /wspd <t/ct/all/0> <switchSpeed> <fireSpeed>", OnCommand);

        _plugin.RegisterListener<Listeners.OnTick>(OnTick);
        _plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);

        _plugin.RegisterEventHandler<EventPlayerSpawn>((@event, info) =>
        {
            var p = @event.Userid;
            if (p != null && p.IsValid && p.Slot >= 0 && p.Slot < _states.Length)
                _states[p.Slot] = new PlayerWeaponSpeedState();

            return HookResult.Continue;
        });

        _plugin.RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            var p = @event.Userid;
            if (p != null && p.Slot >= 0 && p.Slot < _states.Length)
                _states[p.Slot] = new PlayerWeaponSpeedState();

            return HookResult.Continue;
        });
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        // 独立运行版，不写入 CaorenCup.json。
    }

    public void OnUnload()
    {
        SetEnabled(false);
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;

        if (!enabled)
        {
            for (int i = 0; i < _states.Length; i++)
                _states[i] = new PlayerWeaponSpeedState();
        }
    }

    private void OnCommand(CCSPlayerController? player, CommandInfo info)
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

        string target = info.GetArg(1).Trim().ToLowerInvariant();

        if (target == "0" || target == "off" || target == "disable")
        {
            SetEnabled(false);
            CaorenCupUtils.PrintToChatAll($"{ChatColors.Red}武器速度控制已禁用。");
            return;
        }

        if (target != "t" && target != "ct" && target != "all")
        {
            Reply(player, "目标错误，请使用 t / ct / all / 0。");
            return;
        }

        if (info.ArgCount < 4)
        {
            PrintUsage(player);
            return;
        }

        if (!float.TryParse(info.GetArg(2), out float switchSpeed))
        {
            Reply(player, "切枪速度数值无效。");
            return;
        }

        if (!float.TryParse(info.GetArg(3), out float fireSpeed))
        {
            Reply(player, "射击速度数值无效。");
            return;
        }

        switchSpeed = Math.Clamp(switchSpeed, 10.0f, 500.0f);
        fireSpeed = Math.Clamp(fireSpeed, 10.0f, 500.0f);

        _settings.Enabled = true;
        _settings.Target = target;
        _settings.SwitchSpeedPercent = switchSpeed;
        _settings.FireSpeedPercent = fireSpeed;

        for (int i = 0; i < _states.Length; i++)
            _states[i] = new PlayerWeaponSpeedState();

        CaorenCupUtils.PrintToChatAll(
            $"{ChatColors.Green}武器速度控制已启用！{ChatColors.Default} " +
            $"目标:{ChatColors.Green}{GetTargetDisplayName(target)}{ChatColors.Default} | " +
            $"切枪:{ChatColors.Green}{switchSpeed:F0}%{ChatColors.Default} | " +
            $"射击:{ChatColors.Green}{fireSpeed:F0}%{ChatColors.Default}"
        );

        if (info.ArgCount >= 5)
        {
            Reply(player, $"{ChatColors.Yellow}提示：新版已移除换弹速度参数，第三个速度参数会被忽略。");
        }
    }

    private void PrintUsage(CCSPlayerController? player)
    {
        Reply(player, "=== 武器速度控制 WSpeed ===");
        Reply(player, $"{ChatColors.Green}/wspd 0{ChatColors.Default} : 关闭模块");
        Reply(player, $"{ChatColors.Green}/wspd <t/ct/all> <switchSpeed> <fireSpeed>{ChatColors.Default}");
        Reply(player, $"示例: {ChatColors.Green}/wspd all 100 200{ChatColors.Default}  切枪不变，射速 200%");
        Reply(player, $"示例: {ChatColors.Green}/wspd t 200 100{ChatColors.Default}  T方切枪 200%，射速不变");
        Reply(player, "说明: 100 = 原版速度；200 = 2倍速度；50 = 半速。");
        Reply(player, "换弹速度已移除。切枪速度只改服务器端可开火时间，动画可能不同步。");
        Reply(player, "射速加快时，会自动尝试缩放 AimPunch/ViewPunch 后坐力。");
        Reply(player, $"当前状态: {GetStatusInfo()}");
    }

    private void OnTick()
    {
        if (!_settings.Enabled) return;

        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive)
                continue;

            if (!IsTarget(player))
                continue;

            if (player.Slot < 0 || player.Slot >= _states.Length)
                continue;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
                continue;

            var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid)
                continue;

            string weaponName = NormalizeWeaponName(weapon.DesignerName);
            if (string.IsNullOrEmpty(weaponName))
                continue;

            var state = _states[player.Slot];

            ProcessSwitchSpeed(player, weapon, weaponName, state);
            ProcessFireSpeed(weapon, weaponName, state);

            // 记录这一帧的后坐力，给下一次 weapon_fire 当作“开火前基准”
            UpdateObservedRecoil(pawn, state);
        }
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!_settings.Enabled) return HookResult.Continue;

        // 100% 或更低射速时，不做后坐力补偿。
        // 主要解决“射速加快后后坐力过大”的问题。
        if (_settings.FireSpeedPercent <= 100.0f + 0.01f)
            return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || !player.PawnIsAlive)
            return HookResult.Continue;

        if (!IsTarget(player))
            return HookResult.Continue;

        if (player.Slot < 0 || player.Slot >= _states.Length)
            return HookResult.Continue;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return HookResult.Continue;

        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon == null || !weapon.IsValid)
            return HookResult.Continue;

        string weaponName = NormalizeWeaponName(weapon.DesignerName);
        if (!IsGunWeapon(weaponName))
            return HookResult.Continue;

        var state = _states[player.Slot];

        float fireMultiplier = PercentToMultiplier(_settings.FireSpeedPercent);
        if (fireMultiplier <= 1.0f + 0.01f)
            return HookResult.Continue;

        QAngle baseAimPunch = state.HasLastAimPunchAngle
            ? state.LastAimPunchAngle
            : new QAngle(0, 0, 0);

        QAngle baseAimPunchVel = state.HasLastAimPunchVelocity
            ? state.LastAimPunchVelocity
            : new QAngle(0, 0, 0);

        QAngle baseViewPunch = state.HasLastViewPunchAngle
            ? state.LastViewPunchAngle
            : new QAngle(0, 0, 0);

        state.RecoilShotToken++;
        int token = state.RecoilShotToken;

        bool appliedNow = TryApplyRecoilCompensation(
            pawn,
            state,
            token,
            baseAimPunch,
            baseAimPunchVel,
            baseViewPunch,
            fireMultiplier
        );

        // 有些情况下 weapon_fire Post 时后坐力还没完全写完，
        // 所以延迟 0.01 秒再补一次。通过 token 避免重复补偿。
        if (!appliedNow)
        {
            _plugin.AddTimer(0.01f, () =>
            {
                if (!_settings.Enabled) return;
                if (!player.IsValid || !player.PawnIsAlive) return;

                var p = player.PlayerPawn.Value;
                if (p == null || !p.IsValid) return;

                TryApplyRecoilCompensation(
                    p,
                    state,
                    token,
                    baseAimPunch,
                    baseAimPunchVel,
                    baseViewPunch,
                    fireMultiplier
                );
            });
        }

        return HookResult.Continue;
    }

    private void ProcessSwitchSpeed(
        CCSPlayerController player,
        CBasePlayerWeapon weapon,
        string weaponName,
        PlayerWeaponSpeedState state)
    {
        if (state.LastWeaponName == "")
        {
            state.LastWeaponName = weaponName;
            return;
        }

        if (state.LastWeaponName == weaponName)
            return;

        state.LastWeaponName = weaponName;
        state.LastAppliedPrimaryTick = 0;
        state.LastAppliedSecondaryTick = 0;

        if (!IsGunWeapon(weaponName) && !IsKnifeWeapon(weaponName))
            return;

        if (Math.Abs(_settings.SwitchSpeedPercent - 100.0f) < 0.01f)
            return;

        float multiplier = PercentToMultiplier(_settings.SwitchSpeedPercent);

        _plugin.AddTimer(0.01f, () =>
        {
            if (!_settings.Enabled) return;
            if (!player.IsValid || !player.PawnIsAlive) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var active = pawn.WeaponServices?.ActiveWeapon.Value;
            if (active == null || !active.IsValid) return;

            string activeWeaponName = NormalizeWeaponName(active.DesignerName);
            if (activeWeaponName != weaponName)
                return;

            bool includeSecondary = IsKnifeWeapon(activeWeaponName);
            ApplyAttackDelayScaleOnce(active, multiplier, state, force: true, includeSecondary);
        });
    }

    private void ProcessFireSpeed(
        CBasePlayerWeapon weapon,
        string weaponName,
        PlayerWeaponSpeedState state)
    {
        if (!IsGunWeapon(weaponName))
            return;

        if (Math.Abs(_settings.FireSpeedPercent - 100.0f) < 0.01f)
            return;

        float multiplier = PercentToMultiplier(_settings.FireSpeedPercent);

        /*
         * 不依赖 weapon_fire 修改射速。
         * 每 Tick 检查武器的下一次可开火时间。
         * 只要发现游戏刚写入了一段新的开火冷却，就立刻按倍率缩放。
         */
        ApplyAttackDelayScaleOnce(weapon, multiplier, state, force: false, includeSecondary: false);
    }

    private void ApplyAttackDelayScaleOnce(
        CBasePlayerWeapon weapon,
        float multiplier,
        PlayerWeaponSpeedState state,
        bool force,
        bool includeSecondary)
    {
        if (weapon == null || !weapon.IsValid) return;

        int nowTick = Server.TickCount;

        ScalePrimaryAttackTick(weapon, nowTick, multiplier, state, force);
        if (includeSecondary)
            ScaleSecondaryAttackTick(weapon, nowTick, multiplier, state, force);

        NotifyAttackTickChanged(weapon);
    }

    private void ScalePrimaryAttackTick(
        CBasePlayerWeapon weapon,
        int nowTick,
        float multiplier,
        PlayerWeaponSpeedState state,
        bool force)
    {
        int oldNextTick;

        try
        {
            oldNextTick = weapon.NextPrimaryAttackTick;
        }
        catch
        {
            return;
        }

        int remaining = oldNextTick - nowTick;
        if (remaining <= 1) return;
        if (remaining > MaxScalableAttackDelayTicks) return;

        // 不是强制模式，并且这个冷却已经是本插件刚写过的，就不要重复缩放。
        if (!force && oldNextTick == state.LastAppliedPrimaryTick)
            return;

        int newRemaining = Math.Max(1, (int)MathF.Round(remaining / multiplier));
        int newNextTick = nowTick + newRemaining;

        if (newNextTick == oldNextTick) return;

        try
        {
            weapon.NextPrimaryAttackTick = newNextTick;
            state.LastAppliedPrimaryTick = newNextTick;
        }
        catch
        {
            // ignored
        }
    }

    private void ScaleSecondaryAttackTick(
        CBasePlayerWeapon weapon,
        int nowTick,
        float multiplier,
        PlayerWeaponSpeedState state,
        bool force)
    {
        int oldNextTick;

        try
        {
            oldNextTick = weapon.NextSecondaryAttackTick;
        }
        catch
        {
            return;
        }

        int remaining = oldNextTick - nowTick;
        if (remaining <= 1) return;
        if (remaining > MaxScalableAttackDelayTicks) return;

        if (!force && oldNextTick == state.LastAppliedSecondaryTick)
            return;

        int newRemaining = Math.Max(1, (int)MathF.Round(remaining / multiplier));
        int newNextTick = nowTick + newRemaining;

        if (newNextTick == oldNextTick) return;

        try
        {
            weapon.NextSecondaryAttackTick = newNextTick;
            state.LastAppliedSecondaryTick = newNextTick;
        }
        catch
        {
            // ignored
        }
    }

    private bool TryApplyRecoilCompensation(
        CCSPlayerPawn pawn,
        PlayerWeaponSpeedState state,
        int token,
        QAngle baseAimPunch,
        QAngle baseAimPunchVel,
        QAngle baseViewPunch,
        float fireMultiplier)
    {
        if (pawn == null || !pawn.IsValid) return false;

        // 避免同一发子弹重复补偿。
        if (state.LastCompensatedRecoilToken == token)
            return true;

        if (!TryGetAimPunchAngle(pawn, out var currentAimPunch))
            return false;

        QAngle deltaAimPunch = SubAngle(currentAimPunch, baseAimPunch);

        // 当前还没有新增后坐力，说明这个时机太早，等 0.01 秒那次再处理。
        if (AngleMagnitude(deltaAimPunch) < 0.0001f)
            return false;

        // 射速 200% -> 每发后坐力乘 0.5，使单位时间后坐力接近原版。
        float recoilScale = Math.Clamp(1.0f / fireMultiplier, 0.05f, 1.0f);

        QAngle newAimPunch = AddAngle(baseAimPunch, MulAngle(deltaAimPunch, recoilScale));

        bool changed = false;

        if (TrySetAimPunchAngle(pawn, newAimPunch))
        {
            changed = true;
            state.LastAimPunchAngle = newAimPunch;
            state.HasLastAimPunchAngle = true;
        }

        // 后坐力速度也缩一下，否则视角可能会继续被后续弹性恢复逻辑拉动。
        if (TryGetAimPunchVelocity(pawn, out var currentAimPunchVel))
        {
            QAngle deltaVel = SubAngle(currentAimPunchVel, baseAimPunchVel);
            QAngle newVel = AddAngle(baseAimPunchVel, MulAngle(deltaVel, recoilScale));

            if (TrySetAimPunchVelocity(pawn, newVel))
            {
                changed = true;
                state.LastAimPunchVelocity = newVel;
                state.HasLastAimPunchVelocity = true;
            }
        }

        // 兼容部分版本/武器使用 ViewPunch 的情况。
        if (TryGetViewPunchAngle(pawn, out var currentViewPunch))
        {
            QAngle deltaView = SubAngle(currentViewPunch, baseViewPunch);

            if (AngleMagnitude(deltaView) >= 0.0001f)
            {
                QAngle newViewPunch = AddAngle(baseViewPunch, MulAngle(deltaView, recoilScale));

                if (TrySetViewPunchAngle(pawn, newViewPunch))
                {
                    changed = true;
                    state.LastViewPunchAngle = newViewPunch;
                    state.HasLastViewPunchAngle = true;
                }
            }
        }

        if (changed)
        {
            state.LastCompensatedRecoilToken = token;
            NotifyRecoilChanged(pawn);
        }

        return changed;
    }

    private void UpdateObservedRecoil(CCSPlayerPawn pawn, PlayerWeaponSpeedState state)
    {
        if (pawn == null || !pawn.IsValid) return;

        if (TryGetAimPunchAngle(pawn, out var aimPunch))
        {
            state.LastAimPunchAngle = aimPunch;
            state.HasLastAimPunchAngle = true;
        }

        if (TryGetAimPunchVelocity(pawn, out var aimPunchVel))
        {
            state.LastAimPunchVelocity = aimPunchVel;
            state.HasLastAimPunchVelocity = true;
        }

        if (TryGetViewPunchAngle(pawn, out var viewPunch))
        {
            state.LastViewPunchAngle = viewPunch;
            state.HasLastViewPunchAngle = true;
        }
    }

    private static float PercentToMultiplier(float percent)
    {
        return Math.Clamp(percent / 100.0f, 0.10f, 5.00f);
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all") return true;

        if (_settings.Target == "t" && player.TeamNum == (byte)CsTeam.Terrorist)
            return true;

        if (_settings.Target == "ct" && player.TeamNum == (byte)CsTeam.CounterTerrorist)
            return true;

        return false;
    }

    private static string NormalizeWeaponName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        name = name.Trim().ToLowerInvariant();

        if (name.StartsWith("weapon_"))
            name = name.Substring("weapon_".Length);

        return name;
    }

    private static bool IsKnifeWeapon(string weapon)
    {
        if (string.IsNullOrEmpty(weapon)) return false;

        return weapon.Contains("knife") ||
               weapon.Contains("bayonet");
    }

    private static bool IsGunWeapon(string weapon)
    {
        if (string.IsNullOrEmpty(weapon)) return false;

        if (IsKnifeWeapon(weapon)) return false;

        if (weapon.Contains("grenade") ||
            weapon.Contains("flashbang") ||
            weapon.Contains("molotov") ||
            weapon.Contains("incgrenade") ||
            weapon.Contains("smokegrenade") ||
            weapon.Contains("decoy") ||
            weapon.Contains("c4") ||
            weapon.Contains("taser"))
            return false;

        return GetMaxClip(weapon) > 0;
    }

    private static int GetMaxClip(string weapon)
    {
        return weapon switch
        {
            "glock" => 20,
            "hkp2000" => 13,
            "usp_silencer" => 12,
            "p250" => 13,
            "fiveseven" => 20,
            "tec9" => 18,
            "cz75a" => 12,
            "deagle" => 7,
            "revolver" => 8,
            "elite" => 30,

            "mac10" => 30,
            "mp9" => 30,
            "mp7" => 30,
            "mp5sd" => 30,
            "ump45" => 25,
            "p90" => 50,
            "bizon" => 64,

            "ak47" => 30,
            "m4a1" => 30,
            "m4a1_silencer" => 20,
            "galilar" => 35,
            "famas" => 25,
            "aug" => 30,
            "sg556" => 30,

            "ssg08" => 10,
            "awp" => 5,
            "scar20" => 20,
            "g3sg1" => 20,

            "nova" => 8,
            "xm1014" => 7,
            "mag7" => 5,
            "sawedoff" => 7,
            "m249" => 100,
            "negev" => 150,

            _ => 0
        };
    }

    private static void NotifyAttackTickChanged(CBasePlayerWeapon weapon)
    {
        if (weapon == null || !weapon.IsValid) return;

        try { Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick"); } catch { }
        try { Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick"); } catch { }
        try { Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_flNextPrimaryAttackTickRatio"); } catch { }
        try { Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_flNextSecondaryAttackTickRatio"); } catch { }
    }

    private static QAngle AddAngle(QAngle a, QAngle b)
    {
        return new QAngle(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    private static QAngle SubAngle(QAngle a, QAngle b)
    {
        return new QAngle(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    private static QAngle MulAngle(QAngle a, float m)
    {
        return new QAngle(a.X * m, a.Y * m, a.Z * m);
    }

    private static float AngleMagnitude(QAngle a)
    {
        return MathF.Abs(a.X) + MathF.Abs(a.Y) + MathF.Abs(a.Z);
    }

    private static bool TryGetAimPunchAngle(CCSPlayerPawn pawn, out QAngle angle)
    {
        return TryGetPawnAngle(pawn, "AimPunchAngle", out angle);
    }

    private static bool TrySetAimPunchAngle(CCSPlayerPawn pawn, QAngle angle)
    {
        return TrySetPawnAngle(pawn, "AimPunchAngle", angle);
    }

    private static bool TryGetAimPunchVelocity(CCSPlayerPawn pawn, out QAngle angle)
    {
        return TryGetPawnAngle(pawn, "AimPunchAngleVel", out angle) ||
               TryGetPawnAngle(pawn, "AimPunchVelocity", out angle);
    }

    private static bool TrySetAimPunchVelocity(CCSPlayerPawn pawn, QAngle angle)
    {
        return TrySetPawnAngle(pawn, "AimPunchAngleVel", angle) ||
               TrySetPawnAngle(pawn, "AimPunchVelocity", angle);
    }

    private static bool TryGetViewPunchAngle(CCSPlayerPawn pawn, out QAngle angle)
    {
        return TryGetPawnAngle(pawn, "ViewPunchAngle", out angle);
    }

    private static bool TrySetViewPunchAngle(CCSPlayerPawn pawn, QAngle angle)
    {
        return TrySetPawnAngle(pawn, "ViewPunchAngle", angle);
    }

    private static bool TryGetPawnAngle(CCSPlayerPawn pawn, string propertyName, out QAngle angle)
    {
        angle = new QAngle(0, 0, 0);

        if (pawn == null || !pawn.IsValid)
            return false;

        try
        {
            dynamic dynPawn = pawn;

            object value = propertyName switch
            {
                "AimPunchAngle" => dynPawn.AimPunchAngle,
                "AimPunchAngleVel" => dynPawn.AimPunchAngleVel,
                "AimPunchVelocity" => dynPawn.AimPunchVelocity,
                "ViewPunchAngle" => dynPawn.ViewPunchAngle,
                _ => throw new InvalidOperationException()
            };

            if (value is QAngle q)
            {
                angle = q;
                return true;
            }
        }
        catch
        {
            // 当前 CounterStrikeSharp 版本没有这个属性时，直接忽略。
        }

        return false;
    }

    private static bool TrySetPawnAngle(CCSPlayerPawn pawn, string propertyName, QAngle angle)
    {
        if (pawn == null || !pawn.IsValid)
            return false;

        try
        {
            dynamic dynPawn = pawn;

            switch (propertyName)
            {
                case "AimPunchAngle":
                    dynPawn.AimPunchAngle = angle;
                    return true;

                case "AimPunchAngleVel":
                    dynPawn.AimPunchAngleVel = angle;
                    return true;

                case "AimPunchVelocity":
                    dynPawn.AimPunchVelocity = angle;
                    return true;

                case "ViewPunchAngle":
                    dynPawn.ViewPunchAngle = angle;
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void NotifyRecoilChanged(CCSPlayerPawn pawn)
    {
        if (pawn == null || !pawn.IsValid) return;

        // 不同 CSS 版本/Schema 名可能不完全一致，所以多试几个，不成功就忽略。
        try { Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_aimPunchAngle"); } catch { }
        try { Utilities.SetStateChanged(pawn, "C_CSPlayerPawn", "m_aimPunchAngle"); } catch { }
        try { Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_aimPunchAngle"); } catch { }

        try { Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_aimPunchAngleVel"); } catch { }
        try { Utilities.SetStateChanged(pawn, "C_CSPlayerPawn", "m_aimPunchAngleVel"); } catch { }
        try { Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_aimPunchAngleVel"); } catch { }

        try { Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_viewPunchAngle"); } catch { }
        try { Utilities.SetStateChanged(pawn, "C_CSPlayerPawn", "m_viewPunchAngle"); } catch { }
        try { Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_viewPunchAngle"); } catch { }
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
        return $" {ChatColors.Green}/wspd{ChatColors.Default} : 控制切枪速度与射击速度";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled)
            return $"WeaponSpeed: {ChatColors.Red}已禁用{ChatColors.Default}";

        return $"WeaponSpeed: {ChatColors.Green}已启用{ChatColors.Default} | " +
               $"目标:{_settings.Target.ToUpper()} | " +
               $"切枪:{_settings.SwitchSpeedPercent:F0}% | " +
               $"射击:{_settings.FireSpeedPercent:F0}%";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled) return null;

        return $"[武器速度] {GetTargetDisplayName(_settings.Target)} " +
               $"切枪 {_settings.SwitchSpeedPercent:F0}% / " +
               $"射击 {_settings.FireSpeedPercent:F0}%";
    }

    public string GetFeatureDescription()
    {
        return "【武器速度控制】控制指定阵营的切枪速度和射击速度。\n" +
               "指令: /wspd <t/ct/all/0> <switchSpeed> <fireSpeed>\n" +
               "100 表示原版速度；200 表示 2 倍速度；50 表示半速。\n" +
               "换弹速度已移除。\n" +
               "切枪速度通过修改切枪后的可攻击时间实现，动画可能不同步。\n" +
               "射击速度通过持续监控并缩放武器下一次可开火时间实现。\n" +
               "射速加快时，会自动尝试按射速倍率缩放 AimPunch/ViewPunch 后坐力。";
    }
}
