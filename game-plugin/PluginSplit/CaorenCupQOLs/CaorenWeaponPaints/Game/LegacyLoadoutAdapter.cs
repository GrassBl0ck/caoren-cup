using System.Collections.Concurrent;
using System.Globalization;
using CaorenCup.WeaponPaints.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.WeaponPaints;

public static class LegacyLoadoutAdapter
{
    public static WeaponInfo ToWeaponInfo(WeaponSelection source)
    {
        var result = new WeaponInfo
        {
            Paint = checked((int)source.PaintId),
            Seed = checked((int)source.Seed),
            Wear = source.Wear,
            Nametag = source.NameTag,
            StatTrak = source.StatTrakEnabled,
            StatTrakCount = source.StatTrakCount,
            KeyChain = source.Keychain is null
                ? null
                : new KeyChainInfo
                {
                    Id = source.Keychain.Id,
                    OffsetX = source.Keychain.OffsetX,
                    OffsetY = source.Keychain.OffsetY,
                    OffsetZ = source.Keychain.OffsetZ,
                    Seed = source.Keychain.Seed
                }
        };

        if (source.Stickers.Count > 0)
        {
            for (byte slot = 0; slot < 5; slot++)
            {
                source.Stickers.TryGetValue(slot, out var sticker);
                result.Stickers.Add(new StickerInfo
                {
                    Id = sticker?.Id ?? 0,
                    Schema = sticker?.Schema ?? 0,
                    OffsetX = sticker?.OffsetX ?? 0,
                    OffsetY = sticker?.OffsetY ?? 0,
                    Wear = sticker?.Wear ?? 0,
                    Scale = sticker?.Scale ?? 1,
                    Rotation = sticker?.Rotation ?? 0
                });
            }
        }

        return result;
    }

    public static void ApplyToRuntime(int slot, PlayerLoadout loadout)
    {
        CaorenWeaponPaintsPlugin.GPlayerWeaponsInfo.TryRemove(slot, out _);
        CaorenWeaponPaintsPlugin.GPlayersKnife.TryRemove(slot, out _);
        CaorenWeaponPaintsPlugin.GPlayersGlove.TryRemove(slot, out _);
        CaorenWeaponPaintsPlugin.GPlayersMusic.TryRemove(slot, out _);
        CaorenWeaponPaintsPlugin.GPlayersPin.TryRemove(slot, out _);
        CaorenWeaponPaintsPlugin.GPlayersAgent.TryRemove(slot, out _);

        string? terroristAgent = null;
        string? counterTerroristAgent = null;

        foreach (var team in new[] { TeamSide.Terrorist, TeamSide.CounterTerrorist })
        {
            var csTeam = (CsTeam)(byte)team;
            var teamWeapons = new ConcurrentDictionary<int, WeaponInfo>();
            foreach (var (defIndex, weapon) in loadout.GetWeapons(team))
            {
                teamWeapons[defIndex] = ToWeaponInfo(weapon);
            }

            CaorenWeaponPaintsPlugin.GPlayerWeaponsInfo
                .GetOrAdd(slot, _ => new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>())
                [csTeam] = teamWeapons;

            var knife = loadout.GetCosmetic(team, CosmeticKind.Knife);
            if (!string.IsNullOrWhiteSpace(knife))
            {
                CaorenWeaponPaintsPlugin.GPlayersKnife
                    .GetOrAdd(slot, _ => new ConcurrentDictionary<CsTeam, string>())[csTeam] = knife;
            }

            var glove = loadout.GetCosmetic(team, CosmeticKind.Glove);
            if (TryParseGlove(glove, out var gloveDefIndex, out var glovePaint))
            {
                CaorenWeaponPaintsPlugin.GPlayersGlove
                    .GetOrAdd(slot, _ => new ConcurrentDictionary<CsTeam, ushort>())[csTeam] = gloveDefIndex;
                teamWeapons[gloveDefIndex] = new WeaponInfo { Paint = glovePaint, Wear = 0.000001f };
            }

            var music = ParseIdOrDefault(loadout.GetCosmetic(team, CosmeticKind.MusicKit));
            CaorenWeaponPaintsPlugin.GPlayersMusic
                .GetOrAdd(slot, _ => new ConcurrentDictionary<CsTeam, ushort>())[csTeam] = music;

            var pin = ParseIdOrDefault(loadout.GetCosmetic(team, CosmeticKind.Pin));
            CaorenWeaponPaintsPlugin.GPlayersPin
                .GetOrAdd(slot, _ => new ConcurrentDictionary<CsTeam, ushort>())[csTeam] = pin;

            var agent = ExtractAgentModel(loadout.GetCosmetic(team, CosmeticKind.Agent));
            if (team == TeamSide.Terrorist)
            {
                terroristAgent = agent;
            }
            else
            {
                counterTerroristAgent = agent;
            }
        }

        if (!string.IsNullOrWhiteSpace(terroristAgent) || !string.IsNullOrWhiteSpace(counterTerroristAgent))
        {
            CaorenWeaponPaintsPlugin.GPlayersAgent[slot] = (counterTerroristAgent, terroristAgent);
        }
    }

    public static bool TryParseGlove(string value, out ushort defIndex, out int paint)
    {
        defIndex = 0;
        paint = 0;
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out defIndex) &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out paint) &&
               defIndex != 0;
    }

    public static string ExtractAgentModel(string value)
    {
        var separator = value.IndexOf(':');
        var model = separator >= 0 ? value[(separator + 1)..] : value;
        return model.Equals("null", StringComparison.OrdinalIgnoreCase) ? string.Empty : model;
    }

    public static ushort ParseIdOrDefault(string value)
    {
        return ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : (ushort)0;
    }
}
