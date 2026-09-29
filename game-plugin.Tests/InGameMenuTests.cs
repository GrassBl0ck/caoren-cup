using System.Collections.Generic;
using System.Linq;
using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

/// <summary>IMenuHostActions 假实现:记录命令与聊天接力,便于断言。</summary>
internal sealed class FakeMenuHost : IMenuHostActions
{
    public List<string> PlayerCommands = new();
    public List<string> ServerCommands = new();
    public List<string> Prompts = new();
    public List<string> ChatMessages = new();
    public List<System.Action<string>> RelayCallbacks = new();

    public void ExecAsPlayer(string steamId, string command) => PlayerCommands.Add(command);
    public void ExecAsServer(string command) => ServerCommands.Add(command);

    public void BeginChatRelay(string steamId, string prompt, System.Action<string> onText)
    {
        Prompts.Add(prompt);
        RelayCallbacks.Add(onText);
    }

    public void Chat(string steamId, string message) => ChatMessages.Add(message);
}

public sealed class InGameMenuTests
{
    private const string Sid = "76561198000000001";

    private static MenuNavigator BuildNavigator() => new();

    private static MenuNavigator BuildCatalogNavigator(FakeMenuHost host)
    {
        var navigator = new MenuNavigator();
        new AdminMenuCatalog(host, navigator).RegisterAll();
        return navigator;
    }

    // === 基础导航 ===

    [Fact]
    public void Open_unknown_page_fails()
    {
        var nav = BuildNavigator();
        Assert.False(nav.Open(Sid, "nope"));
    }

    [Fact]
    public void Render_shows_title_and_filled_slots()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "标题",
            StaticStatus = "状态行",
            Items =
            {
                new InGameMenuItem { Title = "A" },
                new InGameMenuItem { Title = "B" },
            },
        });
        Assert.True(nav.Open(Sid, "p"));

        var render = nav.BuildRender(Sid, isRoot: true);
        Assert.NotNull(render);
        Assert.Equal("标题", render!.Title);
        Assert.Equal("状态行", render.Status);
        Assert.Equal("A", render.SlotTitles[0]);
        Assert.Equal("B", render.SlotTitles[1]);
        Assert.All(render.SlotTitles.Skip(2), t => Assert.Equal(string.Empty, t));
        Assert.Equal(string.Empty, render.PageInfo);
        Assert.Equal("—", render.PrevLabel);
        Assert.Equal("—", render.NextLabel);
        Assert.Equal("关闭", render.BackLabel);
    }

    [Fact]
    public void Render_paginates_when_more_than_six_items()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "t",
            Items = Enumerable.Range(0, 13).Select(i => new InGameMenuItem { Title = "项" + i }).ToList(),
        });
        nav.Open(Sid, "p");

        var render = nav.BuildRender(Sid, isRoot: false);
        Assert.Equal("1/3 页 · 共 13 项", render!.PageInfo);
        Assert.Equal("—", render.PrevLabel);
        Assert.Equal("下一页", render.NextLabel);
        Assert.Equal(6, render.SlotTitles.Count(t => t.Length > 0));
    }

    [Fact]
    public void Nav_next_and_prev_move_page()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "t",
            Items = Enumerable.Range(0, 8).Select(i => new InGameMenuItem { Title = "项" + i }).ToList(),
        });
        nav.Open(Sid, "p");
        nav.BuildRender(Sid, false);

        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "nav_next", false));
        var page2 = nav.BuildRender(Sid, false);
        Assert.Equal("项6", page2!.SlotTitles[0]);
        Assert.Equal("上一页", page2.PrevLabel);
        Assert.Equal("—", page2.NextLabel);

        // 已在最后一页再点下一页,保持不动
        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "nav_next", false));
        Assert.Equal("项6", nav.BuildRender(Sid, false)!.SlotTitles[0]);

        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "nav_prev", false));
        Assert.Equal("项0", nav.BuildRender(Sid, false)!.SlotTitles[0]);
    }

    // === 槽位点击与回调 ===

    [Fact]
    public void Slot_click_dispatches_callback_with_identity()
    {
        var nav = BuildNavigator();
        string? gotSid = null;
        bool? gotRoot = null;
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "t",
            Items = { new InGameMenuItem { Title = "X", OnClick = (sid, root) => { gotSid = sid; gotRoot = root; return false; } } },
        });
        nav.Open(Sid, "p");

        Assert.Equal(MenuClickAction.None, nav.HandleClick(Sid, "menu_btn_0", isRoot: true));
        Assert.Equal(Sid, gotSid);
        Assert.True(gotRoot);
    }

    [Fact]
    public void Empty_or_unknown_slot_click_is_safe()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage { Id = "p", Title = "t", Items = { new InGameMenuItem { Title = "X" } } });
        nav.Open(Sid, "p");

        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "menu_btn_5", false));
        Assert.Equal(MenuClickAction.Invalid, nav.HandleClick(Sid, "whatever", false));
    }

    // === 确认页 ===

    [Fact]
    public void Confirm_item_requires_explicit_confirmation()
    {
        var nav = BuildNavigator();
        var executed = 0;
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "t",
            Items =
            {
                new InGameMenuItem
                {
                    Title = "危险操作",
                    ConfirmText = "确认吗",
                    OnClick = (_, _) => { executed++; return true; },
                },
            },
        });
        nav.Open(Sid, "p");

        // 第一次点击进入确认页,不执行
        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "menu_btn_0", false));
        var confirmRender = nav.BuildRender(Sid, false);
        Assert.Equal("操作确认", confirmRender!.Title);
        Assert.Equal("确认吗", confirmRender.Status);
        Assert.Equal("确认执行", confirmRender.SlotTitles[0]);
        Assert.Equal("取消", confirmRender.SlotTitles[1]);
        Assert.Equal(0, executed);

        // 确认执行:原回调触发并返回父页
        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "menu_btn_0", false));
        Assert.Equal(1, executed);
        Assert.Equal("t", nav.BuildRender(Sid, false)!.Title);
    }

    [Fact]
    public void Confirm_cancel_returns_without_executing()
    {
        var nav = BuildNavigator();
        var executed = 0;
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "t",
            Items =
            {
                new InGameMenuItem { Title = "危险", ConfirmText = "确认吗", OnClick = (_, _) => { executed++; return true; } },
            },
        });
        nav.Open(Sid, "p");

        nav.HandleClick(Sid, "menu_btn_0", false);      // 进确认页
        nav.HandleClick(Sid, "menu_btn_1", false);      // 取消
        Assert.Equal(0, executed);
        Assert.Equal("t", nav.BuildRender(Sid, false)!.Title);
    }

    // === 可见性与返回 ===

    [Fact]
    public void Invisible_items_do_not_take_slots()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage
        {
            Id = "p",
            Title = "t",
            Items =
            {
                new InGameMenuItem { Title = "A" },
                new InGameMenuItem { Title = "仅root", IsVisible = root => root },
                new InGameMenuItem { Title = "B" },
            },
        });
        nav.Open(Sid, "p");

        var nonRoot = nav.BuildRender(Sid, isRoot: false)!;
        Assert.Equal("A", nonRoot.SlotTitles[0]);
        Assert.Equal("B", nonRoot.SlotTitles[1]);

        var root = nav.BuildRender(Sid, isRoot: true)!;
        Assert.Equal("仅root", root.SlotTitles[1]);
    }

    [Fact]
    public void Back_returns_to_parent_and_root_back_closes()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage { Id = "root", Title = "根", Items = { new InGameMenuItem { Title = "去子页", OnClick = (sid, _) => nav.Open(sid, "child") } } });
        nav.RegisterPage(new InGameMenuPage { Id = "child", Title = "子", ParentId = "root" });
        nav.Open(Sid, "root");

        nav.HandleClick(Sid, "menu_btn_0", false);
        Assert.Equal("子", nav.BuildRender(Sid, false)!.Title);
        Assert.Equal("返回", nav.BuildRender(Sid, false)!.BackLabel);

        Assert.Equal(MenuClickAction.Rerender, nav.HandleClick(Sid, "nav_back", false));
        Assert.Equal("根", nav.BuildRender(Sid, false)!.Title);

        Assert.Equal(MenuClickAction.Close, nav.HandleClick(Sid, "nav_back", false));
        Assert.False(nav.HasSession(Sid));
        Assert.Null(nav.BuildRender(Sid, false));
    }

    [Fact]
    public void Close_button_ends_session()
    {
        var nav = BuildNavigator();
        nav.RegisterPage(new InGameMenuPage { Id = "p", Title = "t" });
        nav.Open(Sid, "p");

        Assert.Equal(MenuClickAction.Close, nav.HandleClick(Sid, "nav_close", false));
        Assert.False(nav.HasSession(Sid));
    }

    // === 页面目录(AdminMenuCatalog) ===

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Minimal_admin_page_executes_fixed_command_as_server_only_with_permission(bool isRoot)
    {
        var host = new FakeMenuHost();
        var nav = new MenuNavigator();
        new AdminMenuCatalog(host, nav).RegisterMinimalAdminPage();
        nav.Open(Sid, "root");

        // 模拟打开后点击；权限撤销时不可执行，即使仍有会话。
        nav.HandleClick(Sid, "menu_btn_0", isRoot);

        Assert.Empty(host.PlayerCommands);
        if (isRoot) Assert.Single(host.ServerCommands, "css_say 1");
        else Assert.Empty(host.ServerCommands);
        Assert.Equal(!isRoot, nav.HasSession(Sid));
    }

    [Fact]
    public void Catalog_registers_all_first_version_pages()
    {
        var nav = BuildCatalogNavigator(new FakeMenuHost());
        var pages = nav.RegisteredPageIds.ToHashSet();
        foreach (var id in new[] { "root", "global", "duel", "duel_nades", "duel_time", "matchzy", "mz_ready" })
        {
            Assert.Contains(id, pages);
        }
    }

    [Fact]
    public void Catalog_root_navigates_to_each_section()
    {
        var nav = BuildCatalogNavigator(new FakeMenuHost());
        nav.Open(Sid, "root");

        nav.HandleClick(Sid, "menu_btn_1", true); // 单挑控制
        Assert.Equal("单挑控制", nav.BuildRender(Sid, true)!.Title);

        nav.HandleClick(Sid, "nav_back", true);
        nav.HandleClick(Sid, "menu_btn_2", true); // MatchZy
        Assert.Equal("MatchZy 比赛", nav.BuildRender(Sid, true)!.Title);
    }

    [Fact]
    public void Catalog_player_cmd_items_execute_through_host()
    {
        var host = new FakeMenuHost();
        var nav = BuildCatalogNavigator(host);
        nav.Open(Sid, "global");

        // 槽 0 = 查看全局状态
        nav.HandleClick(Sid, "menu_btn_0", true);
        Assert.Single(host.PlayerCommands, "css_status");

        // 有确认的"一键重置":槽 2
        nav.HandleClick(Sid, "menu_btn_2", true);
        Assert.Equal("操作确认", nav.BuildRender(Sid, true)!.Title);
        nav.HandleClick(Sid, "menu_btn_0", true);
        Assert.Contains("css_reset_plu", host.PlayerCommands);
    }

    [Fact]
    public void Catalog_duel_pages_execute_commands_and_subpages()
    {
        var host = new FakeMenuHost();
        var nav = BuildCatalogNavigator(host);
        nav.Open(Sid, "duel");

        nav.HandleClick(Sid, "menu_btn_0", true); // 查看单挑状态
        Assert.Contains("css_duel status", host.PlayerCommands);

        // 槽 6 超出本页槽位(仅 0-5),应被安全忽略
        nav.HandleClick(Sid, "menu_btn_6", true);
        Assert.Equal("单挑控制", nav.BuildRender(Sid, true)!.Title);

        // duel 页 9 项,道具模式是第 7 项:翻到第 2 页再点第 1 槽
        nav.HandleClick(Sid, "nav_next", true);
        nav.HandleClick(Sid, "menu_btn_0", true);
        Assert.Equal("单挑 · 道具模式", nav.BuildRender(Sid, true)!.Title);
        nav.HandleClick(Sid, "menu_btn_4", true); // 全道具
        Assert.Contains("css_duel nades full", host.PlayerCommands);
    }

    [Fact]
    public void Catalog_matchzy_asay_uses_chat_relay()
    {
        var host = new FakeMenuHost();
        var nav = BuildCatalogNavigator(host);
        nav.Open(Sid, "matchzy");

        // 13 项,asay 是最后一页第 1 项(index 12):翻两页
        nav.HandleClick(Sid, "nav_next", true);
        nav.HandleClick(Sid, "nav_next", true);
        nav.HandleClick(Sid, "menu_btn_0", true);

        Assert.Single(host.Prompts);
        host.RelayCallbacks[0]("全体注意 最后一把");
        Assert.Contains("css_asay 全体注意 最后一把", host.PlayerCommands);
    }

    [Fact]
    public void Catalog_global_info_cast_uses_chat_relay()
    {
        var host = new FakeMenuHost();
        var nav = BuildCatalogNavigator(host);
        nav.Open(Sid, "global");

        // 9 项,广播玩法说明是第 9 项(index 8):翻一页
        nav.HandleClick(Sid, "nav_next", true);
        nav.HandleClick(Sid, "menu_btn_2", true);

        Assert.Single(host.Prompts);
        host.RelayCallbacks[0]("bq");
        Assert.Contains("css_info_cast bq", host.PlayerCommands);
    }
}
