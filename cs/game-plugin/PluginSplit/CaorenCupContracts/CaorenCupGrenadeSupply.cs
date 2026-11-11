using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CaorenCup.Contracts;

public static class CaorenCupGrenadeSupply
{
    public static readonly string[] Types = ["weapon_hegrenade", "weapon_smokegrenade", "weapon_flashbang",
        "weapon_molotov", "weapon_incgrenade", "weapon_decoy"];

    public static IReadOnlyDictionary<string, int> Inventory(CCSPlayerPawn pawn)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var weapons = pawn.WeaponServices;
        if (weapons is null) return result;
        foreach (var handle in weapons.MyWeapons)
        {
            var weapon = handle.Value;
            if (weapon is not { IsValid: true } || !Types.Contains(weapon.DesignerName)) continue;
            var ammoType = weapon.As<CCSWeaponBase>().VData?.PrimaryAmmoType;
            if (ammoType is not { } index || index >= weapons.Ammo.Length) continue;
            result[weapon.DesignerName] = weapons.Ammo[index];
        }
        return result;
    }

    public static bool TryGive(CCSPlayerController player, string weapon)
    {
        if (!player.IsValid || !player.PawnIsAlive || !Types.Contains(weapon) || player.PlayerPawn.Value is not { } pawn)
            return false;
        var inventory = Inventory(pawn);
        var count = inventory.GetValueOrDefault(weapon);
        var typeLimit = ConVar.Find(weapon == "weapon_flashbang" ? "ammo_grenade_limit_flashbang" : "ammo_grenade_limit_default")
            ?.GetPrimitiveValue<int>() ?? (weapon == "weapon_flashbang" ? 2 : 1);
        var totalLimit = ConVar.Find("ammo_grenade_limit_total")?.GetPrimitiveValue<int>() ?? 4;
        if (count >= typeLimit || inventory.Values.Sum() >= totalLimit) return false;
        player.GiveNamedItem(weapon);
        return Inventory(pawn).GetValueOrDefault(weapon) > count;
    }
}
