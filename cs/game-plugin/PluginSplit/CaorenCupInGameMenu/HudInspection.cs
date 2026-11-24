namespace CaorenCup.Features.InGameMenu;

/// <summary>一个玩家的按住查看状态；迟到的松键只关闭自己获得的输入。</summary>
public class HudInspection
{
    public bool Held { get; private set; }
    public bool Expanded { get; private set; }
    public string? DetailKey { get; private set; }

    public void Begin(Action capture)
    {
        if (Held) return;
        capture();
        Held = true;
    }

    public void Release(Action release)
    {
        if (!Held) return;
        release();
        Held = false;
        Expanded = false;
        DetailKey = null;
    }

    public void Select(string id, IReadOnlyDictionary<string, CombatHudItem> items)
    {
        if (!Held) return;
        if (id == "hud_back") DetailKey = null;
        else if (id is "hud_more" or "hud_less") { Expanded = id == "hud_more"; DetailKey = null; }
        else if (items.TryGetValue(id, out var item)) DetailKey = item.Key;
    }

    public void CollapseCollection() => Expanded = false;
    public void ClearDetail() => DetailKey = null;
}
