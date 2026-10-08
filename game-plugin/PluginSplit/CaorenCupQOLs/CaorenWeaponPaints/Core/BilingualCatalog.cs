using System.Globalization;
using System.Text.Json;

namespace CaorenCup.WeaponPaints.Core;

public enum CatalogCategory
{
    Skin,
    Glove,
    Agent,
    MusicKit,
    Pin,
    Sticker,
    Keychain
}

public static class BilingualCatalog
{
    public static IReadOnlyList<CatalogItem> Parse(
        string englishJson,
        string chineseJson,
        CatalogCategory category)
    {
        var english = ParseLanguage(englishJson, category);
        var chinese = ParseLanguage(chineseJson, category)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return english.Select(item =>
        {
            if (!chinese.TryGetValue(item.Key, out var localized) ||
                string.IsNullOrWhiteSpace(localized.ChineseName))
            {
                return item;
            }

            return item with { ChineseName = localized.ChineseName };
        }).ToArray();
    }

    private static IReadOnlyList<CatalogItem> ParseLanguage(string json, CatalogCategory category)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("物品目录根节点必须是数组。");
        }

        var result = new List<CatalogItem>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var item = CreateItem(element, category);
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static CatalogItem? CreateItem(JsonElement element, CatalogCategory category)
    {
        return category switch
        {
            CatalogCategory.Skin => CreateWeaponItem(element),
            CatalogCategory.Glove => CreateGloveItem(element),
            CatalogCategory.Agent => CreateAgentItem(element),
            CatalogCategory.MusicKit => CreateSimpleItem(element, "name"),
            CatalogCategory.Pin => CreateSimpleItem(element, "name"),
            CatalogCategory.Sticker => CreateSimpleItem(element, "name"),
            CatalogCategory.Keychain => CreateSimpleItem(element, "name"),
            _ => null
        };
    }

    private static CatalogItem? CreateWeaponItem(JsonElement element)
    {
        var weapon = ReadString(element, "weapon_name");
        var name = ReadString(element, "paint_name");
        if (string.IsNullOrWhiteSpace(weapon) || !TryReadUInt(element, "paint", out var paint))
        {
            return null;
        }

        TryReadUInt(element, "weapon_defindex", out var defIndex);
        return new CatalogItem($"{weapon}:{paint}", name, name, paint, weapon, checked((ushort)defIndex));
    }

    private static CatalogItem? CreateGloveItem(JsonElement element)
    {
        var name = ReadString(element, "paint_name");
        if (!TryReadUInt(element, "weapon_defindex", out var defIndex) ||
            !TryReadUInt(element, "paint", out var paint))
        {
            return null;
        }

        return new CatalogItem(
            $"{defIndex}:{paint}",
            name,
            name,
            paint,
            defIndex.ToString(CultureInfo.InvariantCulture),
            checked((ushort)defIndex));
    }

    private static CatalogItem? CreateAgentItem(JsonElement element)
    {
        var model = ReadString(element, "model");
        var name = ReadString(element, "agent_name");
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var team = ReadString(element, "team");
        return new CatalogItem($"{team}:{model}", name, name, 0, team);
    }

    private static CatalogItem? CreateSimpleItem(JsonElement element, string nameProperty)
    {
        if (!TryReadUInt(element, "id", out var id))
        {
            return null;
        }

        var name = ReadString(element, nameProperty);
        return new CatalogItem(id.ToString(CultureInfo.InvariantCulture), name, name, id);
    }

    private static bool TryReadUInt(JsonElement element, string propertyName, out uint value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetUInt32(out value),
            JsonValueKind.String => uint.TryParse(property.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }
}
