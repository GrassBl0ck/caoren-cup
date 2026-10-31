using Caoren.AbilityMode;
using CounterStrikeSharp.API.Core;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class CounterStrikePrototypeAdapterTests
{
    [Fact]
    public void Current_counterstrike_api_exposes_decoy_landing_event_but_not_safe_trace()
    {
        var capabilities = new CounterStrikeAbilityEngineCapabilities();
        Assert.True(capabilities.Supports(AbilityPrototypeKind.DecoyLanding));
        Assert.False(capabilities.Supports(AbilityPrototypeKind.SafeTeleport));
        Assert.NotNull(typeof(EventDecoyStarted).GetProperty(nameof(EventDecoyStarted.Entityid)));
    }
}
