using System.Xml.Linq;
using CaorenCup.Contracts;
using CaorenCup.Core;
using CaorenCup.Features.InGameMenu;
using CaorenCup.Gamemodes.Random;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CombatHudTests
{
    [Fact]
    public void Skills_stay_visible_outside_the_six_passive_slots_and_keep_authoritative_status()
    {
        var owned = Enumerable.Range(1, 10).Select(i => Buff("b" + i, i, i < 6 ? "D" : "A")).ToList();
        owned.Add(Buff("smoke_train", 1, "B") with { Active = true });
        var data = new RandomPlayerView(1, 1, 1, false, false, false, "", [], owned)
            { Skills = [new("smoke_train", "烟雾列车", false, "本回合已使用")] };
        var hud = CombatHudContent.FromRandom(data);
        Assert.Equal("random", hud.Mode);
        Assert.Equal(new[] { "b6", "b7", "b8", "b9", "b10", "b1" }, hud.Buffs.Take(6).Select(b => b.Key));
        Assert.Equal(4, hud.Buffs.Count - CombatHudContent.CompactBuffCount);
        var skill = Assert.Single(hud.Skills);
        Assert.Contains("本回合已使用", skill.Caption);
        Assert.Contains("Lv.2", skill.Caption);
        Assert.Contains("本级完整效果", skill.Detail);
        Assert.Equal("本级简述", hud.Buffs[0].Hint);
    }

    [Fact]
    public void Ability_status_and_private_intel_use_independent_bars()
    {
        var snapshot = new CombatAbilityView("指挥官", "6/10", "主动技能可用", "", "", "", "最近敌人距离：900 游戏单位");
        var hud = CombatHudContent.FromAbility(snapshot);
        var skill = Assert.Single(hud.Skills);
        Assert.Contains("6/10", skill.Caption);
        Assert.DoesNotContain("敌人", skill.Caption);
        Assert.Equal(snapshot.Intel, hud.Intel!.Caption);
        Assert.Null(CombatHudContent.FromAbility(snapshot with { Intel = "" }).Intel);
        Assert.Contains("本回合已使用", Assert.Single(CombatHudContent.FromAbility(snapshot with { Status = "本回合已使用" }).Skills).Caption);
    }

    [Fact]
    public void Old_preferences_keep_other_values_and_layout_is_private_and_durable()
    {
        var dir = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "hud-tests", Guid.NewGuid().ToString()));
        var path = Path.Combine(dir.FullName, "preferences.json");
        File.WriteAllText(path, """{"a":{"Transparency":47,"Volume":31,"MouseEffectEnabled":true}}""");
        var store = new PlayerPreferenceStore(path);
        Assert.Null(store.Get("a").HudLayout);
        var draft = CombatHudLayout.Default.WithPoint(CombatHudBar.Intel, new(200, -1));
        store.SetHudLayout("a", draft);
        store.Set("a", volume: 61);
        var reloaded = new PlayerPreferenceStore(path);
        var saved = reloaded.Get("a");
        Assert.Equal(new HudPoint(100, 0), saved.HudLayout!.Intel);
        Assert.Equal(CombatHudLayout.Default.Buffs, saved.HudLayout.Buffs);
        Assert.Equal(47, saved.Transparency);
        Assert.True(saved.MouseEffectEnabled);
        Assert.Equal(61, saved.Volume);
        Assert.Null(reloaded.Get("b").HudLayout);
    }

    [Fact]
    public void A_failed_layout_save_does_not_publish_the_draft_or_replace_the_original()
    {
        var dir = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "hud-tests", Guid.NewGuid().ToString()));
        var path = Path.Combine(dir.FullName, "preferences.json");
        File.WriteAllText(path, "{incomplete");
        var store = new PlayerPreferenceStore(path);
        Assert.Throws<IOException>(() => store.SetHudLayout("a", CombatHudLayout.Default));
        Assert.Null(store.Get("a").HudLayout);
        Assert.Equal("{incomplete", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(CombatHudBar.Buffs, 0, 0, 0, 0)]
    [InlineData(CombatHudBar.Skills, 8, 4, 50, 48)]
    [InlineData(CombatHudBar.Intel, 16, 10, 100, 100)]
    public void Cell_coordinates_follow_the_selected_bars_visual_anchor(CombatHudBar bar, int x, int y, int expectedX, int expectedY) =>
        Assert.Equal(new HudPoint(expectedX, expectedY), CombatHudLayout.FromCell(bar, x, y));

    [Fact]
    public void Every_catalog_card_has_a_distinct_transparent_glyph_and_a_visible_slot()
    {
        var root = Resources();
        var catalog = RandomCatalog.Cards;
        var shapes = new HashSet<string>();
        var styles = File.ReadAllText(Path.Combine(root, "styles/custom_game/caoren_buff_icons.vcss"));
        var hud = XDocument.Load(Path.Combine(root, "layout/custom_game/caoren_combat_hud.vxml"));
        var buttons = hud.Descendants("Button").Select(e => (string?)e.Attribute("id")).ToHashSet();
        Assert.Equal(46, catalog.Count);
        foreach (var card in catalog)
        {
            var file = "buff_" + card.Key;
            var svg = XDocument.Load(Path.Combine(root, "images/custom_game", file + ".svg"));
            Assert.True(shapes.Add(svg.Root!.ToString()), file);
            Assert.Contains(".Icon_" + card.Key + " {", styles);
            Assert.Contains(file + ".vsvg", styles);
        }
        for (var i = 0; i < catalog.Count(c => !c.Active); i++) Assert.Contains("all_" + i, buttons);
        for (var i = 0; i < catalog.Count(c => c.Active); i++) Assert.Contains("skill_" + i, buttons);
        Assert.DoesNotContain("random_skill", buttons);
        var menu = XDocument.Load(Path.Combine(root, "layout/custom_game/caoren_player_menu.vxml"));
        var ids = menu.Descendants().Attributes("id").Select(a => a.Value).ToHashSet();
        for (var x = 0; x <= 16; x++)
        for (var y = 0; y <= 10; y++)
            Assert.Contains($"cell_{x}_{y}", ids);
        foreach (var id in new[] { "card_glyph_0", "card_glyph_1", "card_glyph_2", "detail_glyph", "hud_save" })
            Assert.Contains(id, ids);
    }

    [Fact]
    public void Held_inspection_is_read_only_and_late_key_up_cannot_release_a_new_menu()
    {
        var state = new HudInspection();
        var events = new List<bool>();
        var item = new CombatHudItem("pocket_medkit", "pocket_medkit", "D", "Lv.1", "急救包", "简述", "详情");
        var slots = new Dictionary<string, CombatHudItem> { ["all_6"] = item };
        state.Select("all_6", slots);
        Assert.Null(state.DetailKey);
        state.Begin(() => events.Add(true));
        state.Begin(() => events.Add(true));
        state.Select("hud_more", slots);
        state.Select("all_6", slots);
        Assert.True(state.Expanded);
        Assert.Equal(item.Key, state.DetailKey);
        state.Select("random_skill", slots);
        Assert.Equal(item.Key, state.DetailKey);
        state.Release(() => events.Add(false));
        state.Release(() => events.Add(false));
        Assert.Equal(new[] { true, false }, events);
        Assert.False(state.Held);
        Assert.False(state.Expanded);
        Assert.Null(state.DetailKey);
    }

    private static RandomBuffView Buff(string key, int number, string quality) =>
        new(key, quality + "-" + number, key, quality, "防御", "本级简述", "本级完整效果", 2, 3, 2);

    private static string Resources()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var root = Path.Combine(dir.FullName, "game-plugin", "Features", "InGameMenu", "resources", "panorama");
            if (Directory.Exists(root)) return root;
        }
        throw new DirectoryNotFoundException();
    }
}
