using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;

namespace CaorenCup.Features;

public class RadarColorFeature : ICaorenFeature
{
    public string FeatureName => "Radar Color Fix (小地图颜色修复)";

    private const string SchemaClass = "CCSPlayerController";
    private const string ColorField = "m_iCompTeammateColor";
    private static readonly int[] ColorOrder = { 0, 1, 2, 3, 4 };

    private RadarColorSettings _settings = null!;
    private CaorenCupPlugin _plugin = null!;
    private readonly Dictionary<string, int?> _schemaOffsetCache = new();
    private bool _warnedSchemaUnavailable;

    public void Init(CaorenCupPlugin plugin)
    {
        _plugin = plugin;

        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        plugin.AddCommand("css_radarcolor", "修复小地图/头像框队友颜色: css_radarcolor <status/apply/1/0>", OnCommand);

        plugin.AddTimer(1.0f, ApplyAllRadarColors);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
        _settings = config.RadarColor;
    }

    public void OnUnload()
    {
    }

    public void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        if (enabled)
            ApplyAllRadarColors();
    }

    private void OnCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "你没有权限使用此指令。");
            return;
        }

        string action = info.ArgCount >= 2 ? info.GetArg(1).Trim().ToLowerInvariant() : "status";
        switch (action)
        {
            case "1":
            case "on":
            case "enable":
                _settings.Enabled = true;
                ApplyAllRadarColors();
                _plugin.SaveConfig();
                Reply(player, "小地图/头像框颜色修复已启用，并已重新分配颜色。");
                break;
            case "0":
            case "off":
            case "disable":
                _settings.Enabled = false;
                _plugin.SaveConfig();
                Reply(player, "小地图/头像框颜色修复已禁用。");
                break;
            case "apply":
            case "fix":
                ApplyAllRadarColors();
                Reply(player, "已重新分配当前玩家的小地图/头像框颜色。");
                break;
            case "status":
                Reply(player, _settings.Enabled ? "小地图/头像框颜色修复：已启用。" : "小地图/头像框颜色修复：已禁用。");
                break;
            default:
                Reply(player, "用法：/radarcolor status | apply | 1 | 0");
                break;
        }
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_settings.Enabled && _settings.ApplyOnRoundStart)
            ApplyAllRadarColors();

        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (_settings.Enabled && _settings.ApplyOnSpawn)
            ApplyAllRadarColors();

        return HookResult.Continue;
    }

    private void ApplyAllRadarColors()
    {
        if (!_settings.Enabled) return;

        ApplyTeamColors((byte)CsTeam.CounterTerrorist);
        ApplyTeamColors((byte)CsTeam.Terrorist);
    }

    private void ApplyTeamColors(byte teamNum)
    {
        var players = Utilities.GetPlayers()
            .Where(IsValidGamePlayer)
            .Where(player => player.TeamNum == teamNum)
            .OrderBy(player => player.Slot)
            .ThenBy(player => player.Index)
            .ToList();

        for (int i = 0; i < players.Count; i++)
        {
            int color = ColorOrder[i % ColorOrder.Length];
            TrySetRadarColor(players[i], color);
        }
    }

    private static bool IsValidGamePlayer(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV
            && player.TeamNum >= (byte)CsTeam.Terrorist;
    }

    private bool TrySetRadarColor(CCSPlayerController player, int color)
    {
        if (!TryWriteInt(player, SchemaClass, ColorField, color))
        {
            WarnSchemaUnavailable();
            return false;
        }

        try
        {
            Utilities.SetStateChanged(player, SchemaClass, ColorField);
        }
        catch
        {
        }

        return true;
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

    private void WarnSchemaUnavailable()
    {
        if (_warnedSchemaUnavailable) return;
        _warnedSchemaUnavailable = true;
        Console.WriteLine($"[CaorenCup] RadarColor: schema field {SchemaClass}.{ColorField} is unavailable; teammate colors were not changed.");
    }

    private void Reply(CCSPlayerController? player, string message)
    {
        if (player != null && player.IsValid)
            CaorenCupUtils.PrintToChat(player, message);
        else
            Console.WriteLine($"[CaorenCup] RadarColor: {message}");
    }

    public string GetHelpEntry()
    {
        return $" {ChatColors.Green}/radarcolor{ChatColors.Default} : 自动修复小地图/头像框队友颜色重复";
    }

    public string GetStatusInfo()
    {
        return _settings.Enabled
            ? $"RadarColor: {ChatColors.Green}已启用{ChatColors.Default} | 同阵营按 0/1/2/3/4 分配"
            : $"RadarColor: {ChatColors.Red}已禁用{ChatColors.Default}";
    }

    public string? GetPublicConfigInfo()
    {
        return _settings.Enabled ? "[小地图颜色] 已启用同阵营颜色自动分配" : null;
    }

    public string GetFeatureDescription()
    {
        return " [RadarColor] 小地图/头像框队友颜色修复模块。\n" +
               " 服务器会在玩家出生和回合开始时，按 CT/T 阵营分别分配 CS2 原生队友颜色，尽量避免同阵营撞色。";
    }
}
