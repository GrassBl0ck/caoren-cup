using CaorenCup.Contracts;
using CaorenCup.Features.InGameMenu;
using CaorenCup.Gamemodes.Random;
using Xunit;
using System.Xml.Linq;

namespace CaorenCup.GamePlugin.Tests;
public sealed class RandomMenuTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Purchase_feedback_only_closes_after_success(bool success)
    {
        var events = new List<string>();
        var result = new GameModeResult(success, success ? "升级成功" : "页面已更新，请重新选择。");
        var closed = RandomMenuPresentation.FinishPurchase(result,
            message => events.Add("notify:" + message), () => events.Add("close"));
        Assert.Equal(success, closed);
        Assert.Equal("notify:" + result.Message, events[0]);
        Assert.Equal(success ? 2 : 1, events.Count);
        if (success) Assert.Equal("close", events[1]);
    }

    [Fact]
    public void Description_toggle_selects_the_complete_card_text_without_changing_levels()
    {
        var card = RandomCatalog.Cards[0].View(2);
        Assert.Equal(card.SimpleDescription, RandomMenuPresentation.CardDescription(card, false));
        Assert.Equal(card.DetailedDescription, RandomMenuPresentation.CardDescription(card, true));
        Assert.Equal(2, card.Level);
        Assert.DoesNotContain("升级：", RandomMenuPresentation.CardDescription(card, false));
    }

    [Theory]
    [InlineData(1, "口袋急救包")]
    [InlineData(2, "口袋急救包-2级")]
    [InlineData(3, "口袋急救包-3级")]
    public void Card_title_only_adds_the_current_or_target_level_after_the_base_level(int level, string expected)
    {
        Assert.Equal(expected, RandomMenuPresentation.CardTitle(RandomCatalog.Cards[1].View(level)));
    }

    [Theory]
    [InlineData("进攻", "attack")]
    [InlineData("防御", "defense")]
    [InlineData("机动", "mobility")]
    [InlineData("战术", "tactics")]
    [InlineData("补给", "supply")]
    public void Categories_map_to_the_approved_icon_names(string category, string icon) =>
        Assert.Equal(icon, RandomMenuPresentation.CategoryIcon(category));

    [Fact]
    public void Player_layout_has_three_column_cards_and_four_footer_groups()
    {
        var root = FindResourceRoot();
        var document = XDocument.Load(Path.Combine(root, "layout", "custom_game", "caoren_player_menu.vxml"));
        XElement ById(string id) => document.Descendants().Single(e => (string?)e.Attribute("id") == id);
        var list = ById("random_list");
        Assert.Equal(3, list.Elements("Panel").Count(e => ((string?)e.Attribute("id"))?.StartsWith("random_buy_") == true));
        for (var i = 0; i < 3; i++)
        {
            var host = ById("random_buy_" + i + "_host");
            var card = ById("random_buy_" + i);
            Assert.Equal(list, host.Parent);
            Assert.Equal(host, card.Parent);
            Assert.Equal("Button", card.Name.LocalName);
            Assert.Empty(card.Descendants("Button"));
            var corners = card.Elements("Panel").Single(e => (string?)e.Attribute("class") == "CaorenRandomCardCorners");
            Assert.All(corners.Descendants("Label"), label =>
                Assert.Equal("CaorenRandomIconHintText", (string?)label.Attribute("class")));
            foreach (var icon in new[] { "attack", "defense", "mobility", "tactics", "supply", "personal", "team" })
                Assert.Equal("s2r://panorama/images/custom_game/random_" + icon + ".vsvg", (string?)ById("random_card_" + i + "_" + icon).Element("Image")?.Attribute("src"));
            Assert.Contains(card.Descendants("Panel"), e => (string?)e.Attribute("class") == "CaorenRandomCardDescriptionScroll");
            Assert.DoesNotContain(card.Descendants("Label"), e => ((string?)e.Attribute("text"))?.Contains("random_card_meta") == true || ((string?)e.Attribute("text"))?.Contains("random_card_level") == true);
        }
        var footer = ById("random_footer");
        Assert.Equal(4, footer.Elements("Panel").Count());
        foreach (var id in new[] { "random_owned", "random_reroll", "random_exchange", "random_toggle" })
            Assert.Contains(footer, ById(id).Ancestors());
        Assert.Contains("Hidden", (string?)ById("random_collection_nav").Attribute("class") ?? "");
        Assert.Equal("返回三选一", (string?)ById("random_offers").Element("Label")?.Attribute("text"));
        var style = File.ReadAllText(Path.Combine(root, "styles", "custom_game", "caoren_player_menu.vcss"));
        Assert.Contains(".CaorenRandomList { width: 100%; height: 520px; flow-children: right; }", style);
        Assert.Contains("overflow: squish scroll", style);
        Assert.DoesNotContain("text-overflow: ellipsis", style);
        foreach (var quality in RandomMenuPresentation.QualityCodes)
        {
            Assert.Contains(".CaorenRandomCard.RandomQuality" + quality + " { background-color:", style);
            Assert.Contains(".CaorenRandomDetail.RandomQuality" + quality + " { background-color:", style);
        }
        Assert.DoesNotContain(ById("random_detail_panel").Descendants("Label"), e => ((string?)e.Attribute("text"))?.Contains("random_detail_quality") == true);
        Assert.Contains("width: 24px; height: 24px", style);
        Assert.Contains("width: 20px; height: 20px", style);
        foreach (var prefix in new[] { "random_card_0", "random_card_1", "random_card_2", "random_detail" })
        foreach (var (icon, label) in new[] { ("attack", "进攻"), ("defense", "防御"), ("mobility", "机动"), ("tactics", "战术"), ("supply", "补给"), ("personal", "个人"), ("team", "团队") })
        {
            var host = ById(prefix + "_" + icon);
            Assert.Equal("false", (string?)host.Attribute("hittest"));
            Assert.Empty(host.Descendants("Button"));
            Assert.Equal("true", (string?)host.Element("Image")?.Attribute("hittest"));
            var hint = host.Element("Panel")!;
            Assert.Equal("CaorenRandomIconHint", (string?)hint.Attribute("class"));
            Assert.Equal("false", (string?)hint.Attribute("hittest"));
            // CustomHud rejects hittestchildren even though resourcecompiler accepts it.
            Assert.Null(hint.Attribute("hittestchildren"));
            Assert.Equal(label, (string?)hint.Element("Label")?.Attribute("text"));
        }
        Assert.Contains(".CaorenRandomIconHost { width: 64px; height: 64px;", style);
        Assert.Contains(".CaorenRandomIconHint { opacity: 0; width: 64px; height: 32px;", style);
        Assert.Contains(".CaorenRandomIconHost:hover .CaorenRandomIconHint { opacity: 1; }", style);
        Assert.DoesNotContain("CaorenRandomIconHoverTarget", style);
        Assert.DoesNotContain("TEMPORARY bounded rendering probe", style);
        Assert.DoesNotContain("Temporary local diagnostic", style);
    }

    [Fact]
    public void Silhouettes_use_path_fill_and_curves_supported_by_the_cs2_renderer()
    {
        var root = FindResourceRoot();
        foreach (var icon in new[] { "attack", "defense", "mobility", "tactics", "supply", "personal", "team" })
        {
            var svg = XDocument.Load(Path.Combine(root, "images", "custom_game", "random_" + icon + ".svg"));
            var paths = svg.Descendants().Where(e => e.Name.LocalName == "path").ToList();
            Assert.NotEmpty(paths);
            foreach (var path in paths)
            {
                // resourcecompiler accepts Q, but the actual CS2 renderer rejects it.
                Assert.DoesNotMatch("[Qq]", (string?)path.Attribute("d") ?? "");
                Assert.Equal("#e6e6e6", (string?)path.Attribute("fill"));
            }
        }
    }

    private static string FindResourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "game-plugin", "Features", "InGameMenu", "resources", "panorama");
            if (Directory.Exists(path)) return path;
        }
        throw new DirectoryNotFoundException("找不到菜单资源目录。");
    }

    [Fact] public void Overview_counts_distinct_buffs_and_contains_zero_categories()
    {
        var cards = new[] { RandomCatalog.Cards[0].View(1), RandomCatalog.Cards[0].View(3), RandomCatalog.Cards[2].View(1) };
        var q=RandomMenuPresentation.CountQualities(cards);var c=RandomMenuPresentation.CountCategories(cards);
        Assert.Equal(5,q.Count);Assert.Equal(5,c.Count);Assert.Equal(2,q["D"]);Assert.Equal(0,q["S"]);
        Assert.Equal(1,c["防御"]);Assert.Equal(1,c["战术"]);Assert.Equal(0,c["机动"]);
    }
    [Fact] public void Details_are_paginated_without_losing_text_or_splitting_unicode()
    {
        var text=string.Concat(Enumerable.Repeat("换弹售后😀",100));
        var pages=RandomMenuPresentation.DescriptionPages(text);
        Assert.True(pages.Count>1);Assert.All(pages,p=>Assert.True(p.Split('\n').Length<=7));
        Assert.Equal(text,string.Concat(pages).Replace("\n",""));
    }
    [Fact] public void Compact_descriptions_show_effects_without_numeric_values_or_upgrade_appendix()
    {
        foreach(var card in RandomCatalog.Cards)
        for (var level = 1; level <= card.MaxLevel; level++)
        {
            var view = card.View(level);
            Assert.DoesNotMatch("[0-9]", view.SimpleDescription);
            Assert.DoesNotContain("升级：", view.SimpleDescription);
        }
    }
}
