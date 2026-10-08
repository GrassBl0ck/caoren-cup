using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CaorenCup.Features.InGameMenu;

public sealed record HelpFragment(string Text, int? Parameter = null);
public sealed record HelpExample(HelpFragment[] Command, HelpFragment[] Effect);
public sealed record HelpRow(IReadOnlyList<HelpFragment> Fragments, bool Heading = false);

public sealed record CommandHelpEntry
{
    public string Name { get; init; } = "";
    public string Console { get; init; } = "";
    public string Module { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Permission { get; init; } = "@css/root";
    public string[] Usage { get; init; } = [];
    public string[] Parameters { get; init; } = [];
    public string[] Examples { get; init; } = [];
    public string[] Notes { get; init; } = [];
    public string[] Sources { get; init; } = [];
    public HelpFragment[][] ColoredUsage { get; init; } = [];
    public HelpFragment[][] ColoredParameters { get; init; } = [];
    public HelpExample[] WorkedExamples { get; init; } = [];
}

/// <summary>只读帮助数据。浏览、翻页和查看详情均不执行指令。</summary>
public sealed class CommandHelpCatalog
{
    public const int ListPageSize = 8;
    public const int DetailPageSize = 12;
    public const int DetailFragmentsPerRow = 24;
    public static IReadOnlyList<string> ParameterColors { get; } = Array.AsReadOnly(new[]
        { "#82D8FF", "#FFC776", "#B5E48C", "#D7A8FF", "#FF9EAA", "#80E0CF", "#F2E58C", "#A8B8FF" });
    public IReadOnlyList<CommandHelpEntry> Entries { get; }

    public CommandHelpCatalog(IEnumerable<CommandHelpEntry> entries)
    {
        var values = entries.OrderBy(e => e.Name.TrimStart('/'), StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Any(e => string.IsNullOrWhiteSpace(e.Name) || e.Usage.Length == 0 || e.Parameters.Length == 0))
            throw new InvalidDataException("Command help requires a name, usage and parameter explanation.");
        if (values.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            throw new InvalidDataException("Duplicate command help entry.");
        Entries = Array.AsReadOnly(values);
        foreach (var entry in values)
        foreach (var fragment in entry.ColoredUsage.SelectMany(row => row).Concat(entry.ColoredParameters.SelectMany(row => row))
            .Concat(entry.WorkedExamples.SelectMany(example => example.Command.Concat(example.Effect))))
            if (fragment.Parameter is { } index && (index < 0 || index >= ParameterColors.Count))
                throw new InvalidDataException("帮助参数颜色不够，必须扩展调色板：" + entry.Name);
    }

    public static CommandHelpCatalog Load()
        => LoadResource("CaorenCup.InGameMenu.CommandHelpCatalog.json");

    public static CommandHelpCatalog LoadPlayer()
        => LoadResource("CaorenCup.InGameMenu.PlayerCommandHelpCatalog.json");

    private static CommandHelpCatalog LoadResource(string resource)
    {
        using var stream = typeof(CommandHelpCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("Embedded command help catalog is missing.");
        var entries = JsonSerializer.Deserialize<CommandHelpEntry[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Command help catalog is empty.");
        return new CommandHelpCatalog(entries);
    }

    public int ListPageCount => Math.Max(1, (Entries.Count + ListPageSize - 1) / ListPageSize);
    public IReadOnlyList<CommandHelpEntry> ListPage(int index) => Entries.Skip(Math.Clamp(index, 0, ListPageCount - 1) * ListPageSize).Take(ListPageSize).ToArray();

    public IReadOnlyList<string> DetailLines(CommandHelpEntry entry)
    {
        var lines = new List<string> { "用法" };
        lines.AddRange(entry.Usage);
        lines.Add("参数意义"); lines.AddRange(entry.Parameters);
        if (entry.Examples.Length > 0) { lines.Add("示例"); lines.AddRange(entry.Examples); }
        if (entry.Notes.Length > 0) { lines.Add("说明"); lines.AddRange(entry.Notes); }
        return lines.SelectMany(line => Wrap(line, 76)).ToArray();
    }

    /// <summary>按可见文字宽度折行再着色；小标题至少与一行正文同行分页。</summary>
    public IReadOnlyList<string> DetailPages(CommandHelpEntry entry)
        => DetailFragmentPages(entry).Select(page => string.Join("\n", page.Select(row => string.Concat(row.Fragments.Select(f => f.Text))))).ToArray();

    public IReadOnlyList<IReadOnlyList<HelpRow>> DetailFragmentPages(CommandHelpEntry entry)
    {
        var rows = new List<HelpRow>();
        void Section(string title, IEnumerable<HelpFragment[]> paragraphs)
        {
            var items = paragraphs.ToArray();
            if (items.Length == 0) return;
            if (rows.Count != 0) rows.Add(new HelpRow([]));
            rows.Add(new HelpRow([new HelpFragment(title)], true));
            foreach (var paragraph in items) rows.AddRange(WrapFragments(paragraph, 76).Select(line => new HelpRow(line)));
        }
        Section("用法", entry.ColoredUsage.Length > 0 ? entry.ColoredUsage : entry.Usage.Select(line => new[] { new HelpFragment(line) }));
        Section("参数意义", entry.ColoredParameters.Length > 0 ? entry.ColoredParameters : entry.Parameters.Select(line => new[] { new HelpFragment(line) }));
        if (entry.WorkedExamples.Length > 0)
            Section("示例及具体效果", entry.WorkedExamples.SelectMany(example => new[] { example.Command, example.Effect }));
        else Section("示例", entry.Examples.Select(line => new[] { new HelpFragment(line) }));
        Section("补充说明", entry.Notes.Select(line => new[] { new HelpFragment(line) }));
        var pages = new List<IReadOnlyList<HelpRow>>(); var page = new List<HelpRow>();
        foreach (var row in rows)
        {
            if (row.Fragments.Count > DetailFragmentsPerRow) throw new InvalidDataException("帮助行片段数量超过布局容量：" + entry.Name);
            if (page.Count >= DetailPageSize || (row.Heading && page.Count >= DetailPageSize - 1))
            { pages.Add(page.ToArray()); page.Clear(); }
            if (page.Count == 0 && row.Fragments.Count == 0) continue;
            page.Add(row);
        }
        if (page.Count > 0) pages.Add(page.ToArray());
        return pages.Count == 0 ? new IReadOnlyList<HelpRow>[] { Array.Empty<HelpRow>() } : pages;
    }

    private static IEnumerable<IReadOnlyList<HelpFragment>> WrapFragments(IEnumerable<HelpFragment> fragments, int columns)
    {
        var row = new List<HelpFragment>(); var width = 0;
        foreach (var fragment in fragments)
        {
            var part = new StringBuilder();
            foreach (var rune in fragment.Text.EnumerateRunes())
            {
                var next = rune.Value >= 0x2E80 ? 2 : 1;
                if (width + next > columns)
                {
                    if (part.Length > 0) { row.Add(new HelpFragment(part.ToString(), fragment.Parameter)); part.Clear(); }
                    yield return row.ToArray(); row.Clear(); width = 0;
                }
                part.Append(rune.ToString()); width += next;
            }
            if (part.Length > 0) row.Add(new HelpFragment(part.ToString(), fragment.Parameter));
        }
        yield return row.ToArray();
    }

    private static IEnumerable<string> Wrap(string value, int columns)
    {
        var text = new StringBuilder(); var width = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var next = rune.Value >= 0x2E80 ? 2 : 1;
            if (width + next > columns) { yield return text.ToString(); text.Clear(); width = 0; }
            text.Append(rune.ToString()); width += next;
        }
        if (text.Length > 0) yield return text.ToString();
    }
}
