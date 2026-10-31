namespace CaorenCup.WeaponPaints.Core;

public enum TeamSide : byte
{
    Terrorist = 2,
    CounterTerrorist = 3
}

public enum CosmeticKind : byte
{
    Knife,
    Glove,
    Agent,
    MusicKit,
    Pin
}

public sealed record StickerSelection(
    uint Id,
    uint Schema = 0,
    float OffsetX = 0,
    float OffsetY = 0,
    float Wear = 0,
    float Scale = 1,
    float Rotation = 0);

public sealed record KeychainSelection(
    uint Id,
    float OffsetX = 0,
    float OffsetY = 0,
    float OffsetZ = 0,
    uint Seed = 0);

public sealed class WeaponSelection
{
    private readonly Dictionary<byte, StickerSelection> _stickers = new();

    public WeaponSelection(ushort weaponDefIndex, uint paintId)
    {
        WeaponDefIndex = weaponDefIndex;
        PaintId = paintId;
    }

    public ushort WeaponDefIndex { get; }
    public uint PaintId { get; set; }
    public float Wear { get; set; }
    public uint Seed { get; set; }
    public string NameTag { get; set; } = string.Empty;
    public bool StatTrakEnabled { get; set; }
    public int StatTrakCount { get; set; }
    public KeychainSelection? Keychain { get; set; }
    public IReadOnlyDictionary<byte, StickerSelection> Stickers => _stickers;

    public WeaponSelection Clone()
    {
        var clone = new WeaponSelection(WeaponDefIndex, PaintId)
        {
            Wear = Wear,
            Seed = Seed,
            NameTag = NameTag,
            StatTrakEnabled = StatTrakEnabled,
            StatTrakCount = StatTrakCount,
            Keychain = Keychain
        };
        foreach (var (slot, sticker) in _stickers)
        {
            clone.SetSticker(slot, sticker);
        }

        return clone;
    }

    public void SetSticker(byte slot, StickerSelection sticker)
    {
        if (slot > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "印花槽必须为 0 到 4。");
        }

        _stickers[slot] = sticker;
    }
}

public sealed class PlayerLoadout
{
    private readonly Dictionary<TeamSide, Dictionary<ushort, WeaponSelection>> _weapons = new();
    private readonly Dictionary<(TeamSide Team, CosmeticKind Kind), string> _cosmetics = new();

    public PlayerLoadout(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            throw new ArgumentException("SteamID64 不能为空。", nameof(steamId));
        }

        SteamId = steamId;
    }

    public string SteamId { get; }

    public void SetWeapon(TeamSide team, WeaponSelection selection)
    {
        if (!_weapons.TryGetValue(team, out var teamWeapons))
        {
            teamWeapons = new Dictionary<ushort, WeaponSelection>();
            _weapons[team] = teamWeapons;
        }

        teamWeapons[selection.WeaponDefIndex] = selection;
    }

    public WeaponSelection? GetWeapon(TeamSide team, ushort weaponDefIndex)
    {
        return _weapons.TryGetValue(team, out var teamWeapons) &&
               teamWeapons.TryGetValue(weaponDefIndex, out var selection)
            ? selection
            : null;
    }

    public IReadOnlyDictionary<ushort, WeaponSelection> GetWeapons(TeamSide team)
    {
        return _weapons.TryGetValue(team, out var teamWeapons)
            ? teamWeapons
            : new Dictionary<ushort, WeaponSelection>();
    }

    public void SetCosmetic(TeamSide team, CosmeticKind kind, string itemKey)
    {
        _cosmetics[(team, kind)] = itemKey ?? string.Empty;
    }

    public string GetCosmetic(TeamSide team, CosmeticKind kind)
    {
        return _cosmetics.TryGetValue((team, kind), out var itemKey) ? itemKey : string.Empty;
    }
}
