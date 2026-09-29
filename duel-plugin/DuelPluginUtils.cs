using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenDuel;

/// <summary>
/// 从桥接插件复制的小工具（单挑插件内独立副本，不跨插件共享）。
/// </summary>
internal static class DuelPluginUtils
{
    internal static bool ShouldProcessPluginContinuation(bool isUnloading) => !isUnloading;

    internal static bool IsEnemySideKill(string? attackerTeam, string? victimTeam) =>
        (attackerTeam == "CT" || attackerTeam == "T") &&
        (victimTeam == "CT" || victimTeam == "T") &&
        attackerTeam != victimTeam;

    internal static bool IsSamePlayableSide(string? attackerTeam, string? victimTeam) =>
        (attackerTeam == "CT" || attackerTeam == "T") &&
        attackerTeam == victimTeam;

    internal static string SafePlayerName(CCSPlayerController? player)
    {
        if (player == null) return string.Empty;
        try
        {
            return player.PlayerName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static bool IsRealPlayer(CCSPlayerController? player)
    {
        try
        {
            return player is { IsValid: true, IsBot: false, IsHLTV: false };
        }
        catch
        {
            return false;
        }
    }

    internal static string? TeamName(int teamNum)
    {
        return (CsTeam)teamNum switch
        {
            CsTeam.CounterTerrorist => "CT",
            CsTeam.Terrorist => "T",
            _ => null
        };
    }

    internal static string TeamLabel(CsTeam team)
    {
        return team == CsTeam.CounterTerrorist ? "CT" : team == CsTeam.Terrorist ? "T" : team.ToString();
    }

    internal static string SafeMapName()
    {
        try { return Server.MapName ?? string.Empty; }
        catch { return string.Empty; }
    }

    internal static CCSPlayerController? ReadControllerProperty(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value is CCSPlayerController controller) return controller;
            }
            catch { }
        }
        return null;
    }

    internal static float ReadFloatProperty(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value == null) continue;
                return Convert.ToSingle(value);
            }
            catch { }
        }
        return 0f;
    }

    internal static string ReadStringProperty(object source, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = source.GetType().GetProperty(name)?.GetValue(source);
                if (value != null) return value.ToString() ?? string.Empty;
            }
            catch { }
        }
        return string.Empty;
    }

    internal static bool IsKnifeWeapon(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName)) return false;
        var weapon = weaponName.ToLowerInvariant();
        return weapon.Contains("knife") || weapon.Contains("bayonet");
    }

    internal static bool IsGrenadeWeapon(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName)) return false;
        var weapon = weaponName.ToLowerInvariant();
        return weapon.Contains("flashbang")
            || weapon.Contains("hegrenade")
            || weapon.Contains("smokegrenade")
            || weapon.Contains("molotov")
            || weapon.Contains("incgrenade")
            || weapon.Contains("decoy");
    }

    internal static int CountGrenades(IEnumerable<string> weapons)
    {
        var count = 0;
        foreach (var weapon in weapons)
        {
            if (IsGrenadeWeapon(weapon)) count++;
        }
        return count;
    }

    internal static PlayerEquipmentSnapshot PlayerEquipment(CCSPlayerController? player)
    {
        var pawn = player?.PlayerPawn?.Value;
        var weaponServices = pawn?.WeaponServices;
        var activeWeapon = weaponServices?.ActiveWeapon.Value;
        var hasHelmet = false;
        try
        {
            dynamic dynPawn = pawn!;
            hasHelmet = dynPawn.HasHelmet;
        }
        catch
        {
            hasHelmet = false;
        }
        var weapons = Array.Empty<string>();
        try
        {
            weapons = weaponServices?.MyWeapons
                .Select(handle => handle.Value?.DesignerName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray() ?? Array.Empty<string>();
        }
        catch
        {
            weapons = activeWeapon?.DesignerName is { Length: > 0 } name ? new[] { name } : Array.Empty<string>();
        }

        return new PlayerEquipmentSnapshot
        {
            ActiveWeapon = activeWeapon?.DesignerName ?? string.Empty,
            Weapons = weapons,
            GrenadeCount = CountGrenades(weapons),
            ActiveWeaponIsKnife = IsKnifeWeapon(activeWeapon?.DesignerName),
            Armor = pawn?.ArmorValue ?? 0,
            HasHelmet = hasHelmet
        };
    }

    internal static string StripChatTag(string message) =>
        message.Replace("[草人杯 Notice]", "Notice", StringComparison.Ordinal)
            .Replace("[草人杯]", string.Empty, StringComparison.Ordinal)
            .Trim();

    internal static string FormatPrivateChatMessage(string message) =>
        $" {ChatColors.Green}[草人杯]{ChatColors.Default} {StripChatTag(message)}";

    internal static string FormatGlobalChatMessage(string message) =>
        $" {ChatColors.Red}[草人杯]{ChatColors.Default} {StripChatTag(message)}";

    internal static void PrintToAllPlayers(string message) =>
        Server.PrintToChatAll(FormatGlobalChatMessage(message));
}

internal sealed class PlayerEquipmentSnapshot
{
    [JsonPropertyName("activeWeapon")]
    public string ActiveWeapon { get; set; } = string.Empty;

    [JsonPropertyName("weapons")]
    public string[] Weapons { get; set; } = Array.Empty<string>();

    [JsonPropertyName("grenadeCount")]
    public int GrenadeCount { get; set; }

    [JsonPropertyName("activeWeaponIsKnife")]
    public bool ActiveWeaponIsKnife { get; set; }

    [JsonPropertyName("armor")]
    public int Armor { get; set; }

    [JsonPropertyName("hasHelmet")]
    public bool HasHelmet { get; set; }
}
