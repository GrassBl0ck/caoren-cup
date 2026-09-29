using CaorenCup.WeaponPaints.Core;
using CaorenCup.WeaponPaints.Persistence;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class RecordMapperTests
{
    [Fact]
    public void WeaponRow_UsesMySqlProviderTypesForDecimalAndUnsignedSmallintColumns()
    {
        var parameters = typeof(WeaponRow).GetConstructors().Single().GetParameters()
            .ToDictionary(parameter => parameter.Name!);

        Assert.Equal(typeof(decimal), parameters[nameof(WeaponRow.Wear)].ParameterType);
        Assert.Equal(typeof(ushort), parameters[nameof(WeaponRow.Seed)].ParameterType);
        Assert.Equal(typeof(byte), parameters[nameof(WeaponRow.StatTrakEnabled)].ParameterType);
    }

    [Fact]
    public void Mapper_RebuildsCompleteTeamSpecificLoadout()
    {
        var weapons = new[]
        {
            new WeaponRow("76561198000000001", 2, 7, 801, 0.12m, 321, "草人杯", 1, 9,
                4, 0.1f, 0.2f, 0.3f, 99),
            new WeaponRow("76561198000000001", 3, 7, 302, 0.01m, 12, "", 0, 0,
                0, 0, 0, 0, 0)
        };
        var stickers = new[]
        {
            new StickerRow("76561198000000001", 2, 7, 4, 123, 7, 0.1f, 0.2f, 0.3f, 1.2f, 15)
        };
        var cosmetics = new[]
        {
            new CosmeticRow("76561198000000001", 2, "Knife", "weapon_knife_karambit"),
            new CosmeticRow("76561198000000001", 3, "MusicKit", "12")
        };

        var loadout = LoadoutRecordMapper.Map("76561198000000001", weapons, stickers, cosmetics);

        var tWeapon = loadout.GetWeapon(TeamSide.Terrorist, 7)!;
        Assert.Equal((uint)801, tWeapon.PaintId);
        Assert.Equal((uint)4, tWeapon.Keychain!.Id);
        Assert.Equal((uint)123, tWeapon.Stickers[4].Id);
        Assert.Equal((uint)302, loadout.GetWeapon(TeamSide.CounterTerrorist, 7)!.PaintId);
        Assert.Equal("weapon_knife_karambit", loadout.GetCosmetic(TeamSide.Terrorist, CosmeticKind.Knife));
        Assert.Equal("12", loadout.GetCosmetic(TeamSide.CounterTerrorist, CosmeticKind.MusicKit));
    }

    [Fact]
    public void Mapper_IgnoresUnknownTeamsKindsAndStickerSlots()
    {
        var loadout = LoadoutRecordMapper.Map(
            "76561198000000001",
            [new WeaponRow("76561198000000001", 1, 7, 801, 0, 0, "", 0, 0, 0, 0, 0, 0, 0)],
            [new StickerRow("76561198000000001", 2, 7, 9, 123, 0, 0, 0, 0, 1, 0)],
            [new CosmeticRow("76561198000000001", 2, "Unknown", "value")]);

        Assert.Empty(loadout.GetWeapons(TeamSide.Terrorist));
        Assert.Equal(string.Empty, loadout.GetCosmetic(TeamSide.Terrorist, CosmeticKind.Knife));
    }

    [Fact]
    public void StickerWritePlan_AlwaysWritesAllFiveSlotsSoResetCannotLeaveOldData()
    {
        var weapon = new WeaponSelection(7, 801);
        weapon.SetSticker(4, new StickerSelection(123));

        var writes = StickerWritePlan.For(weapon);

        Assert.Equal(5, writes.Count);
        Assert.Equal((uint)0, writes[0].Sticker.Id);
        Assert.Equal((uint)123, writes[4].Sticker.Id);
    }
}
