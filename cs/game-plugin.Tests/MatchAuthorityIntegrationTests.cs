using CaorenCup.Contracts;
using CaorenCup.Core;
using CaorenCup.Features;
using Xunit;
namespace CaorenCup.GamePlugin.Tests;
[Collection("Game mode state")]
public sealed class MatchAuthorityIntegrationTests
{
    [Fact] public void Bridge_authority_blocks_Core_mode_start_selection_and_stop_before_any_Cvar_write()
    {
        var commands=new List<string>();
        var runtime=new GameModeRuntime(new ManagedCvarScope(),(_,_)=>{},commands.Add,()=>"de_mirage",_=>{});
        var owner=new object();
        Assert.True(CaorenCupMatchAuthority.TryAcquire(owner));
        try {
            Assert.False(runtime.Start().Success);
            Assert.False(runtime.SelectMode("competitive").Success);
            Assert.False(runtime.Stop().Success);
            Assert.Empty(commands);
            Assert.Equal(GameModeLifecycleState.Lobby,runtime.State.Lifecycle);
        } finally { CaorenCupMatchAuthority.Release(owner); }
        Assert.True(runtime.Stop().Success);
        Assert.Empty(commands);
    }
}
