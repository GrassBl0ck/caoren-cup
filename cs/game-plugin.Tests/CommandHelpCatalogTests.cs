using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CommandHelpCatalogTests
{
    [Fact]
    public void Embedded_catalog_is_complete_unique_and_alphabetically_ordered()
    {
        var catalog = CommandHelpCatalog.Load();
        Assert.Equal(53, catalog.Entries.Count);
        Assert.Equal(catalog.Entries.OrderBy(e => e.Name.TrimStart('/'), StringComparer.OrdinalIgnoreCase), catalog.Entries);
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(catalog.Entries, e => { Assert.NotEmpty(e.Usage); Assert.NotEmpty(e.Parameters); Assert.NotEmpty(e.Sources); });
        foreach (var name in new[] { "/acc", "/loadout", "/mod", "/move", "/duel", "/notice", "/wp_refresh", "/crcvote_admin" })
            Assert.Contains(catalog.Entries, e => e.Name == name);
    }

    [Fact]
    public void Player_catalog_contains_only_normal_match_commands()
    {
        var player = CommandHelpCatalog.LoadPlayer();
        Assert.Equal(new[] { ".ready", ".tac", ".tech", ".unpause", ".unready" }, player.Entries.Select(entry => entry.Name));
        Assert.Equal(1, player.ListPageCount);
        Assert.Equal(player.Entries.OrderBy(e => e.Name.TrimStart('/'), StringComparer.OrdinalIgnoreCase), player.Entries);
        Assert.All(player.Entries, entry =>
        {
            Assert.Equal("MatchZy · 对局", entry.Module);
            Assert.StartsWith("css_", entry.Console);
            Assert.NotEmpty(entry.Usage);
            Assert.NotEmpty(entry.WorkedExamples);
            Assert.All(entry.Sources, source => Assert.Contains("/MatchZy/blob/0.8.15/", source));
        });
        Assert.Contains(".pause", player.Entries.Single(entry => entry.Name == ".tech").Usage);
        Assert.Contains(player.Entries.Single(entry => entry.Name == ".unpause").Notes, note => note.Contains("双方"));
        Assert.DoesNotContain(CommandHelpCatalog.Load().Entries, e => e.Name == "/status");
    }

    [Fact]
    public void Paging_covers_each_entry_once_and_clamps_invalid_page_indices()
    {
        var catalog = CommandHelpCatalog.Load();
        var pages = Enumerable.Range(0, catalog.ListPageCount).SelectMany(catalog.ListPage).ToArray();
        Assert.Equal(catalog.Entries, pages);
        Assert.Equal(catalog.ListPage(0), catalog.ListPage(-100));
        Assert.Equal(catalog.ListPage(catalog.ListPageCount - 1), catalog.ListPage(1000));
    }

    [Fact]
    public void Important_parameter_meanings_match_verified_code_and_native_cvars()
    {
        var catalog = CommandHelpCatalog.Load();
        var loadout = catalog.Entries.Single(e => e.Name == "/loadout");
        Assert.Contains(loadout.Parameters, text => text.Contains("2仅T、3仅CT"));
        var ammo = catalog.Entries.Single(e => e.Name == "/ammo");
        Assert.Contains(ammo.Parameters, text => text.Contains("不支持 -"));
        var duel = catalog.Entries.Single(e => e.Name == "/duel");
        Assert.Contains(duel.Parameters, text => text.Contains("15秒"));
        Assert.True(catalog.DetailLines(loadout).Count > CommandHelpCatalog.DetailPageSize);
    }
}
