namespace CaorenCup.WeaponPaints.Core;

public sealed record CatalogItem(
    string Key,
    string ChineseName,
    string EnglishName,
    uint Id = 0,
    string? WeaponKey = null,
    ushort DefIndex = 0)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ChineseName) ? EnglishName : ChineseName;
}

public static class CatalogSearch
{
    public static IReadOnlyList<CatalogItem> Find(IEnumerable<CatalogItem> items, string? query, int limit)
    {
        if (limit <= 0)
        {
            return Array.Empty<CatalogItem>();
        }

        var normalized = query?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(normalized)
            ? items
            : items.Where(item =>
                item.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.ChineseName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.EnglishName.Contains(normalized, StringComparison.OrdinalIgnoreCase));

        return filtered.Take(limit).ToArray();
    }
}
