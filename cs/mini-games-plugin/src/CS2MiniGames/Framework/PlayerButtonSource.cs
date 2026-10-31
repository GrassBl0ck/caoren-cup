using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace CS2MiniGames.Framework;

internal static class PlayerButtonSource
{
    public static PlayerButtons Read(
        CBasePlayerController player,
        Func<PlayerButtons> readButtons)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(readButtons);

        var pawn = player.Pawn.Value;
        return Read(
            pawn is not null && pawn.IsValid && pawn.MovementServices is not null,
            readButtons);
    }

    public static PlayerButtons Read(
        bool hasInputSource,
        Func<PlayerButtons> readButtons) =>
        hasInputSource ? readButtons() : default;
}
