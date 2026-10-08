using CaorenCup.WeaponPaints;
using CaorenCup.WeaponPaints.Core;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class LegacyAdapterTests
{
    [Fact]
    public void Adapter_PreservesAllWeaponAttributes()
    {
        var source = new WeaponSelection(7, 801)
        {
            Wear = 0.12f,
            Seed = 321,
            NameTag = "草人杯",
            StatTrakEnabled = true,
            StatTrakCount = 17,
            Keychain = new KeychainSelection(4, 0.1f, 0.2f, 0.3f, 99)
        };
        for (byte slot = 0; slot < 5; slot++)
        {
            source.SetSticker(slot, new StickerSelection((uint)(100 + slot), 7, 0.1f, 0.2f, 0.3f, 1.1f, 15));
        }

        var adapted = LegacyLoadoutAdapter.ToWeaponInfo(source);

        Assert.Equal(801, adapted.Paint);
        Assert.Equal(321, adapted.Seed);
        Assert.Equal("草人杯", adapted.Nametag);
        Assert.True(adapted.StatTrak);
        Assert.Equal(17, adapted.StatTrakCount);
        Assert.Equal((uint)4, adapted.KeyChain!.Id);
        Assert.Equal(5, adapted.Stickers.Count);
    }

    [Fact]
    public void Adapter_ParsesGloveAndAgentStorageKeys()
    {
        Assert.True(LegacyLoadoutAdapter.TryParseGlove("5030:10006", out var defIndex, out var paint));
        Assert.Equal((ushort)5030, defIndex);
        Assert.Equal(10006, paint);
        Assert.Equal("models/player/custom_player/legacy/tm_phoenix.mdl",
            LegacyLoadoutAdapter.ExtractAgentModel("2:models/player/custom_player/legacy/tm_phoenix.mdl"));
        Assert.False(LegacyLoadoutAdapter.TryParseGlove("0:0", out _, out _));
        Assert.Equal(string.Empty, LegacyLoadoutAdapter.ExtractAgentModel("2:null"));
        Assert.Equal((ushort)0, LegacyLoadoutAdapter.ParseIdOrDefault(string.Empty));
        Assert.Equal((ushort)12, LegacyLoadoutAdapter.ParseIdOrDefault("12"));
    }
}
