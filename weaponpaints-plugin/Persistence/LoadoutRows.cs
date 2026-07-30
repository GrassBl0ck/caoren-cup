using CaorenCup.WeaponPaints.Core;

namespace CaorenCup.WeaponPaints.Persistence;

public sealed record WeaponRow(
    string SteamId,
    byte Team,
    ushort WeaponDefIndex,
    uint PaintId,
    float Wear,
    uint Seed,
    string NameTag,
    bool StatTrakEnabled,
    int StatTrakCount,
    uint KeychainId,
    float KeychainOffsetX,
    float KeychainOffsetY,
    float KeychainOffsetZ,
    uint KeychainSeed);

public sealed record StickerRow(
    string SteamId,
    byte Team,
    ushort WeaponDefIndex,
    byte Slot,
    uint StickerId,
    uint StickerSchema,
    float OffsetX,
    float OffsetY,
    float Wear,
    float Scale,
    float Rotation);

public sealed record CosmeticRow(string SteamId, byte Team, string Kind, string ItemKey);

public static class LoadoutRecordMapper
{
    public static PlayerLoadout Map(
        string steamId,
        IEnumerable<WeaponRow> weapons,
        IEnumerable<StickerRow> stickers,
        IEnumerable<CosmeticRow> cosmetics)
    {
        var loadout = new PlayerLoadout(steamId);

        foreach (var row in weapons)
        {
            if (!TryTeam(row.Team, out var team))
            {
                continue;
            }

            var selection = new WeaponSelection(row.WeaponDefIndex, row.PaintId)
            {
                Wear = row.Wear,
                Seed = row.Seed,
                NameTag = row.NameTag ?? string.Empty,
                StatTrakEnabled = row.StatTrakEnabled,
                StatTrakCount = row.StatTrakCount,
                Keychain = row.KeychainId == 0
                    ? null
                    : new KeychainSelection(
                        row.KeychainId,
                        row.KeychainOffsetX,
                        row.KeychainOffsetY,
                        row.KeychainOffsetZ,
                        row.KeychainSeed)
            };
            loadout.SetWeapon(team, selection);
        }

        foreach (var row in stickers)
        {
            if (row.Slot > 4 || !TryTeam(row.Team, out var team))
            {
                continue;
            }

            var selection = loadout.GetWeapon(team, row.WeaponDefIndex);
            if (selection is null)
            {
                continue;
            }

            selection.SetSticker(row.Slot, new StickerSelection(
                row.StickerId,
                row.StickerSchema,
                row.OffsetX,
                row.OffsetY,
                row.Wear,
                row.Scale,
                row.Rotation));
        }

        foreach (var row in cosmetics)
        {
            if (!TryTeam(row.Team, out var team) ||
                !Enum.TryParse<CosmeticKind>(row.Kind, true, out var kind))
            {
                continue;
            }

            loadout.SetCosmetic(team, kind, row.ItemKey);
        }

        return loadout;
    }

    private static bool TryTeam(byte value, out TeamSide team)
    {
        if (value is (byte)TeamSide.Terrorist or (byte)TeamSide.CounterTerrorist)
        {
            team = (TeamSide)value;
            return true;
        }

        team = default;
        return false;
    }
}
