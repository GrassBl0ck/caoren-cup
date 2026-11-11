using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Contracts;

public static class CaorenCupPlayerMovement
{
    public static void SetMultiplier(CCSPlayerPawn pawn, float multiplier)
    {
        if (!pawn.IsValid) return;
        pawn.VelocityModifier = Math.Max(0.01f, multiplier);
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
    }
}
