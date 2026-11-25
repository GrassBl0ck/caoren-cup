using CaorenCup.Contracts;
using CaorenCup.Features.InGameMenu;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using Xunit;

namespace CaorenCup.Tests;

public sealed class AdminShortcutMenuTests
{
    private sealed class Host : IAdminShortcutHost
    {
        public List<string> Commands { get; } = [];
        public List<string> Notices { get; } = [];
        public List<ShortcutPlayer> Online { get; } = [];
        public HpCapSettings Cap { get; } = new() { Enabled = true, Min = 1, Max = 100 };
        public MatchAdminState State = new(new("game", 3, 2, 5), "Live", [], false, true, false, true, []);
        public MatchAdminRequest? LastAction;
        public IReadOnlyList<ShortcutPlayer> Players() => Online;
        public HpCapSettings? HpCap() => Cap;
        public IReadOnlyList<string> Rules() => Cap.Enabled ? ["血量保护"] : [];
        public MatchAdminState Match() => State;
        public void Pause(MatchAdminRequest request)
        {
            LastAction = request;
            State = State with { IsPaused = request.Action == MatchAdminAction.Pause };
        }
        public void Run(string command) => Commands.Add(command);
        public void Notify(string message) => Notices.Add(message);
    }

    [Fact]
    public void Ancestor_jump_and_sibling_switch_only_navigate()
    {
        var host = new Host();
        host.Online.Add(new(17, 9, "张三"));
        var menu = new AdminShortcutMenu(host, ShortcutSection.Equipment);
        menu.OpenOperation(EquipmentAction.Give);
        Click(menu, "指定玩家");
        Assert.Equal(new[] { "玩家与装备", "立即发放物品", "指定玩家" }, menu.PathLabels);
        Click(menu, "张三（#9）");
        Click(menu, "步枪");
        Assert.Equal("张三", menu.PathLabels[^2]);
        Assert.True(menu.BackTo(1));
        Assert.Equal("立即发放物品", menu.Page.Title);
        Assert.False(menu.BackTo(5));
        menu.OpenOperation(EquipmentAction.Health);
        Click(menu, "CT 方全体");
        Click(menu, "自定义血量");
        menu.Key("8");
        menu.OpenOperation(EquipmentAction.Loadout);
        Assert.Equal(new[] { "玩家与装备", "设置出生装备" }, menu.PathLabels);
        Assert.Null(menu.Page.Number);
        Assert.True(menu.Back());
        Assert.Null(menu.Operation);
        Assert.Empty(host.Commands);
    }

    private static void Click(AdminShortcutMenu menu, string label)
    {
        int index = menu.Page.Rows.ToList().FindIndex(row => row.Label == label);
        Assert.True(index >= 0, $"找不到 {label}");
        menu.TurnPage(index / AdminShortcutMenu.Slots - menu.Index);
        menu.Select(index % AdminShortcutMenu.Slots);
    }

    [Theory]
    [InlineData("T 方全体", "@t")]
    [InlineData("CT 方全体", "@ct")]
    [InlineData("所有人", "@all")]
    public void Give_and_set_hp_use_original_commands(string team, string selector)
    {
        var host = new Host();
        var menu = new AdminShortcutMenu(host, ShortcutSection.Equipment);
        Click(menu, "立即发放物品");
        Click(menu, team);
        Click(menu, "狙击枪");
        Click(menu, "AWP");
        for (int i = 0; i < 3; i++) Assert.True(menu.Back());
        Click(menu, "修改当前血量");
        Click(menu, team);
        Click(menu, "50 HP");
        Assert.Equal(new[] { $"css_give {selector} AWP", $"css_hp {selector} 50" }, host.Commands);
    }

    [Fact]
    public void Online_selection_survives_paging_and_does_not_follow_a_reused_userid()
    {
        var host = new Host();
        for (int n = 0; n < 8; n++) host.Online.Add(new((ulong)n + 1, n + 40, "同名玩家"));
        var menu = new AdminShortcutMenu(host, ShortcutSection.Equipment);
        Click(menu, "修改当前血量");
        Click(menu, "指定玩家");
        menu.TurnPage(1);
        menu.Select(1);
        Click(menu, "自定义血量");
        foreach (var key in new[] { "2", "9", "erase", "5", "0", "apply" }) menu.Key(key);
        Assert.Equal("css_hp #47 250", Assert.Single(host.Commands));
        host.Online[7] = new(9999, 47, "同名玩家");
        Click(menu, "100 HP");
        Assert.Single(host.Commands);
        Assert.Contains("重新选择", Assert.Single(host.Notices));
        Assert.True(menu.Back());
        Assert.Equal(1, menu.Index);
    }

    [Theory]
    [InlineData("主武器", "步枪", "M4A1-S", "primary weapon_m4a1_silencer")]
    [InlineData("副武器", null, "USP-S", "secondary weapon_usp_silencer")]
    public void Loadout_uses_the_team_slot_and_entity_name(string slot, string? group, string item, string args)
    {
        var host = new Host();
        var menu = new AdminShortcutMenu(host, ShortcutSection.Equipment);
        Click(menu, "设置出生装备");
        Assert.DoesNotContain(menu.Page.Rows, row => row.Label == "指定玩家");
        Click(menu, "CT 方全体");
        Click(menu, slot);
        if (group != null) Click(menu, group);
        Click(menu, item);
        Assert.Equal("css_loadout weapon ct " + args, Assert.Single(host.Commands));
    }

    [Fact]
    public void Every_item_has_a_give_argument_and_a_real_entity_name()
    {
        var all = ShortcutItems.Groups.SelectMany(group => group.Items).ToArray();
        Assert.Equal(all.Length, all.Select(item => item.EntityName).Distinct().Count());
        foreach (var item in all)
        {
            Assert.StartsWith("weapon_", item.EntityName);
            Assert.True(Enum.TryParse<CsItem>(item.Id.ToString(), out var parsed));
            Assert.Equal(item.Id, parsed);
        }
    }

    [Fact]
    public void Cap_edit_is_atomic_validated_and_cancellable()
    {
        var host = new Host();
        var menu = new AdminShortcutMenu(host, ShortcutSection.HealthCap);
        Click(menu, "设置下限与上限");
        menu.Key("5"); menu.Key("apply");
        menu.Key("1"); menu.Key("apply");
        Assert.Empty(host.Commands);
        Assert.Single(host.Notices);
        Assert.True(menu.Back());
        Assert.Equal("5", menu.Page.Number?.Digits);
        menu.Key("erase"); menu.Key("0"); menu.Key("apply");
        menu.Key("9"); menu.Key("9"); menu.Key("apply");
        Assert.Equal("css_hpcap 0 99", Assert.Single(host.Commands));
        Click(menu, "关闭限制");
        Assert.Equal("css_hpcap 0", host.Commands.Last());
    }

    [Fact]
    public void Rules_and_cancel_are_read_only_and_reset_requires_a_separate_click()
    {
        var host = new Host();
        var menu = new AdminShortcutMenu(host, ShortcutSection.Match);
        Click(menu, "查看当前规则");
        Assert.Equal("血量保护", Assert.Single(menu.Page.Rows).Label);
        host.Cap.Enabled = false;
        menu.Refresh();
        Assert.Equal("当前使用默认竞技规则", menu.Page.Detail);
        menu.Back();
        Click(menu, "恢复默认规则");
        Click(menu, "取消");
        Assert.Empty(host.Commands);
        Click(menu, "恢复默认规则");
        Click(menu, "确认恢复默认规则");
        Assert.Equal("css_reset_plu", Assert.Single(host.Commands));
    }

    [Fact]
    public void Pause_tracks_live_state_and_uses_the_match_stamp()
    {
        var host = new Host();
        var menu = new AdminShortcutMenu(host, ShortcutSection.Match);
        Click(menu, "暂停比赛");
        Assert.Equal(new(MatchAdminAction.Pause, host.State.Stamp), host.LastAction);
        Click(menu, "继续比赛");
        Assert.Equal(MatchAdminAction.Resume, host.LastAction!.Action);
        host.State = host.State with { IsPaused = true, CanPause = false };
        Assert.True(menu.RefreshMatchIfChanged());
        Assert.Null(menu.Page.Rows.Single(row => row.Label == "继续比赛").Select);
        Assert.False(menu.RefreshMatchIfChanged());
    }
}
