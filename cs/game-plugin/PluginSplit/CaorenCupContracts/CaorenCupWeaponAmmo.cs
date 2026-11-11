using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Contracts;

/// <summary>弹药补给共用容量边界和网络同步，补给不会模拟换弹事件。</summary>
public static class CaorenCupWeaponAmmo
{
    public static int Capacity(CBasePlayerWeapon weapon) => Math.Max(0, weapon.VData?.MaxClip1 ?? 0);

    public static int AddClip(CBasePlayerWeapon weapon, int amount)
    {
        if (!weapon.IsValid || amount <= 0 || !CaorenCupDamageSources.IsGun(weapon.DesignerName)) return 0;
        var added = Math.Min(amount, Math.Max(0, Capacity(weapon) - weapon.Clip1));
        if (added <= 0) return 0;
        weapon.Clip1 += added;
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
        return added;
    }
}
