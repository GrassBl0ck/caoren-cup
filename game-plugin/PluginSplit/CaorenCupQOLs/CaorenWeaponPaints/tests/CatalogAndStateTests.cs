using CaorenCup.WeaponPaints.Core;
using Xunit;

namespace CaorenCup.WeaponPaints.Tests;

public sealed class CatalogAndStateTests
{
    [Fact]
    public void BilingualCatalog_UsesChineseNameAndEnglishFallback()
    {
        const string english = """
            [
              { "id": "1", "name": "Sticker | Shooter", "image": "https://example.invalid/1.png" },
              { "id": "2", "name": "Sticker | Crown", "image": "https://example.invalid/2.png" }
            ]
            """;
        const string chinese = """
            [
              { "id": "1", "name": "印花 | 射手", "image": "" },
              { "id": "2", "name": "", "image": "" }
            ]
            """;

        var items = BilingualCatalog.Parse(english, chinese, CatalogCategory.Sticker);

        Assert.Equal("印花 | 射手", items[0].DisplayName);
        Assert.Equal("Sticker | Crown", items[1].DisplayName);
        Assert.DoesNotContain(items, item => item.Key.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InputSession_IsOneShotAndExpires()
    {
        var store = new ChatInputSessionStore(TimeSpan.FromSeconds(30));
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        store.Begin(42, new ChatInputRequest(ChatInputKind.Seed, TeamSide.Terrorist, 7), now);

        Assert.True(store.TryConsume(42, now.AddSeconds(10), out var request));
        Assert.Equal(ChatInputKind.Seed, request.Kind);
        Assert.False(store.TryConsume(42, now.AddSeconds(11), out _));

        store.Begin(42, new ChatInputRequest(ChatInputKind.Wear, TeamSide.Terrorist, 7), now);
        Assert.False(store.TryConsume(42, now.AddSeconds(31), out _));
    }

    [Fact]
    public void InputSession_CancelClearsPendingInput()
    {
        var store = new ChatInputSessionStore(TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.UtcNow;
        store.Begin(9, new ChatInputRequest(ChatInputKind.Search, TeamSide.CounterTerrorist), now);

        Assert.True(store.Cancel(9));
        Assert.False(store.TryConsume(9, now, out _));
    }
}
