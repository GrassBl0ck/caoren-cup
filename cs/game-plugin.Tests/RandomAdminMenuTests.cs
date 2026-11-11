using CaorenCup.Contracts;
using CaorenCup.Features.InGameMenu;
using CaorenCup.Gamemodes.Random;
using System.Xml.Linq;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class RandomAdminMenuTests
{
    [Fact]
    public void Background_updates_do_not_reassign_visible_slots_when_players_join_leave_or_new_buffs_arrive()
    {
        var shown = RandomAdminMenuPresentation.PageKeys(["A", "B", "C", "D", "E", "F", "G"], [], 0, false);
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F" }, shown);
        var updated = RandomAdminMenuPresentation.PageKeys(["NEW", "B", "C", "D", "E", "F", "G"], shown, 0, true);
        Assert.Equal(shown, updated); // A disappears: its slot must hide rather than point at NEW or B.
        Assert.Equal(new[] { "G" }, RandomAdminMenuPresentation.PageKeys(["NEW", "B", "C", "D", "E", "F", "G"], updated, 1, false));
        Assert.Equal(new[] { "NEW", "B", "C", "D", "E", "F" }, RandomAdminMenuPresentation.PageKeys(["NEW", "B", "C", "D", "E", "F", "G"], updated, 0, false));
    }

    [Fact]
    public void Player_status_distinguishes_retained_disconnect_and_spectating_without_losing_coupons()
    {
        var player = new RandomAdminPlayerView("123", "测试玩家", 2, false, true, 7, true, false, true,
            [RandomCatalog.Cards[0].View(1), RandomCatalog.Cards[0].View(2)]);
        Assert.Equal("离线 · T · 本局参赛", RandomAdminMenuPresentation.Presence(player));
        Assert.Equal("测试玩家 · 离线 · 强化券 7 · BUFF 1", RandomAdminMenuPresentation.PlayerRow(player));
        Assert.Equal("本回合：购买已用 · 重抽未用 · 兑换已用", RandomAdminMenuPresentation.RoundUsage(player));
        Assert.Equal("在线 · 旁观 / 未分队 · 未参赛", RandomAdminMenuPresentation.Presence(player with { Team = 1, Connected = true, Participating = false }));
    }

    [Fact]
    public void Admin_random_screen_contains_only_read_only_navigation_and_complete_button_hosts()
    {
        var root = FindRoot();
        var xml = XDocument.Load(Path.Combine(root, "cs/game-plugin/Features/InGameMenu/resources/panorama/layout/custom_game/caoren_admin_menu.vxml"));
        var section = xml.Descendants("Panel").Single(panel => (string?)panel.Attribute("id") == "admin_random");
        var buttons = section.Descendants("Button").ToArray();
        Assert.Equal(10, buttons.Length);
        foreach (var button in buttons)
        {
            var id = (string)button.Attribute("id")!;
            Assert.True(id.StartsWith("admin_random_row_", StringComparison.Ordinal) || id is "admin_random_prev" or "admin_random_next" or "admin_random_back" or "admin_random_refresh");
            Assert.Equal(id + "_host", (string?)button.Parent!.Attribute("id"));
            Assert.Single(button.Parent.Elements("Panel"), panel => ((string?)panel.Attribute("class"))?.Contains("CaorenHoverHint") == true);
        }
        var source = File.ReadAllText(Path.Combine(root, "cs/game-plugin/PluginSplit/CaorenCupInGameMenu/RandomAdminMenuFeature.cs"));
        Assert.Contains("GetAdminView(player)", source);
        Assert.DoesNotContain(".GetView(", source);
        Assert.DoesNotContain(".Purchase(", source);
        Assert.DoesNotContain(".Reroll(", source);
        Assert.DoesNotContain(".Exchange(", source);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "scripts/server-package-manifest.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find Random task worktree.");
    }
}
