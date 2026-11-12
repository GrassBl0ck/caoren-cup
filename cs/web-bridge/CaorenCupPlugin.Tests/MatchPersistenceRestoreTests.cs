using System.Text;
using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class MatchPersistenceRestoreTests
{
    [Fact]
    public async Task State_store_round_trips_utf8_and_matching_identity_atomically()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "match-state.json");
        var store = new MatchStateStore(path);
        var state = StateFile(epoch: 3, teamAName: "草人甲队");

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync(new MatchStateIdentity("match-restore", 5, 3));

        Assert.NotNull(loaded);
        Assert.Equal("草人甲队", loaded!.TeamAName);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.TopDirectoryOnly));
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
    }

    [Fact]
    public async Task Corrupt_primary_falls_back_to_the_previous_valid_snapshot()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "match-state.json");
        var store = new MatchStateStore(path);
        await store.SaveAsync(StateFile(epoch: 1, teamAName: "第一版"));
        await store.SaveAsync(StateFile(epoch: 2, teamAName: "第二版"));
        File.WriteAllText(path, "{broken", new UTF8Encoding(false));

        var loaded = await store.LoadAsync(new MatchStateIdentity("match-restore", 5, 1));

        Assert.NotNull(loaded);
        Assert.Equal("第一版", loaded!.TeamAName);
        Assert.Equal(1, loaded.Snapshot.RuntimeEpoch);
    }

    [Fact]
    public async Task Unknown_schema_identity_mismatch_and_nonmonotonic_events_are_rejected()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "match-state.json");
        var store = new MatchStateStore(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(StateFile(schemaVersion: 99)));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(StateFile(events: [Event(2), Event(1)])));
        await store.SaveAsync(StateFile(epoch: 4));

        var mismatched = await store.LoadAsync(new MatchStateIdentity("another-match", 5, 4));

        Assert.Null(mismatched);
    }

    [Fact]
    public void Stop_requires_both_captains_within_sixty_seconds()
    {
        var actions = new FakeServerActions();
        var manager = CreateBackupManager(actions, currentEpoch: 4);
        manager.CreateRestorePoint(RestorePoint(roundNumber: 7, epoch: 3));
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        var first = manager.RequestStop(RosterSide.A, now, currentRound: 7);
        var duplicate = manager.RequestStop(RosterSide.A, now.AddSeconds(5), currentRound: 7);
        var second = manager.RequestStop(RosterSide.B, now.AddSeconds(59), currentRound: 7);

        Assert.Equal("WAITING_FOR_OTHER_CAPTAIN", first.Code);
        Assert.Equal("ALREADY_CONFIRMED", duplicate.Code);
        Assert.Equal("RESTORE_READY", second.Code);
        Assert.Equal(7, second.Plan?.Target.RoundNumber);
        Assert.Equal(5, second.Plan?.NewRuntimeEpoch);
    }

    [Fact]
    public void Expired_stop_confirmation_starts_a_new_confirmation_window()
    {
        var manager = CreateBackupManager(new FakeServerActions(), currentEpoch: 4);
        manager.CreateRestorePoint(RestorePoint(roundNumber: 7, epoch: 3));
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        manager.RequestStop(RosterSide.A, now, currentRound: 7);

        var expired = manager.RequestStop(RosterSide.B, now.AddSeconds(61), currentRound: 7);

        Assert.Equal("WAITING_FOR_OTHER_CAPTAIN", expired.Code);
        Assert.Null(expired.Plan);
    }

    [Fact]
    public void Administrator_can_preview_an_exact_round_before_commit()
    {
        var manager = CreateBackupManager(new FakeServerActions(), currentEpoch: 8);
        manager.CreateRestorePoint(RestorePoint(roundNumber: 12, epoch: 5));

        var found = manager.PlanRestore(12);
        var missing = manager.PlanRestore(11);

        Assert.True(found.Ok);
        Assert.Equal("PREVIEW_READY", found.Code);
        Assert.Equal(9, found.Plan?.NewRuntimeEpoch);
        Assert.False(missing.Ok);
        Assert.Equal("RESTORE_POINT_NOT_FOUND", missing.Code);
    }

    [Fact]
    public void Restore_transaction_orders_engine_match_ability_and_pause_steps()
    {
        var log = new List<string>();
        var actions = new FakeServerActions(log);
        var target = new FakeRestoreTarget(log);
        var manager = CreateBackupManager(actions, currentEpoch: 8);
        manager.CreateRestorePoint(RestorePoint(roundNumber: 12, epoch: 5));
        var plan = manager.PlanRestore(12).Plan!;

        var result = manager.CommitRestore(plan, target);

        Assert.True(result.Ok);
        Assert.Equal(
            [
                "server:pause",
                "target:restoring",
                "server:restore:round12.txt",
                "target:match:9",
                "target:ability:ability-12:9",
                "target:reapply",
                "target:technical-pause",
            ],
            log);
        Assert.Equal(9, target.RestoredSnapshot?.RuntimeEpoch);
    }

    [Fact]
    public void Failed_valve_restore_stays_in_restoring_and_does_not_apply_later_steps()
    {
        var log = new List<string>();
        var actions = new FakeServerActions(log) { ThrowOnRestore = true };
        var target = new FakeRestoreTarget(log);
        var manager = CreateBackupManager(actions, currentEpoch: 8);
        manager.CreateRestorePoint(RestorePoint(roundNumber: 12, epoch: 5));

        var result = manager.CommitRestore(manager.PlanRestore(12).Plan!, target);

        Assert.False(result.Ok);
        Assert.Equal("RESTORE_FAILED", result.Code);
        Assert.Equal(["server:pause", "target:restoring", "server:restore:round12.txt"], log);
        Assert.Null(target.RestoredSnapshot);
    }

    [Fact]
    public void Match_cvar_scope_captures_once_and_restores_original_values()
    {
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mp_maxrounds"] = "24",
        };
        var commands = new List<string>();
        var scope = new MatchServerCvarScope(
            (name, fallback) => current.GetValueOrDefault(name, fallback),
            command => commands.Add(command));

        scope.Set("mp_maxrounds", "30", "24");
        scope.Set("mp_maxrounds", "36", "24");
        scope.RestoreAll();

        Assert.Equal(["mp_maxrounds 30", "mp_maxrounds 36", "mp_maxrounds 24"], commands);
        Assert.True(scope.IsReadyForNewMatch);
    }

    [Theory]
    [InlineData("mp_maxrounds;quit", "24")]
    [InlineData("mp_maxrounds", "24;quit")]
    [InlineData("mp_maxrounds", "NaN")]
    public void Match_cvar_scope_rejects_unsafe_names_and_values(string name, string value)
    {
        var scope = new MatchServerCvarScope((_, fallback) => fallback, _ => { });

        Assert.Throws<ArgumentException>(() => scope.Set(name, value, "24"));
    }

    [Fact]
    public void Failed_cvar_restore_remains_pending_for_safe_retry()
    {
        var attempts = 0;
        var scope = new MatchServerCvarScope(
            (_, fallback) => fallback,
            command =>
            {
                if (command == "mp_maxrounds 24" && attempts++ == 0) throw new InvalidOperationException("busy");
            });
        scope.Set("mp_maxrounds", "30", "24");

        Assert.False(scope.TryRestoreAll(maxAttempts: 1));
        Assert.False(scope.IsReadyForNewMatch);
        Assert.True(scope.TryRestoreAll(maxAttempts: 1));
        Assert.True(scope.IsReadyForNewMatch);
    }

    [Fact]
    public void Demo_recovery_creates_a_new_local_segment_and_never_uploads()
    {
        var actions = new FakeServerActions();
        var recorder = new DemoRecorder(actions);
        var first = recorder.StartSegment("match-1", "de_mirage", "草人A", "草人B", new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
        recorder.Stop(new DateTimeOffset(2026, 8, 22, 12, 30, 0, TimeSpan.Zero));
        var second = recorder.ResumeAfterRecovery("match-1", "de_mirage", "草人A", "草人B", new DateTimeOffset(2026, 8, 22, 12, 31, 0, TimeSpan.Zero));

        Assert.NotEqual(first.FileName, second.FileName);
        Assert.Equal(2, recorder.Segments.Count);
        Assert.All(recorder.Segments, segment => Assert.EndsWith(".dem", segment.FileName));
        Assert.Equal(2, actions.StartedDemos.Count);
        Assert.Equal(1, actions.StopDemoCalls);
    }

    [Fact]
    public void Bridge_stats_checkpoint_restores_player_counters_without_sharing_mutable_instances()
    {
        var live = new Dictionary<string, BridgePlayerStats>(StringComparer.Ordinal)
        {
            ["76561198000000001"] = new("A1", 3, 2, 1, 450),
        };
        var checkpoint = BridgeStatsCheckpoint.Capture(roundNumber: 4, runtimeEpoch: 2, live);
        live["76561198000000001"] = new("A1", 9, 9, 9, 999);
        live["76561198100000001"] = new("B1", 1, 1, 1, 100);

        checkpoint.RestoreTo(live);

        Assert.Equal(new BridgePlayerStats("A1", 3, 2, 1, 450), live["76561198000000001"]);
        Assert.False(live.ContainsKey("76561198100000001"));
        Assert.Equal(4, checkpoint.RoundNumber);
        Assert.Equal(2, checkpoint.RuntimeEpoch);
    }

    private static RoundBackupManager CreateBackupManager(FakeServerActions actions, long currentEpoch) =>
        new(actions, () => Snapshot(currentEpoch));

    private static RoundRestorePoint RestorePoint(int roundNumber, long epoch) => new(
        roundNumber,
        $"match-restore:{epoch}:round-{roundNumber}",
        $"round{roundNumber}.txt",
        Snapshot(epoch),
        $"ability-{roundNumber}",
        $"stats-{roundNumber}",
        [],
        RunningPause(),
        []);

    private static MatchStateFile StateFile(
        int schemaVersion = 1,
        long epoch = 1,
        string teamAName = "A队",
        IReadOnlyList<MatchEvent>? events = null) => new(
        schemaVersion,
        new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero),
        teamAName,
        "B队",
        Snapshot(epoch),
        events ?? [Event(1, epoch), Event(2, epoch)],
        [RestorePoint(1, epoch)]);

    private static MatchEvent Event(long sequence, long epoch = 1) =>
        new(sequence, "round_started", "match-restore", 5, epoch, $"round-{sequence}");

    private static MatchRuntimeSnapshot Snapshot(long epoch) => new(
        "match-restore",
        5,
        epoch,
        MatchStage.SecondHalf,
        MatchPauseState.Running,
        8,
        7,
        15,
        0,
        0,
        EngineSide.Terrorist,
        false,
        null,
        20);

    private static PauseSnapshot RunningPause() => new(
        MatchPauseState.Running,
        null,
        null,
        null,
        null,
        false,
        []);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caoren-match-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeServerActions(List<string>? log = null) : IMatchServerActions
    {
        private readonly List<string> _log = log ?? [];

        public bool ThrowOnRestore { get; set; }

        public List<string> StartedDemos { get; } = [];

        public int StopDemoCalls { get; private set; }

        public string ReadCvar(string name, string fallback) => fallback;

        public void Execute(string command) => _log.Add("server:execute:" + command);

        public void Pause() => _log.Add("server:pause");

        public void RestoreValveBackup(string fileName)
        {
            _log.Add("server:restore:" + fileName);
            if (ThrowOnRestore) throw new InvalidOperationException("restore failed");
        }

        public void StartDemo(string fileName)
        {
            StartedDemos.Add(fileName);
            _log.Add("server:demo-start:" + fileName);
        }

        public void StopDemo()
        {
            StopDemoCalls++;
            _log.Add("server:demo-stop");
        }
    }

    private sealed class FakeRestoreTarget(List<string> log) : IMatchRestoreTarget
    {
        public MatchRuntimeSnapshot? RestoredSnapshot { get; private set; }

        public void EnterRestoring() => log.Add("target:restoring");

        public void RestoreMatchState(MatchRuntimeSnapshot snapshot)
        {
            RestoredSnapshot = snapshot;
            log.Add("target:match:" + snapshot.RuntimeEpoch);
        }

        public void RestoreAbility(string checkpointId, long runtimeEpoch) =>
            log.Add($"target:ability:{checkpointId}:{runtimeEpoch}");

        public void ReapplyRosterAndCvars() => log.Add("target:reapply");

        public void EnterTechnicalPause() => log.Add("target:technical-pause");
    }
}
