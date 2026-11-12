using System.Text.Json;
using CaorenCup.Contracts;
using CaorenCup.Core;
using CaorenCup.Features;
using CaorenCupPlugin.MatchRuntime;
using CaorenCupPlugin.Practice;
using Xunit;

namespace CaorenCupPlugin.Tests;

[CollectionDefinition("Match authority", DisableParallelization = true)]
public sealed class MatchAuthorityCollection;

[Collection("Match authority")]
public sealed class MatchCleanupAuthorityTests
{
    [Fact]
    public void Completed_match_retains_authority_until_explicit_cleanup_succeeds()
    {
        var host = new FailingRestoreHost();
        var adapter = new MatchRuntimeBridgeAdapter(host);
        var load = JsonSerializer.SerializeToElement(new {
            action = "load", matchId = "cleanup", revision = 1, mapName = "de_mirage",
            teamAName = "A", teamBName = "B", teamAInitialSide = "CT",
            seats = new[] {
                new { seatId = "a", roster = "A", steamId = "76561198000000001", name = "A", isCaptain = true },
                new { seatId = "b", roster = "B", steamId = "76561198000000002", name = "B", isCaptain = true },
            },
        });
        Assert.True(adapter.ApplyWebCommand(load, true, "caoren").Ok);
        Apply(adapter, "begin_warmup");
        Apply(adapter, "begin_ready_check");
        Apply(adapter, "begin_going_live");
        using var core = new CoreFixture();
        var owner = new object();
        Assert.True(CaorenCupMatchAuthority.TryAcquire(owner));
        try
        {
            MatchRoundResult? result = null;
            for (var round = 0; round < 13; round++)
            {
                if (adapter.Snapshot.SideSwapPending) Apply(adapter, "confirm_side_swap");
                Assert.True(adapter.OnNativeRoundStart(false).Ok);
                result = adapter.OnNativeRoundEnd(adapter.Snapshot.TeamAEngineSide);
            }
            Assert.True(result!.MatchFinished);
            Assert.Equal(MatchStage.Completed, adapter.Snapshot.Stage);
            Assert.False(adapter.IsActive);
            Assert.True(adapter.PendingCleanup);
            Assert.False(MatchRuntimeOwnership.TryReleaseIfIdle(owner, adapter.RequiresAuthority, false));
            core.AssertBlocked();
            Assert.Equal("CVAR_RESTORE_PENDING", adapter.ApplyWebCommand(load, true, "caoren").Code);
            Assert.Equal("CVAR_RESTORE_PENDING", adapter.Cleanup("retry still fails").Code);
            Assert.False(MatchRuntimeOwnership.TryReleaseIfIdle(owner, adapter.RequiresAuthority, false));
            core.AssertBlocked();

            host.RestoreSucceeds = true;
            Assert.True(adapter.Cleanup("administrator retry").Ok);
            Assert.Equal(MatchStage.Completed, adapter.Snapshot.Stage);
            Assert.False(adapter.PendingCleanup);
            Assert.True(MatchRuntimeOwnership.TryReleaseIfIdle(owner, adapter.RequiresAuthority, false));
            core.AssertCanStart();
            var attempts = host.RestoreAttempts;
            Assert.True(adapter.Cleanup("late cleanup").Ok);
            Assert.Equal(attempts, host.RestoreAttempts);
        }
        finally { CaorenCupMatchAuthority.Release(owner); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Practice_failure_keeps_authority_until_stop_retry_restores_all_cvars(bool failStart)
    {
        var failRestore = true;
        var practice = new PracticeRuntime((_, fallback) => fallback, command => {
            if ((failStart && command == "mp_warmup_end") || (failRestore && command == "sv_cheats 0"))
                throw new InvalidOperationException("injected host failure");
        });
        using var core = new CoreFixture();
        var owner = new object();
        Assert.True(CaorenCupMatchAuthority.TryAcquire(owner));
        try
        {
            var started = practice.Start("de_mirage", false);
            Assert.Equal(!failStart, started.Ok);
            if (!failStart) Assert.Equal("CVAR_RESTORE_PENDING", practice.Stop().Code);
            Assert.Equal(PracticeStage.PendingCleanup, practice.Stage);
            Assert.True(practice.PendingCleanup);
            Assert.False(MatchRuntimeOwnership.TryReleaseIfIdle(owner, false, practice.RequiresAuthority));
            core.AssertBlocked();
            Assert.Equal("PRACTICE_BUSY", practice.Start("de_mirage", false).Code);
            Assert.Equal("CVAR_RESTORE_PENDING", practice.Stop().Code);
            Assert.False(MatchRuntimeOwnership.TryReleaseIfIdle(owner, false, practice.RequiresAuthority));
            core.AssertBlocked();

            failRestore = false;
            Assert.True(practice.Stop().Ok);
            Assert.Equal(PracticeStage.Inactive, practice.Stage);
            Assert.False(practice.PendingCleanup);
            Assert.True(MatchRuntimeOwnership.TryReleaseIfIdle(owner, false, practice.RequiresAuthority));
            core.AssertCanStart();
        }
        finally { CaorenCupMatchAuthority.Release(owner); }
    }

    private static void Apply(MatchRuntimeBridgeAdapter adapter, string action) =>
        Assert.True(adapter.ApplyWebCommand(JsonSerializer.SerializeToElement(new {
            action, matchId = adapter.Snapshot.MatchId, revision = adapter.Snapshot.Revision,
            runtimeEpoch = adapter.Snapshot.RuntimeEpoch, force = true, abilitySyncConfirmed = true,
        }), true, "caoren").Ok);

    private sealed class CoreFixture : IDisposable
    {
        private readonly Provider _provider = new();
        private readonly List<string> _commands = [];
        private readonly GameModeRuntime _core;
        public CoreFixture()
        {
            CaorenCupGameModeRegistry.Register(_provider);
            _core = new GameModeRuntime(new ManagedCvarScope(), (_, _) => {}, _commands.Add, () => "de_mirage", _ => {});
            Assert.True(_core.SelectMode(_provider.Descriptor.Id).Success);
        }
        public void AssertBlocked()
        {
            Assert.False(_core.Start().Success);
            Assert.False(_core.Stop().Success);
            Assert.Empty(_commands);
        }
        public void AssertCanStart()
        {
            Assert.True(_core.Start().Success);
            Assert.Contains("mp_warmup_start", _commands);
        }
        public void Dispose() => CaorenCupGameModeRegistry.Unregister(_provider);
    }

    private sealed class Provider : ICaorenCupGameModeProvider
    {
        public GameModeDescriptor Descriptor { get; } = new("cleanup-test", "Cleanup", "Test", [], null, 15, []);
        public GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnWarmupStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnLiveStarted(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
        public GameModeResult OnStopped(GameModeRuntimeContext context) => GameModeResult.Ok("ok");
    }

    private sealed class FailingRestoreHost : IMatchRuntimeHost
    {
        public bool RestoreSucceeds { get; set; }
        public int RestoreAttempts { get; private set; }
        public IReadOnlyList<string> DemoSegments => [];
        public void ApplyFormalMatchConfig() {}
        public void ExecuteGoingLive() {}
        public void SwapSides() {}
        public void StartDemo(MatchDefinition definition) {}
        public void PauseMatch() {}
        public void UnpauseMatch() {}
        public bool RestoreFormalMatchConfig() { RestoreAttempts++; return RestoreSucceeds; }
        public string ReadCvar(string name, string fallback) => fallback;
        public void Execute(string command) {}
        public void Pause() {}
        public void RestoreValveBackup(string fileName) {}
        public void StopDemo() {}
        public void StartDemo(string fileName) {}
    }
}
