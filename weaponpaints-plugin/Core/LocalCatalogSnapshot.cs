using Newtonsoft.Json.Linq;

namespace CaorenCup.WeaponPaints.Core;

public sealed class LocalCatalogSnapshot
{
    private static readonly IReadOnlyDictionary<CatalogCategory, string> FileNames =
        new Dictionary<CatalogCategory, string>
        {
            [CatalogCategory.Skin] = "skins.json",
            [CatalogCategory.Glove] = "gloves.json",
            [CatalogCategory.Agent] = "agents.json",
            [CatalogCategory.MusicKit] = "music.json",
            [CatalogCategory.Pin] = "collectibles.json",
            [CatalogCategory.Sticker] = "stickers.json",
            [CatalogCategory.Keychain] = "keychains.json"
        };

    private readonly IReadOnlyDictionary<CatalogCategory, IReadOnlyList<CatalogItem>> _items;

    private LocalCatalogSnapshot(
        IReadOnlyDictionary<CatalogCategory, IReadOnlyList<CatalogItem>> items,
        IReadOnlyList<JObject> legacySkinMetadata)
    {
        _items = items;
        LegacySkinMetadata = legacySkinMetadata;
    }

    public IReadOnlyList<CatalogItem> this[CatalogCategory category] => _items[category];
    public IEnumerable<CatalogItem> AllItems => _items.Values.SelectMany(items => items);
    public IReadOnlyList<JObject> LegacySkinMetadata { get; }

    public static LocalCatalogSnapshot Load(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("数据目录不能为空。", nameof(dataRoot));
        }

        var englishRoot = Path.Combine(dataRoot, "en");
        var chineseRoot = Path.Combine(dataRoot, "zh-CN");
        var result = new Dictionary<CatalogCategory, IReadOnlyList<CatalogItem>>();
        string? englishSkins = null;

        foreach (var (category, fileName) in FileNames)
        {
            var englishPath = Path.Combine(englishRoot, fileName);
            var chinesePath = Path.Combine(chineseRoot, fileName);
            if (!File.Exists(englishPath) || !File.Exists(chinesePath))
            {
                throw new FileNotFoundException($"缺少物品目录文件：{fileName}");
            }

            var english = File.ReadAllText(englishPath);
            var chinese = File.ReadAllText(chinesePath);
            result[category] = BilingualCatalog.Parse(english, chinese, category);
            if (category == CatalogCategory.Skin)
            {
                englishSkins = english;
            }
        }

        var metadata = JArray.Parse(englishSkins ?? "[]").OfType<JObject>().ToArray();
        return new LocalCatalogSnapshot(result, metadata);
    }
}
