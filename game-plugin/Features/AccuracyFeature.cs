using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Features;

/// <summary>
/// /acc <t/ct/all/0> <移动惩罚倍数> <后坐力倍数>
/// 武器精准与后坐力控制。
///
/// 说明：
/// 1. 这是高风险 Schema 修改模块；
/// 2. 主要修改 CCSWeaponBase:
///    - m_fAccuracyPenalty
///    - m_iRecoilIndex
///    - m_flRecoilIndex
/// 3. CS2 内部移动散布/后坐力计算不属于稳定公开 API，效果需要实测。
/// </summary>
public class AccuracyFeature : ICaorenFeature
{
    public string FeatureName => "武器精准";

    private CaorenCupPlugin _plugin = null!;
    private bool _registered;

    private const string WeaponSchemaClass = "CCSWeaponBase";
    private const float Epsilon = 0.0001f;

    private readonly AccRuntimeSettings _settings = new();

    // Schema 偏移缓存：key = class:field
    private readonly Dictionary<string, int?> _schemaOffsetCache = new();

    // 写入缓存，避免每 Tick 对已经写过的值重复乘法，导致倍率叠加。
    private readonly Dictionary<string, FloatWriteCache> _floatWriteCache = new();
    private readonly Dictionary<string, IntWriteCache> _intWriteCache = new();

    private static readonly HashSet<string> IgnoredWeaponNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapon_knife",
        "weapon_knife_t",
        "weapon_knife_gg",
        "weapon_c4",
        "weapon_flashbang",
        "weapon_hegrenade",
        "weapon_smokegrenade",
        "weapon_molotov",
        "weapon_incgrenade",
        "weapon_decoy",
        "weapon_healthshot",
        "weapon_taser"
    };

    private sealed class AccRuntimeSettings
    {
        public bool Enabled { get; set; } = false;
        public string Target { get; set; } = "all";
        public float MovePenaltyMultiplier { get; set; } = 1.0f;
        public float RecoilMultiplier { get; set; } = 1.0f;
    }

    private sealed class FloatWriteCache
    {
        public float Raw { get; set; }
        public float Written { get; set; }
    }

    private sealed class IntWriteCache
    {
        public int Raw { get; set; }
        public int Written { get; set; }
    }

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        plugin.AddCommand("css_acc", "更改指定方的武器精准程度与后坐力", OnAccCommand);
        plugin.AddCommand("acc", "更改指定方的武器精准程度与后坐力", OnAccCommand);

        plugin.RegisterListener<Listeners.OnTick>(OnTick);
        plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);

        _registered = true;
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        // 独立运行版，不读取持久化配置。
    }

    public void OnUnload()
    {
        try
        {
            RestoreAllActiveWeapons();
        }
        catch
        {
            // 卸载阶段避免抛异常影响主插件卸载。
        }

        if (_registered)
        {
            try
            {
                _plugin.RemoveListener<Listeners.OnTick>(OnTick);
            }
            catch
            {
                // 忽略重复卸载或框架卸载阶段异常。
            }

            try
            {
                _plugin.DeregisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
            }
            catch
            {
                // 忽略重复卸载或框架卸载阶段异常。
            }

            _registered = false;
        }

        _settings.Enabled = false;
        ClearRuntimeCaches();
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            try
            {
                RestoreAllActiveWeapons();
            }
            catch
            {
                // 忽略关闭时恢复失败。
            }

            _settings.Enabled = false;
            ClearRuntimeCaches();
            return;
        }

        _settings.Enabled = true;
    }

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/acc <t/ct/all/0> <移动惩罚倍数> <后坐力倍数>{ChatColors.Default} : 调整武器精准惩罚与后坐力";
    }

    public string GetStatusInfo()
    {
        if (!_settings.Enabled)
            return $"{ChatColors.LightRed}武器精准: 关闭{ChatColors.Default}";

        return
            $"{ChatColors.Green}武器精准: 开启{ChatColors.Default} " +
            $"目标:{ChatColors.Green}{_settings.Target}{ChatColors.Default} " +
            $"移动惩罚:{ChatColors.Green}x{_settings.MovePenaltyMultiplier:0.###}{ChatColors.Default} " +
            $"后坐力:{ChatColors.Green}x{_settings.RecoilMultiplier:0.###}{ChatColors.Default}";
    }

    public string? GetPublicConfigInfo()
    {
        if (!_settings.Enabled)
            return null;

        return
            $"武器精准控制：目标 {_settings.Target}，" +
            $"移动惩罚 x{_settings.MovePenaltyMultiplier:0.###}，" +
            $"后坐力 x{_settings.RecoilMultiplier:0.###}";
    }

    public string GetFeatureDescription()
    {
        return
            "武器精准模块 /acc\n" +
            "用法：/acc <t/ct/all/0> <移动惩罚倍数> <后坐力倍数>\n" +
            "示例：/acc all 0 0 表示尽量让所有玩家无移动精准惩罚、无后坐力。\n" +
            "移动惩罚倍数：1 为原版，2 为放大，0.5 为减半，0 为尽量清除当前精准惩罚。\n" +
            "后坐力倍数：1 为原版，0.5 为减半，0 为尽量清除后坐力索引。\n" +
            "注意：本模块通过武器 Schema 字段实现，属于高风险区，不同 CounterStrikeSharp / CS2 版本可能需要实测。";
    }

    private void OnAccCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
            return;
        }

        if (command.ArgCount < 2)
        {
            Reply(player,
                $"{ChatColors.Yellow}用法:{ChatColors.Default} /acc <t/ct/all/0> <移动惩罚倍数> <后坐力倍数>");
            return;
        }

        string target = command.GetArg(1).Trim().ToLowerInvariant();

        if (IsOffArg(target))
        {
            SetEnabled(false);
            Reply(player, $"{ChatColors.LightRed}武器精准模块已关闭。{ChatColors.Default}");
            return;
        }

        if (!IsValidTargetArg(target))
        {
            Reply(player,
                $"{ChatColors.LightRed}目标无效。{ChatColors.Default} 可用: t / ct / all / 0");
            return;
        }

        if (command.ArgCount < 4)
        {
            Reply(player,
                $"{ChatColors.Yellow}用法:{ChatColors.Default} /acc <t/ct/all/0> <移动惩罚倍数> <后坐力倍数>");
            return;
        }

        if (!TryParseNonNegativeFloat(command.GetArg(2), out float movePenaltyMultiplier))
        {
            Reply(player,
                $"{ChatColors.LightRed}移动惩罚倍数无效。{ChatColors.Default} 请输入 >= 0 的数字，例如 0 / 0.5 / 1 / 2");
            return;
        }

        if (!TryParseNonNegativeFloat(command.GetArg(3), out float recoilMultiplier))
        {
            Reply(player,
                $"{ChatColors.LightRed}后坐力倍数无效。{ChatColors.Default} 请输入 >= 0 的数字，例如 0 / 0.5 / 1 / 2");
            return;
        }

        // 切换配置前，尽量把当前活跃武器恢复到模块写入前的数值，避免旧倍率残留。
        try
        {
            RestoreAllActiveWeapons();
        }
        catch
        {
            // 不阻断新配置生效。
        }

        ClearRuntimeCaches();

        _settings.Enabled = true;
        _settings.Target = target;
        _settings.MovePenaltyMultiplier = movePenaltyMultiplier;
        _settings.RecoilMultiplier = recoilMultiplier;

        CaorenCupUtils.PrintToChatAll(
            $"{ChatColors.Green}武器精准模块已启用！{ChatColors.Default} " +
            $"目标:{ChatColors.Green}{target}{ChatColors.Default} " +
            $"移动惩罚:{ChatColors.Green}x{movePenaltyMultiplier:0.###}{ChatColors.Default} " +
            $"后坐力:{ChatColors.Green}x{recoilMultiplier:0.###}{ChatColors.Default}"
        );
    }

    private void OnTick()
    {
        if (!_settings.Enabled)
            return;

        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                ApplyToPlayer(player);
            }
        }
        catch
        {
            // 避免某个实体瞬间无效导致整 Tick 报错刷屏。
        }
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!_settings.Enabled)
            return HookResult.Continue;

        var player = @event.Userid;
        if (player == null)
            return HookResult.Continue;

        try
        {
            ApplyToPlayer(player);
        }
        catch
        {
            // 避免开火事件中实体瞬间无效导致报错。
        }

        return HookResult.Continue;
    }

    private void ApplyToPlayer(CCSPlayerController player)
    {
        if (!_settings.Enabled)
            return;

        if (!IsUsablePlayer(player))
            return;

        if (!IsTarget(player))
            return;

        var weapon = GetActiveWeapon(player);
        if (weapon == null || !weapon.IsValid)
            return;

        if (!IsAffectableWeapon(weapon))
            return;

        if (!NearlyEqual(_settings.MovePenaltyMultiplier, 1.0f))
        {
            ApplyFloatMultiplier(
                weapon,
                WeaponSchemaClass,
                "m_fAccuracyPenalty",
                _settings.MovePenaltyMultiplier
            );
        }

        if (!NearlyEqual(_settings.RecoilMultiplier, 1.0f))
        {
            ApplyFloatMultiplier(
                weapon,
                WeaponSchemaClass,
                "m_flRecoilIndex",
                _settings.RecoilMultiplier
            );

            ApplyIntMultiplier(
                weapon,
                WeaponSchemaClass,
                "m_iRecoilIndex",
                _settings.RecoilMultiplier
            );
        }
    }

    private CBasePlayerWeapon? GetActiveWeapon(CCSPlayerController player)
    {
        if (!IsUsablePlayer(player))
            return null;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return null;

        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return null;

        return weaponServices.ActiveWeapon.Value;
    }

    private bool IsUsablePlayer(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return false;

        if (player.Connected != PlayerConnectedState.Connected)
            return false;

        if (!player.PawnIsAlive)
            return false;

        var pawn = player.PlayerPawn.Value;
        return pawn != null && pawn.IsValid;
    }

    private bool IsTarget(CCSPlayerController player)
    {
        if (_settings.Target == "all")
            return true;

        if (_settings.Target == "t" && player.TeamNum == (byte)CsTeam.Terrorist)
            return true;

        if (_settings.Target == "ct" && player.TeamNum == (byte)CsTeam.CounterTerrorist)
            return true;

        return false;
    }

    private static bool IsValidTargetArg(string target)
    {
        return target is "t" or "ct" or "all";
    }

    private static bool IsOffArg(string arg)
    {
        return arg is "0" or "off" or "disable" or "disabled" or "false";
    }

    private static bool TryParseNonNegativeFloat(string text, out float value)
    {
        value = 0f;

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            return false;

        if (float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < 0f)
            return false;

        value = parsed;
        return true;
    }

    private static bool IsAffectableWeapon(CBasePlayerWeapon weapon)
    {
        string designerName = weapon.DesignerName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(designerName))
            return true;

        if (!designerName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            return false;

        return !IgnoredWeaponNames.Contains(designerName);
    }

    private void ApplyFloatMultiplier(CEntityInstance entity, string className, string fieldName, float multiplier)
    {
        if (!TryReadFloat(entity, className, fieldName, out float current))
            return;

        string key = GetEntityFieldKey(entity, className, fieldName);

        if (_floatWriteCache.TryGetValue(key, out var cache) && NearlyEqual(current, cache.Written))
            return;

        float written = Math.Max(0f, current * multiplier);
        if (Math.Abs(written) < Epsilon)
            written = 0f;

        if (NearlyEqual(current, written))
        {
            _floatWriteCache[key] = new FloatWriteCache
            {
                Raw = current,
                Written = written
            };
            return;
        }

        if (TryWriteFloat(entity, className, fieldName, written))
        {
            _floatWriteCache[key] = new FloatWriteCache
            {
                Raw = current,
                Written = written
            };
        }
    }

    private void ApplyIntMultiplier(CEntityInstance entity, string className, string fieldName, float multiplier)
    {
        if (!TryReadInt(entity, className, fieldName, out int current))
            return;

        string key = GetEntityFieldKey(entity, className, fieldName);

        if (_intWriteCache.TryGetValue(key, out var cache) && current == cache.Written)
            return;

        int written = Math.Max(0, (int)MathF.Round(current * multiplier));

        if (current == written)
        {
            _intWriteCache[key] = new IntWriteCache
            {
                Raw = current,
                Written = written
            };
            return;
        }

        if (TryWriteInt(entity, className, fieldName, written))
        {
            _intWriteCache[key] = new IntWriteCache
            {
                Raw = current,
                Written = written
            };
        }
    }

    private void RestoreAllActiveWeapons()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            var weapon = GetActiveWeapon(player);
            if (weapon == null || !weapon.IsValid)
                continue;

            RestoreFloatIfCached(weapon, WeaponSchemaClass, "m_fAccuracyPenalty");
            RestoreFloatIfCached(weapon, WeaponSchemaClass, "m_flRecoilIndex");
            RestoreIntIfCached(weapon, WeaponSchemaClass, "m_iRecoilIndex");
        }

        ClearRuntimeCaches();
    }

    private void RestoreFloatIfCached(CEntityInstance entity, string className, string fieldName)
    {
        string key = GetEntityFieldKey(entity, className, fieldName);

        if (!_floatWriteCache.TryGetValue(key, out var cache))
            return;

        if (!TryReadFloat(entity, className, fieldName, out float current))
            return;

        // 只有当前值仍是模块写入值时才恢复，避免覆盖游戏或其他模块刚刚更新的新值。
        if (NearlyEqual(current, cache.Written))
        {
            TryWriteFloat(entity, className, fieldName, cache.Raw);
        }
    }

    private void RestoreIntIfCached(CEntityInstance entity, string className, string fieldName)
    {
        string key = GetEntityFieldKey(entity, className, fieldName);

        if (!_intWriteCache.TryGetValue(key, out var cache))
            return;

        if (!TryReadInt(entity, className, fieldName, out int current))
            return;

        if (current == cache.Written)
        {
            TryWriteInt(entity, className, fieldName, cache.Raw);
        }
    }

    private bool TryReadFloat(CEntityInstance entity, string className, string fieldName, out float value)
    {
        value = 0f;

        if (!TryGetSchemaOffset(className, fieldName, out int offset))
            return false;

        try
        {
            IntPtr ptr = IntPtr.Add(entity.Handle, offset);
            value = Marshal.PtrToStructure<float>(ptr);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryWriteFloat(CEntityInstance entity, string className, string fieldName, float value)
    {
        if (!TryGetSchemaOffset(className, fieldName, out int offset))
            return false;

        try
        {
            IntPtr ptr = IntPtr.Add(entity.Handle, offset);
            Marshal.StructureToPtr(value, ptr, false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadInt(CEntityInstance entity, string className, string fieldName, out int value)
    {
        value = 0;

        if (!TryGetSchemaOffset(className, fieldName, out int offset))
            return false;

        try
        {
            IntPtr ptr = IntPtr.Add(entity.Handle, offset);
            value = Marshal.ReadInt32(ptr);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryWriteInt(CEntityInstance entity, string className, string fieldName, int value)
    {
        if (!TryGetSchemaOffset(className, fieldName, out int offset))
            return false;

        try
        {
            IntPtr ptr = IntPtr.Add(entity.Handle, offset);
            Marshal.WriteInt32(ptr, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetSchemaOffset(string className, string fieldName, out int offset)
    {
        offset = 0;
        string key = $"{className}:{fieldName}";

        if (_schemaOffsetCache.TryGetValue(key, out int? cached))
        {
            if (!cached.HasValue)
                return false;

            offset = cached.Value;
            return true;
        }

        try
        {
            offset = Schema.GetSchemaOffset(className, fieldName);
            _schemaOffsetCache[key] = offset;
            return true;
        }
        catch
        {
            _schemaOffsetCache[key] = null;
            return false;
        }
    }

    private static string GetEntityFieldKey(CEntityInstance entity, string className, string fieldName)
    {
        return $"{entity.Handle.ToInt64()}:{className}:{fieldName}";
    }

    private static bool NearlyEqual(float a, float b)
    {
        return Math.Abs(a - b) <= Epsilon;
    }

    private void ClearRuntimeCaches()
    {
        _floatWriteCache.Clear();
        _intWriteCache.Clear();
    }

    private static void Reply(CCSPlayerController? player, string message)
    {
        if (player != null)
        {
            CaorenCupUtils.PrintToChat(player, message);
            return;
        }

        Console.WriteLine(message);
    }
}