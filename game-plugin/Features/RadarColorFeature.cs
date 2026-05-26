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
    public string FeatureName => "Radar Color Fix (???????)";

    private const string SchemaClass = "CCSPlayerController";
    private const string ColorField = "m_iCompTeammateColor";
    private static readonly int[] ColorOrder = { 0, 1, 2, 3, 4 };

    private readonly Dictionary<string, int?> _schemaOffsetCache = new();
    private bool _warnedSchemaUnavailable;

    public void Init(CaorenCupPlugin plugin)
    {
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        plugin.AddCommand("css_radarcolor", "?????/???????: css_radarcolor <status/apply>", OnCommand);

        plugin.AddTimer(1.0f, ApplyAllRadarColors);
    }

    public void OnConfigParsed(CaorenCupConfig config)
    {
    }

    public void OnUnload()
    {
    }

    public void SetEnabled(bool enabled)
    {
        ApplyAllRadarColors();
    }

    private void OnCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            CaorenCupUtils.PrintToChat(player, "???????????");
            return;
        }

        string action = info.ArgCount >= 2 ? info.GetArg(1).Trim().ToLowerInvariant() : "status";
        switch (action)
        {
            case "0":
            case "off":
            case "disable":
                ApplyAllRadarColors();
                Reply(player, "???/????????????????????????");
                break;
            case "1":
            case "on":
            case "enable":
                ApplyAllRadarColors();
                Reply(player, "???/????????????????????");
                break;
            case "apply":
            case "fix":
                ApplyAllRadarColors();
                Reply(player, "?????????????/??????");
                break;
            case "status":
                Reply(player, "???/??????????????????????");
                break;
            default:
                Reply(player, "???/radarcolor status | apply");
                break;
        }
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ApplyAllRadarColors();

        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        ApplyAllRadarColors();

        return HookResult.Continue;
    }

    private void ApplyAllRadarColors()
    {
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
        return $" {ChatColors.Green}/radarcolor{ChatColors.Default} : ???????/?????????";
    }

    public string GetStatusInfo()
    {
        return $"RadarColor: {ChatColors.Green}????{ChatColors.Default} | ???? 0/1/2/3/4 ??";
    }

    public string? GetPublicConfigInfo()
    {
        return "[?????] ????????????";
    }

    public string GetFeatureDescription()
    {
        return " [RadarColor] ???/????????????\n" +
               " ????????????????? CT/T ?????? CS2 ?????????????????";
    }
}
