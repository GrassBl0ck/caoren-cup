using System.Text.Json;

namespace CaorenCup.Gamemodes.Random;

public static class RandomCatalog
{
    public const string Reload = "reload_aftercare";
    public const string Medkit = "pocket_medkit";
    public const string Ghost = "ghost_delivery";
    public const string TeamRescue = "team_rescue";
    public const string Tank = "tank_engine";
    public const string Light = "light_engine";
    public static IReadOnlyList<BuffDefinition> Cards { get; } = Load();

    public static BuffDefinition? Find(string input) => Cards.FirstOrDefault(card =>
        string.Equals(card.Key, input, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(card.DisplayId, input, StringComparison.OrdinalIgnoreCase) || card.Name == input);

    private static IReadOnlyList<BuffDefinition> Load()
    {
        using var stream = typeof(RandomCatalog).Assembly.GetManifestResourceStream("CaorenCup.Random.Catalog")
            ?? throw new InvalidOperationException("随机乱斗卡牌目录缺失。");
        using var document = JsonDocument.Parse(stream);
        var cards = new List<BuffDefinition>();
        foreach (var item in document.RootElement.GetProperty("cards").EnumerateArray())
        {
            var descriptions = item.GetProperty("levels").EnumerateArray().Select(level =>
                new BuffDefinition.Description(level.GetProperty("simple").GetString()!, level.GetProperty("detailed").GetString()!)).ToArray();
            var card = new BuffDefinition(item.GetProperty("key").GetString()!, item.GetProperty("displayId").GetString()!,
                item.GetProperty("name").GetString()!, item.GetProperty("category").GetString()!,
                item.GetProperty("simple").GetString()!, item.GetProperty("detailed").GetString()!,
                item.GetProperty("maxLevel").GetInt32(), item.GetProperty("cost").GetInt32())
            {
                Quality = item.GetProperty("quality").GetString()!,
                Scope = item.GetProperty("scope").GetString()!,
                Active = item.GetProperty("active").GetBoolean(),
                LevelDescriptions = Array.AsReadOnly(descriptions)
            };
            if (card.Cost < 0 || card.MaxLevel < 1 || descriptions.Length != card.MaxLevel)
                throw new InvalidOperationException($"卡牌定义无效 {card.Key}");
            cards.Add(card);
        }
        if (cards.Count != 46 || cards.Select(card => card.Key).Distinct(StringComparer.Ordinal).Count() != cards.Count)
            throw new InvalidOperationException("随机乱斗必须包含46个唯一稳定键。");
        return cards.AsReadOnly();
    }
}
