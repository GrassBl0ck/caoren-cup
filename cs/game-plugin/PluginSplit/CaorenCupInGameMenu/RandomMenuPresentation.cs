using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

/// <summary>纯展示逻辑；不在菜单里计算价格、可用性或变更玩家BUFF。</summary>
public static class RandomMenuPresentation
{
    public static readonly string[] QualityCodes = ["D", "C", "B", "A", "S"];
    public static readonly string[] Categories = ["进攻", "防御", "机动", "战术", "补给"];

    public static string CardDescription(RandomBuffView card, bool detailed) =>
        detailed ? card.DetailedDescription : card.SimpleDescription;

    public static string CardTitle(RandomBuffView card) => card.Level > 1 ? $"{card.Name}-{card.Level}级" : card.Name;

    public static string CategoryIcon(string category) => category switch
    {
        "进攻" => "attack", "防御" => "defense", "机动" => "mobility", "战术" => "tactics", "补给" => "supply", _ => ""
    };

    /// <summary>成功交易先反馈，再关闭并释放输入；失败只反馈，不改变当前查看状态。</summary>
    public static bool FinishPurchase(GameModeResult result, Action<string> notify, Action close)
    {
        notify(result.Message);
        if (!result.Success) return false;
        close();
        return true;
    }

    public static string QualityName(string quality) => quality switch
    {
        "D" => "普通", "C" => "精良", "B" => "稀有", "A" => "史诗", "S" => "传说", _ => quality
    };

    public static IReadOnlyDictionary<string, int> CountQualities(IEnumerable<RandomBuffView> owned) =>
        Count(owned, QualityCodes, buff => buff.Quality);

    public static IReadOnlyDictionary<string, int> CountCategories(IEnumerable<RandomBuffView> owned) =>
        Count(owned, Categories, buff => buff.Category);

    private static IReadOnlyDictionary<string, int> Count(IEnumerable<RandomBuffView> owned, string[] keys, Func<RandomBuffView, string> select)
    {
        var result = keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        foreach (var buff in owned.DistinctBy(buff => buff.Key))
            if (result.ContainsKey(select(buff))) result[select(buff)]++;
        return result;
    }

    // 保守的行宽与每页行数，完整保留文字；不通过缩小字号容纳详细机制。
    public static IReadOnlyList<string> DescriptionPages(string description)
    {
        const int lineLength = 38, linesPerPage = 7;
        var lines = new List<string>();
        foreach (var paragraph in description.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { lines.Add(""); continue; }
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(paragraph);
            var line = new System.Text.StringBuilder();
            var count = 0;
            while (elements.MoveNext())
            {
                line.Append(elements.GetTextElement());
                if (++count < lineLength) continue;
                lines.Add(line.ToString()); line.Clear(); count = 0;
            }
            if (line.Length > 0) lines.Add(line.ToString());
        }
        return lines.Chunk(linesPerPage).Select(chunk => string.Join("\n", chunk)).DefaultIfEmpty("").ToArray();
    }
}
