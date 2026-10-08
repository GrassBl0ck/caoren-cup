using System;
using System.Collections.Generic;
using System.Linq;

namespace CaorenCup.Features.InGameMenu;

/// <summary>菜单项:一个功能槽位。回调与可见性只依赖 steamId + 权限快照,便于单元测试。</summary>
public sealed class InGameMenuItem
{
    /// <summary>按钮静态文字;与 <see cref="TitleProvider"/> 二选一,动态优先。</summary>
    public string Title = "";

    /// <summary>动态按钮文字(参数为是否 root),返回空串时该项不占槽位。</summary>
    public Func<bool, string>? TitleProvider;

    /// <summary>点击回调;返回值表示点击后需要重新渲染(导航/状态变化)。</summary>
    public Func<string, bool, bool>? OnClick;

    /// <summary>可见性过滤(参数为是否 root);null 表示总是可见。</summary>
    public Func<bool, bool>? IsVisible;

    /// <summary>非空时点击先生成确认子页,展示该文案,确认后才执行 <see cref="OnClick"/>。</summary>
    public string? ConfirmText;
}

/// <summary>菜单页:固定 6 个功能槽 + 4 个导航钮(上一页/下一页/返回/关闭),超过 6 项自动分页。</summary>
public sealed class InGameMenuPage
{
    public string Id = "";
    public string Title = "";

    /// <summary>状态行动态内容(参数为是否 root);与 <see cref="StaticStatus"/> 二选一。</summary>
    public Func<bool, string>? StatusProvider;

    public string? StaticStatus;

    public List<InGameMenuItem> Items = new();

    /// <summary>nav_back 的目标页;null 表示返回即关闭菜单。</summary>
    public string? ParentId;
}

/// <summary>一次渲染的完整结果,由实体层逐变量下发到客户端。</summary>
public sealed class MenuRenderResult
{
    public string Title = "";
    public string Status = "";
    public string PageInfo = "";
    public string PrevLabel = "";
    public string NextLabel = "";
    public string BackLabel = "";
    public string CloseLabel = "关闭";
    public IReadOnlyList<string> SlotTitles = Array.Empty<string>();
}

/// <summary>每玩家会话状态。</summary>
public sealed class MenuSession
{
    public string PageId = "";
    public int PageIndex;
}

/// <summary>
/// 纯逻辑菜单导航器:会话管理、可见性过滤、分页切片、点击路由与确认页。
/// 不依赖 CounterStrikeSharp 类型,由宿主(Feature)负责实体渲染与真实玩家查找。
/// </summary>
public sealed class MenuNavigator
{
    public const int SlotsPerPage = 6;

    private readonly Dictionary<string, InGameMenuPage> _pages = new();
    private readonly Dictionary<string, MenuSession> _sessions = new();
    private readonly Dictionary<string, InGameMenuPage> _dynamicPages = new();
    private int _confirmSeq;

    public IEnumerable<string> RegisteredPageIds => _pages.Keys.Concat(_dynamicPages.Keys);

    /// <summary>注册静态页;同 Id 覆盖(页面目录重新构建时使用)。</summary>
    public void RegisterPage(InGameMenuPage page) => _pages[page.Id] = page;

    public bool HasSession(string steamId) => _sessions.ContainsKey(steamId);

    public IReadOnlyDictionary<string, MenuSession> Sessions => _sessions;

    /// <summary>打开(或跳转到)某页。返回是否成功。</summary>
    public bool Open(string steamId, string pageId)
    {
        if (FindPage(pageId) == null) return false;
        _sessions[steamId] = new MenuSession { PageId = pageId, PageIndex = 0 };
        return true;
    }

    public void Close(string steamId) => _sessions.Remove(steamId);

    public void CloseAll() => _sessions.Clear();

    /// <summary>构建某玩家当前页的渲染数据。</summary>
    public MenuRenderResult? BuildRender(string steamId, bool isRoot)
    {
        if (!_sessions.TryGetValue(steamId, out var session)) return null;
        var page = FindPage(session.PageId);
        if (page == null)
        {
            _sessions.Remove(steamId);
            return null;
        }

        var visible = VisibleItems(page, isRoot).ToList();
        var pageCount = Math.Max(1, (visible.Count + SlotsPerPage - 1) / SlotsPerPage);
        session.PageIndex = Math.Clamp(session.PageIndex, 0, pageCount - 1);

        var slice = visible.Skip(session.PageIndex * SlotsPerPage).Take(SlotsPerPage).ToList();
        var slots = new List<string>(SlotsPerPage);
        for (var i = 0; i < SlotsPerPage; i++)
        {
            slots.Add(i < slice.Count ? ResolveTitle(slice[i], isRoot) : string.Empty);
        }

        return new MenuRenderResult
        {
            Title = page.Title,
            Status = page.StatusProvider != null ? page.StatusProvider(isRoot) : page.StaticStatus ?? string.Empty,
            PageInfo = visible.Count > SlotsPerPage ? $"{session.PageIndex + 1}/{pageCount} 页 · 共 {visible.Count} 项" : string.Empty,
            PrevLabel = session.PageIndex > 0 ? "上一页" : "—",
            NextLabel = session.PageIndex < pageCount - 1 ? "下一页" : "—",
            BackLabel = page.ParentId != null ? "返回" : "关闭",
            SlotTitles = slots,
        };
    }

    /// <summary>
    /// 点击路由。返回点击后需要的动作:None=无(重渲染即可)、Close=关闭菜单、Invalid=非本菜单交互。
    /// </summary>
    public MenuClickAction HandleClick(string steamId, string buttonId, bool isRoot)
    {
        if (!_sessions.TryGetValue(steamId, out var session)) return MenuClickAction.Invalid;
        var page = FindPage(session.PageId);
        if (page == null)
        {
            _sessions.Remove(steamId);
            return MenuClickAction.Close;
        }

        switch (buttonId)
        {
            case "nav_close":
                _sessions.Remove(steamId);
                return MenuClickAction.Close;
            case "nav_back":
                if (page.ParentId == null)
                {
                    _sessions.Remove(steamId);
                    return MenuClickAction.Close;
                }
                session.PageId = page.ParentId;
                session.PageIndex = 0;
                return MenuClickAction.Rerender;
            case "nav_prev":
                session.PageIndex = Math.Max(0, session.PageIndex - 1);
                return MenuClickAction.Rerender;
            case "nav_next":
            {
                var pageCount = Math.Max(1, (VisibleItems(page, isRoot).Count() + SlotsPerPage - 1) / SlotsPerPage);
                session.PageIndex = Math.Min(pageCount - 1, session.PageIndex + 1);
                return MenuClickAction.Rerender;
            }
        }

        if (buttonId.StartsWith("menu_btn_", StringComparison.Ordinal))
        {
            var visible = VisibleItems(page, isRoot).ToList();
            var slice = visible.Skip(session.PageIndex * SlotsPerPage).Take(SlotsPerPage).ToList();
            if (!int.TryParse(buttonId.AsSpan("menu_btn_".Length), out var slot) || slot < 0 || slot >= slice.Count)
            {
                return MenuClickAction.Rerender;
            }

            var item = slice[slot];
            if (item.ConfirmText != null)
            {
                OpenConfirmPage(steamId, item, page);
                return MenuClickAction.Rerender;
            }

            var rerender = item.OnClick?.Invoke(steamId, isRoot) ?? false;
            return rerender ? MenuClickAction.Rerender : MenuClickAction.None;
        }

        return MenuClickAction.Invalid;
    }

    private void OpenConfirmPage(string steamId, InGameMenuItem item, InGameMenuPage parent)
    {
        var pageId = $"#confirm:{_confirmSeq++}";
        _dynamicPages[pageId] = new InGameMenuPage
        {
            Id = pageId,
            Title = "操作确认",
            StaticStatus = item.ConfirmText,
            ParentId = parent.Id,
            Items =
            {
                new InGameMenuItem
                {
                    Title = "确认执行",
                    OnClick = (sid, root) =>
                    {
                        var keep = item.OnClick?.Invoke(sid, root) ?? false;
                        _sessions.TryGetValue(sid, out var s);
                        if (s != null)
                        {
                            s.PageId = parent.Id;
                            s.PageIndex = 0;
                        }
                        _dynamicPages.Remove(pageId);
                        return true;
                    },
                },
                new InGameMenuItem
                {
                    Title = "取消",
                    OnClick = (sid, _) =>
                    {
                        if (_sessions.TryGetValue(sid, out var s))
                        {
                            s.PageId = parent.Id;
                            s.PageIndex = 0;
                        }
                        _dynamicPages.Remove(pageId);
                        return true;
                    },
                },
            },
        };

        if (_sessions.TryGetValue(steamId, out var session))
        {
            session.PageId = pageId;
            session.PageIndex = 0;
        }
    }

    private InGameMenuPage? FindPage(string pageId) =>
        _pages.TryGetValue(pageId, out var page) ? page
        : _dynamicPages.TryGetValue(pageId, out var dyn) ? dyn
        : null;

    private static IEnumerable<InGameMenuItem> VisibleItems(InGameMenuPage page, bool isRoot) =>
        page.Items.Where(i => i.IsVisible == null || i.IsVisible(isRoot));

    private static string ResolveTitle(InGameMenuItem item, bool isRoot) =>
        item.TitleProvider != null ? item.TitleProvider(isRoot) : item.Title;
}

public enum MenuClickAction
{
    /// <summary>点击已处理且状态未变,无需重渲染。</summary>
    None,

    /// <summary>需要重新渲染当前页(导航或内容变化)。</summary>
    Rerender,

    /// <summary>关闭该玩家的菜单。</summary>
    Close,

    /// <summary>无效交互(非本菜单按钮或无会话),忽略。</summary>
    Invalid,
}
