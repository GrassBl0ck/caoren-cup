using Caoren;
using Caoren.AbilityMode;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityHudServiceTests
{
    [Fact]
    public void Hud_is_visible_by_default_and_toggle_can_hide_and_restore_it()
    {
        var hud = new AbilityHudService();
        hud.Start();

        Assert.True(hud.IsVisible("seat-a"));
        Assert.False(hud.ToggleVisibility("seat-a"));
        Assert.False(hud.IsVisible("seat-a"));
        Assert.True(hud.ToggleVisibility("seat-a"));
        Assert.True(hud.IsVisible("seat-a"));
    }

    [Fact]
    public void Hud_render_shows_ability_charge_and_skill_timer()
    {
        var definition = new AbilityDefinition("test", "测试异能", 12, AbilityReleaseModel.B, FixedCost: 8);
        var seat = new AbilitySeatState(new AbilitySyncSeat
        {
            PlayerId = "seat-a",
            SteamId = "76561198000000001",
            RosterTeam = "A",
            InitialSide = "CT",
            AbilityId = "test",
        }, definition);
        new ChargeService().Add(seat, 8, ChargeReason.Test);
        var now = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        seat.RecoverableUntil["坦克主动"] = now.AddSeconds(4.5);
        var hud = new AbilityHudService();
        hud.Start();

        var rendered = hud.Render(seat, now, hasActiveHandler: true);

        Assert.Contains("异能：测试异能", rendered);
        Assert.Contains("充能：", rendered);
        Assert.Contains("8/12", rendered);
        Assert.Contains("主动技能：主动技能可用", rendered);
        Assert.Contains("技能计时/CD：坦克主动剩余 4.5 秒", rendered);
    }
}
