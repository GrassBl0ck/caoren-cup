using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace CS2MiniGames.Framework;

public static class PlayerMovementGuard
{
    public static void Freeze(CCSPlayerController? controller) =>
        SetMoveType(controller, MoveType_t.MOVETYPE_NONE, actualMoveType: 0);

    public static void Restore(CCSPlayerController? controller) =>
        SetMoveType(controller, MoveType_t.MOVETYPE_WALK, actualMoveType: 2);

    private static void SetMoveType(
        CCSPlayerController? controller,
        MoveType_t moveType,
        int actualMoveType)
    {
        if (controller is null || !controller.IsValid)
        {
            return;
        }

        var pawn = controller.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            return;
        }

        pawn.MoveType = moveType;
        Schema.SetSchemaValue(
            pawn.Handle,
            "CBaseEntity",
            "m_nActualMoveType",
            actualMoveType);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }
}
