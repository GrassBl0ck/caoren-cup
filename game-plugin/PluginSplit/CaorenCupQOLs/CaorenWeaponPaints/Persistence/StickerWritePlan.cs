using CaorenCup.WeaponPaints.Core;

namespace CaorenCup.WeaponPaints.Persistence;

public sealed record StickerWrite(byte Slot, StickerSelection Sticker);

public static class StickerWritePlan
{
    public static IReadOnlyList<StickerWrite> For(WeaponSelection weapon)
    {
        var result = new StickerWrite[5];
        for (byte slot = 0; slot < 5; slot++)
        {
            weapon.Stickers.TryGetValue(slot, out var sticker);
            result[slot] = new StickerWrite(slot, sticker ?? new StickerSelection(0));
        }

        return result;
    }
}
