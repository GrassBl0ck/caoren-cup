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

    [Fact]
    public void Local_warmup_and_live_block_bridge_until_the_match_finishes()
    {
        var provider = new Provider();
        CaorenCupGameModeRegistry.Register(provider);
        Action? warmup = null;
        using var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, callback) => warmup = callback,
            _ => { }, () => "de_mirage", _ => { });
        var bridge = new object();
        try
        {
            Assert.True(runtime.SelectMode(provider.Descriptor.Id).Success);
            Assert.True(runtime.Start().Success);
            var firstId = runtime.MatchId;
            Assert.NotNull(firstId);
            Assert.True(CaorenCupMatchAuthority.IsOwned);
            Assert.False(CaorenCupMatchAuthority.IsOwnedOutsideCore);
            Assert.False(CaorenCupMatchAuthority.TryAcquire(bridge));

            Assert.NotNull(warmup);
            warmup();
            Assert.Equal(GameModeLifecycleState.Live, runtime.State.Lifecycle);
            Assert.Equal(firstId, runtime.MatchId);
            Assert.False(CaorenCupMatchAuthority.TryAcquire(bridge));
            Assert.True(runtime.FinishLiveMatch()?.Success);
            Assert.True(CaorenCupMatchAuthority.TryAcquire(bridge));
            Assert.True(CaorenCupMatchAuthority.IsOwnedOutsideCore);

            CaorenCupMatchAuthority.Release(bridge);
            Assert.True(runtime.Start().Success);
            Assert.NotEqual(firstId, runtime.MatchId);
        }
        finally
        {
            CaorenCupMatchAuthority.Release(bridge);
            CaorenCupGameModeRegistry.Unregister(provider);
        }
    }

    [Fact]
    public void Stop_cleanup_failure_keeps_control_and_invalidates_the_warmup_callback_until_retry()
    {
        var provider = new Provider { RejectStop = true };
        CaorenCupGameModeRegistry.Register(provider);
        Action? warmup = null;
        using var runtime = new GameModeRuntime(new ManagedCvarScope(), (_, callback) => warmup = callback,
            _ => { }, () => "de_mirage", _ => { });
        var bridge = new object();
        try
        {
            Assert.True(runtime.SelectMode(provider.Descriptor.Id).Success);
            Assert.True(runtime.Start().Success);
            Assert.False(runtime.Stop().Success);
            Assert.True(runtime.CleanupPending);
            Assert.False(CaorenCupMatchAuthority.TryAcquire(bridge));
            Assert.False(runtime.Start().Success);
            Assert.False(runtime.SelectMode(provider.Descriptor.Id).Success);
            Assert.NotNull(warmup);
            warmup();
            Assert.Equal(GameModeLifecycleState.Error, runtime.State.Lifecycle);

            provider.RejectStop = false;
            Assert.True(runtime.Stop().Success);
            Assert.True(CaorenCupMatchAuthority.TryAcquire(bridge));
        }
        finally
        {
            CaorenCupMatchAuthority.Release(bridge);
            CaorenCupGameModeRegistry.Unregister(provider);
        }
    }

    private sealed class Provider : ICaorenCupGameModeProvider
    {
        public bool RejectStop { get; set; }
        public GameModeDescriptor Descriptor { get; } = new("authority-test", "Authority Test", "Test", [], null, 15, []);
        public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnWarmupStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnLiveStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnStopped(GameModeRuntimeContext context) =>
            RejectStop ? GameModeResult.Fail("restore failed") : GameModeResult.Ok("ok");
    }
}
