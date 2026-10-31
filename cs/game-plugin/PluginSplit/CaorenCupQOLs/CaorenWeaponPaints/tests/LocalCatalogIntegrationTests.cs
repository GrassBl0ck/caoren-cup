using CaorenCup.WeaponPaints.Core;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class LocalCatalogIntegrationTests
{
    [Fact]
    public void BundledCatalog_LoadsEveryRequiredCategoryWithoutImagesOrUrls()
    {
        var snapshot = LocalCatalogSnapshot.Load(Path.Combine(AppContext.BaseDirectory, "data"));

        Assert.True(snapshot[CatalogCategory.Skin].Count > 1000);
        Assert.True(snapshot[CatalogCategory.Glove].Count > 50);
        Assert.True(snapshot[CatalogCategory.Agent].Count > 50);
        Assert.True(snapshot[CatalogCategory.MusicKit].Count > 10);
        Assert.True(snapshot[CatalogCategory.Pin].Count > 100);
        Assert.True(snapshot[CatalogCategory.Sticker].Count > 1000);
        Assert.True(snapshot[CatalogCategory.Keychain].Count > 10);

        foreach (var item in snapshot.AllItems)
        {
            Assert.DoesNotContain("http", item.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", item.DisplayName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
