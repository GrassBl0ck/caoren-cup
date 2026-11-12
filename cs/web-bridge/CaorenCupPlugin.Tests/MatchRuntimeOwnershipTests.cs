using CaorenCup.Contracts;
using CaorenCupPlugin.MatchRuntime;
using Xunit;
namespace CaorenCupPlugin.Tests;
[Collection("Match authority")]
public sealed class MatchRuntimeOwnershipTests
{
    [Theory]
    [InlineData(false,false,"competitive","caoren",false)]
    [InlineData(true,false,"competitive","caoren",false)]
    [InlineData(false,true,"competitive","caoren",false)]
    [InlineData(true,true,"competitive","matchzy",false)]
    [InlineData(true,true,"random","caoren",false)]
    [InlineData(true,true,"duel","caoren",false)]
    [InlineData(true,true,"rush","caoren",false)]
    [InlineData(true,true,"competitive","caoren",true)]
    [InlineData(true,true,"ability","caoren",true)]
    public void Explicit_local_and_web_gates_and_compatible_mode_are_required(bool local,bool web,string mode,string controller,bool expected) =>
        Assert.Equal(expected,MatchRuntimeOwnership.IsRequested(local,web,mode,controller));
    [Theory]
    [InlineData(GameModeLifecycleState.Live,false,"0",false)]
    [InlineData(GameModeLifecycleState.Warmup,false,"0",false)]
    [InlineData(GameModeLifecycleState.Lobby,true,"0",false)]
    [InlineData(GameModeLifecycleState.Lobby,false,"1",false)]
    [InlineData(GameModeLifecycleState.Lobby,false,"0",true)]
    public void Existing_Core_Practice_and_MatchZy_control_cannot_be_preempted(GameModeLifecycleState state,bool practice,string matchzy,bool expected) =>
        Assert.Equal(expected,MatchRuntimeOwnership.CanClaim(true,state,practice,matchzy));
    [Fact] public void Loaded_MatchZy_manual_match_is_rejected_even_with_autostart_disabled() => Assert.False(MatchRuntimeOwnership.CanClaim(true,GameModeLifecycleState.Lobby,false,"0",true));
    [Fact] public void Missing_compatible_Core_cannot_claim_authority() => Assert.False(MatchRuntimeOwnership.CanClaim(true,null,false,"0"));
    [Fact] public void Authority_lease_is_exclusive_and_cannot_be_released_by_another_owner()
    {
        var a=new object();var b=new object();
        try { Assert.True(CaorenCupMatchAuthority.TryAcquire(a));Assert.False(CaorenCupMatchAuthority.TryAcquire(b));
            CaorenCupMatchAuthority.Release(b);Assert.True(CaorenCupMatchAuthority.IsOwned);
            Assert.True(CaorenCupMatchAuthority.TryAcquire(a)); }
        finally { CaorenCupMatchAuthority.Release(a); }
        Assert.False(CaorenCupMatchAuthority.IsOwned);
    }
}
