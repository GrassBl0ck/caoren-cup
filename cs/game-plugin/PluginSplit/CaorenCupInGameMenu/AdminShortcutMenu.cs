using System.Globalization;
using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

internal sealed record ShortcutPlayer(ulong SteamId, int UserId, string Name);
internal sealed record ShortcutTarget(string Label, string Selector, ShortcutPlayer? Player = null);
internal sealed record ShortcutRow(string Label, Action? Select = null, string? Hint = null);
internal sealed record ShortcutNumber(int Minimum, Action<int> Accept)
{
    internal string Digits { get; set; } = "";
}
internal sealed record ShortcutPage(string Title, string Detail, IReadOnlyList<ShortcutRow> Rows, ShortcutNumber? Number = null);

internal interface IAdminShortcutHost
{
    IReadOnlyList<ShortcutPlayer> Players();
    HpCapSettings? HpCap();
    IReadOnlyList<string> Rules();
    MatchAdminState? Match();
    void Pause(MatchAdminRequest request);
    void Run(string command);
    void Notify(string message);
}

internal enum EquipmentAction { Give, Health, Loadout }
internal enum ShortcutSection { Equipment, HealthCap, Match }

internal sealed class AdminShortcutMenu
{
    internal const int Slots = 6;
    private readonly IAdminShortcutHost _host;
    private readonly Stack<(Func<ShortcutPage> Build, int Index, string Label)> _history = new();
    private Func<ShortcutPage> _build;
    private MatchAdminState? _lastMatch;

    internal ShortcutPage Page { get; private set; }
    internal ShortcutSection Section { get; }
    internal EquipmentAction? Operation { get; private set; }
    private string _label;
    internal string[] PathLabels => [.. _history.Reverse().Select(frame => frame.Label), _label];
    internal int Index { get; private set; }
    internal int Pages => Math.Max(1, (Page.Rows.Count + Slots - 1) / Slots);
    internal IEnumerable<ShortcutRow> VisibleRows => Page.Rows.Skip(Index * Slots).Take(Slots);

    internal AdminShortcutMenu(IAdminShortcutHost host, ShortcutSection section)
    {
        _host = host;
        Section = section;
        _build = section switch
        {
            ShortcutSection.Equipment => Equipment,
            ShortcutSection.HealthCap => HealthCap,
            ShortcutSection.Match => Match,
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        Page = _build();
        _label = Page.Title;
    }

    internal void Refresh()
    {
        Page = _build();
        Index = Math.Min(Index, Pages - 1);
    }

    internal void TurnPage(int delta) => Index = Math.Clamp(Index + delta, 0, Pages - 1);

    internal void Select(int slot)
    {
        if (slot >= 0 && slot < Slots)
            VisibleRows.ElementAtOrDefault(slot)?.Select?.Invoke();
    }

    private void Open(Func<ShortcutPage> build, string? label = null)
    {
        _history.Push((_build, Index, _label));
        _build = build;
        Index = 0;
        Refresh();
        _label = label ?? Page.Title;
    }

    internal bool Back()
    {
        return BackTo(_history.Count - 1);
    }

    internal bool BackTo(int depth)
    {
        if (depth < 0 || depth >= _history.Count) return false;
        if (depth == 0) Operation = null;
        while (_history.Count > depth)
            (_build, Index, _label) = _history.Pop();
        Refresh();
        return true;
    }

    internal void OpenOperation(EquipmentAction action)
    {
        if (Section != ShortcutSection.Equipment) return;
        BackTo(0);
        Operation = action;
        Open(() => Targets(action == EquipmentAction.Loadout, action == EquipmentAction.Health));
    }

    private ShortcutPage Equipment() => new("玩家与装备", "选择操作", [
        new("立即发放物品", () => OpenOperation(EquipmentAction.Give)),
        new("修改当前血量", () => OpenOperation(EquipmentAction.Health)),
        new("设置出生装备", () => OpenOperation(EquipmentAction.Loadout))
    ]);

    private ShortcutPage Targets(bool loadout, bool health)
    {
        var rows = new List<ShortcutRow>();
        if (!loadout) rows.Add(new("指定玩家", () => Open(() => Players(health))));
        foreach (var (label, team) in new[] { ("T 方全体", "t"), ("CT 方全体", "ct"), (loadout ? "双方" : "所有人", "all") })
        {
            var target = new ShortcutTarget(label, loadout ? team : "@" + team);
            rows.Add(new(label, () => ChooseTarget(target, loadout, health)));
        }
        var title = loadout ? "设置出生装备" : health ? "修改当前血量" : "立即发放物品";
        return new(title, loadout ? "选择阵营，从下次出生生效" : "选择作用对象，立即应用于存活玩家", rows);
    }

    private ShortcutPage Players(bool health)
    {
        var rows = _host.Players().Select(p => new ShortcutRow(p.Name + $"（#{p.UserId}）",
            () => ChooseTarget(new(p.Name, $"#{p.UserId}", p), false, health))).ToArray();
        return new("指定玩家", rows.Length == 0 ? "当前没有在线玩家" : "选择一位在线玩家", rows);
    }

    private void ChooseTarget(ShortcutTarget target, bool loadout, bool health)
    {
        if (health) Open(() => Health(target), target.Label);
        else if (loadout) Open(() => new("设置出生装备", target.Label + " · 从下次出生生效", [
            new("主武器", () => Open(() => Groups(target, "primary"), "主武器")),
            new("副武器", () => Open(() => Items(target, "secondary", ShortcutItems.Groups.Single(g => g.Slot == "secondary")), "副武器"))
        ]), target.Label);
        else Open(() => Groups(target, null), target.Label);
    }

    private ShortcutPage Groups(ShortcutTarget target, string? slot)
    {
        var groups = ShortcutItems.Groups.Where(g => slot == null || g.Slot == slot);
        return new(slot == null ? "立即发放物品" : "设置出生装备", target.Label + " · 选择类别",
            groups.Select(g => new ShortcutRow(g.Name, () => Open(() => Items(target, slot, g)))).ToArray());
    }

    private ShortcutPage Items(ShortcutTarget target, string? slot, ShortcutItemGroup group) =>
        new(group.Name, target.Label + (slot == null ? " · 点击立即发放" : " · 点击设置下次出生装备"),
            group.Items.Select(item => new ShortcutRow(item.Name, () =>
                Apply(target, slot == null ? $"css_give {target.Selector} {item.Id}"
                    : $"css_loadout weapon {target.Selector} {slot} {item.EntityName}"))).ToArray());

    private void Apply(ShortcutTarget target, string command)
    {
        if (target.Player is { } p && !_host.Players().Any(live => live.SteamId == p.SteamId && live.UserId == p.UserId))
        {
            _host.Notify("该玩家已离服或重新连接，请重新选择。");
            return;
        }
        _host.Run(command);
    }

    private ShortcutPage Health(ShortcutTarget target)
    {
        void SetHp(int value) => Apply(target, $"css_hp {target.Selector} {value}");
        var rows = new List<ShortcutRow>();
        foreach (var value in new[] { 1, 50, 100 }) rows.Add(new($"{value} HP", () => SetHp(value)));
        rows.Add(new("自定义血量", () => Number("修改当前血量", target.Label, 1, value => { SetHp(value); Back(); })));
        return new("修改当前血量", target.Label + " · 选择新的当前血量", rows);
    }

    private ShortcutPage HealthCap()
    {
        var limits = _host.HpCap();
        if (limits == null) return new("血量上下限", "规则服务暂不可用", []);
        var current = limits.Enabled ? $"当前下限 {limits.Min}，上限 {limits.Max}" : "当前已关闭限制";
        return new("血量上下限", current + " · 限制玩法模块扣血和回血", [
            new("设置下限与上限", () => Number("输入血量下限", current, 0, min =>
                Number("输入血量上限", $"已选下限 {min}", Math.Max(1, min), max =>
                {
                    _host.Run($"css_hpcap {min} {max}");
                    Back();
                    Back();
                }))),
            new("关闭限制", limits.Enabled ? () => { _host.Run("css_hpcap 0"); Refresh(); } : null)
        ]);
    }

    private void Number(string title, string context, int minimum, Action<int> accept)
    {
        var field = new ShortcutNumber(minimum, accept);
        Open(() => new(title, $"{context} · 当前输入：{(field.Digits.Length == 0 ? "—" : field.Digits)}", [], field));
    }

    internal void Key(string key)
    {
        if (Page.Number is not { } field) return;
        if (key == "apply")
        {
            if (int.TryParse(field.Digits, out var value) && value >= field.Minimum) field.Accept(value);
            else _host.Notify($"请输入不小于 {field.Minimum} 的整数。");
        }
        else if (key == "erase")
        {
            if (field.Digits.Length > 0) field.Digits = field.Digits[..^1];
        }
        else if (key.Length == 1 && char.IsAsciiDigit(key[0]))
        {
            if (int.TryParse(field.Digits + key, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                field.Digits = number.ToString(CultureInfo.InvariantCulture);
            else _host.Notify("数字太大，请退格后重新输入。");
        }
        Refresh();
    }

    internal bool RefreshMatchIfChanged()
    {
        if (_build != Match) return false;
        var match = _host.Match();
        if (match?.Stamp == _lastMatch?.Stamp && match?.CanPause == _lastMatch?.CanPause &&
            match?.IsPaused == _lastMatch?.IsPaused && match?.Status == _lastMatch?.Status) return false;
        Page = MatchPage(match);
        return true;
    }

    private ShortcutPage Match() => MatchPage(_host.Match());

    private ShortcutPage MatchPage(MatchAdminState? match)
    {
        _lastMatch = match;
        var paused = match?.IsPaused == true;
        return new("比赛与规则", match?.Status ?? "比赛控制暂不可用", [
            new(paused ? "继续比赛" : "暂停比赛", match?.CanPause == true ? () =>
            {
                var action = paused ? MatchAdminAction.Resume : MatchAdminAction.Pause;
                _host.Pause(new(action, match.Stamp));
                Refresh();
            } : null),
            new("查看当前规则", () => Open(() =>
            {
                var rules = _host.Rules();
                return new("当前规则", rules.Count == 0 ? "当前使用默认竞技规则" : "已启用的特殊规则",
                    rules.Select(rule => new ShortcutRow(rule, Hint: rule)).ToArray());
            })),
            new("恢复默认规则", () => Open(() => new("恢复默认规则", "将重置全服玩法修改，是否继续？", [
                new("取消", () => Back()),
                new("确认恢复默认规则", () => { Back(); _host.Run("css_reset_plu"); })
            ])))
        ]);
    }
}
