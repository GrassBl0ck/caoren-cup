using System.Text.Json;
using System.Reflection;
using CaorenCupPlugin.MatchRuntime;
using CaorenCupPlugin.Practice;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class MatchBridgeIntegrationTests
{
    [Fact]
    public void Runtime_load_requires_feature_flag_and_caoren_controller()
    {
        var host = new FakeMatchRuntimeHost();
        var adapter = new MatchRuntimeBridgeAdapter(host);
        var payload = LoadPayload();

        var disabled = adapter.ApplyWebCommand(payload, featureEnabled: false, matchController: "caoren");
        var matchzy = adapter.ApplyWebCommand(payload, featureEnabled: true, matchController: "matchzy");
        var caoren = adapter.ApplyWebCommand(payload, featureEnabled: true, matchController: "caoren");

        Assert.Equal("FEATURE_DISABLED", disabled.Code);
        Assert.Equal("CONTROLLER_CONFLICT", matchzy.Code);
        Assert.True(caoren.Ok);
        Assert.Equal(MatchStage.Staging, adapter.Snapshot.Stage);
    }

    [Fact]
    public void Warmup_round_start_never_promotes_staging_or_ready_check_to_live()
    {
        var adapter = CreateLoadedAdapter();
        Apply(adapter, "begin_warmup");
        Apply(adapter, "begin_ready_check");

        var result = adapter.OnNativeRoundStart(isWarmup: true);

        Assert.False(result.Ok);
        Assert.Equal("WARMUP_IGNORED", result.Code);
        Assert.Equal(MatchStage.ReadyCheck, adapter.Snapshot.Stage);
        Assert.Null(adapter.Snapshot.CurrentRoundKey);
    }

    [Fact]
    public void Going_live_native_round_start_emits_explicit_match_start_and_opens_round()
    {
        var adapter = CreateGoingLiveAdapter();

        var result = adapter.OnNativeRoundStart(isWarmup: false);

        Assert.True(result.Ok);
        Assert.Equal(MatchStage.FirstHalf, adapter.Snapshot.Stage);
        Assert.Equal("match-bridge:1:round-1", adapter.Snapshot.CurrentRoundKey);
        Assert.Single(adapter.DemoSegments);
        Assert.Contains(adapter.Events, item => item.Type == "match_started");
        Assert.Contains(adapter.Events, item => item.Type == "round_started");
    }

    [Fact]
    public void Native_round_end_maps_engine_side_to_roster_and_deduplicates()
    {
        var adapter = CreateGoingLiveAdapter();
        adapter.OnNativeRoundStart(isWarmup: false);

        var first = adapter.OnNativeRoundEnd(EngineSide.CounterTerrorist);
        var duplicate = adapter.OnNativeRoundEnd(EngineSide.CounterTerrorist);

        Assert.True(first.Ok);
        Assert.Equal(1, adapter.Snapshot.ScoreA);
        Assert.Equal(0, adapter.Snapshot.ScoreB);
        Assert.False(duplicate.Ok);
        Assert.Equal("NO_OPEN_ROUND", duplicate.Code);
        Assert.Equal(1, adapter.Snapshot.ScoreA);
    }

    [Fact]
    public void Halftime_blocks_native_rounds_until_authoritative_side_swap()
    {
        var host = new FakeMatchRuntimeHost();
        var adapter = CreateGoingLiveAdapter(host);
        for (var round = 0; round < 12; round++)
        {
            Assert.True(adapter.OnNativeRoundStart(isWarmup: false).Ok);
            Assert.True(adapter.OnNativeRoundEnd(round % 2 == 0
                ? EngineSide.CounterTerrorist
                : EngineSide.Terrorist).Ok);
        }

        var blocked = adapter.OnNativeRoundStart(isWarmup: false);
        var swapped = Apply(adapter, "confirm_side_swap");
        var next = adapter.OnNativeRoundStart(isWarmup: false);

        Assert.Equal("INVALID_STAGE", blocked.Code);
        Assert.True(swapped.Ok);
        Assert.Equal(["apply-config", "going-live", "start-demo", "swap-sides"], host.Log);
        Assert.True(next.Ok);
        Assert.Equal(EngineSide.Terrorist, adapter.Snapshot.TeamAEngineSide);
    }

    [Fact]
    public void Commands_from_an_old_runtime_epoch_are_rejected()
    {
        var adapter = CreateLoadedAdapter();
        var before = adapter.Snapshot;

        var result = adapter.ApplyWebCommand(
            CommandPayload("abort", runtimeEpoch: 99),
            featureEnabled: true,
            matchController: "caoren");

        Assert.False(result.Ok);
        Assert.Equal("IDENTITY_MISMATCH", result.Code);
        Assert.Equal(before, adapter.Snapshot);
    }

    [Fact]
    public void Expected_stage_mismatch_rejects_a_late_web_command()
    {
        var adapter = CreateLoadedAdapter();
        var payload = JsonSerializer.SerializeToElement(new
        {
            action = "begin_warmup",
            matchId = "match-bridge",
            revision = 3,
            runtimeEpoch = 1,
            expectedStage = "readyCheck",
        });

        var result = adapter.ApplyWebCommand(payload, true, "caoren");

        Assert.False(result.Ok);
        Assert.Equal("EXPECTED_STAGE_MISMATCH", result.Code);
        Assert.Equal(MatchStage.Staging, adapter.Snapshot.Stage);
    }

    [Fact]
    public void Cleanup_aborts_runtime_stops_demo_and_restores_match_cvars()
    {
        var host = new FakeMatchRuntimeHost();
        var adapter = CreateGoingLiveAdapter(host);
        adapter.OnNativeRoundStart(isWarmup: false);

        var result = adapter.Cleanup("plugin unload");

        Assert.True(result.Ok);
        Assert.Equal(MatchStage.Aborted, adapter.Snapshot.Stage);
        Assert.Equal(["apply-config", "going-live", "start-demo", "stop-demo", "restore-config"], host.Log);
    }

    [Fact]
    public void Ready_pause_and_substitute_actions_share_the_bridge_runtime_services()
    {
        var host = new FakeMatchRuntimeHost();
        var adapter = CreateLoadedAdapter(host);
        Assert.True(ApplyPayload(adapter, "observe_player", new { steamId = "76561198000000001", side = "CT", connected = true }).Ok);
        Assert.True(ApplyPayload(adapter, "observe_player", new { steamId = "76561198100000001", side = "T", connected = true }).Ok);
        Assert.True(ApplyPayload(adapter, "set_ready", new { steamId = "76561198000000001", ready = true }).Ok);
        Assert.True(ApplyPayload(adapter, "set_ready", new { steamId = "76561198100000001", ready = true }).Ok);
        Apply(adapter, "begin_warmup");
        Apply(adapter, "begin_ready_check");

        var started = ApplyPayload(adapter, "begin_going_live", new { force = false, abilitySyncConfirmed = true });
        adapter.OnNativeRoundStart(isWarmup: false);
        var technical = ApplyPayload(adapter, "technical_pause", new { reason = "network", requestedAt = 1_000L });
        Assert.True(technical.Ok);
        Assert.Equal(MatchPauseState.TechnicalPaused, adapter.Snapshot.PauseState);
        var substitute = ApplyPayload(adapter, "substitute", new
        {
            seatId = "seat-a1",
            steamId = "76561198222222222",
            name = "替补",
            effectiveRound = 2,
        });
        var unpause = ApplyPayload(adapter, "force_unpause", new { });

        Assert.True(started.Ok);
        Assert.True(substitute.Ok);
        Assert.Contains(adapter.CurrentRosterAssignments, item =>
            item.SteamId == "76561198222222222" && item.Side == EngineSide.CounterTerrorist);
        Assert.DoesNotContain(adapter.CurrentRosterAssignments, item => item.SteamId == "76561198000000001");
        Assert.True(unpause.Ok);
        Assert.Equal(MatchPauseState.Running, adapter.Snapshot.PauseState);
        Assert.Contains("pause-match", host.Log);
        Assert.Contains("unpause-match", host.Log);
    }

    [Fact]
    public void Tactical_pause_is_rejected_before_the_match_is_live()
    {
        var adapter = CreateLoadedAdapter();

        var result = ApplyPayload(adapter, "tactical_pause", new { roster = "A", requestedAt = 1_000L });

        Assert.False(result.Ok);
        Assert.Equal("INVALID_STAGE", result.Code);
        Assert.Equal(MatchPauseState.Running, adapter.Snapshot.PauseState);
    }

    [Fact]
    public void Both_captains_stop_restores_current_round_and_creates_ability_restore_request()
    {
        var host = new FakeMatchRuntimeHost();
        var adapter = CreateGoingLiveAdapter(host);
        adapter.OnNativeRoundStart(isWarmup: false);

        Assert.Equal(RosterSide.A, adapter.CaptainRosterForSteamId("76561198000000001"));
        Assert.Null(adapter.CaptainRosterForSteamId("76561198222222222"));
        var first = ApplyPayload(adapter, "stop", new { roster = "A", requestedAt = 1_000L });
        var second = ApplyPayload(adapter, "stop", new { roster = "B", requestedAt = 1_050L });
        var ability = adapter.TakePendingAbilityRestore();

        Assert.Equal("WAITING_FOR_OTHER_CAPTAIN", first.Code);
        Assert.True(second.Ok);
        Assert.Equal("RESTORED", second.Code);
        Assert.Equal(2, adapter.Snapshot.RuntimeEpoch);
        Assert.Equal(MatchPauseState.TechnicalPaused, adapter.Snapshot.PauseState);
        Assert.Equal("round-1", ability?.CheckpointId);
        Assert.Equal(2, ability?.RuntimeEpoch);
        Assert.Equal(2, adapter.DemoSegments.Count);
        Assert.Contains(host.Log, item => item.StartsWith("restore:caoren_match-bridge_round1.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void Normal_match_completion_stops_demo_and_restores_formal_cvars()
    {
        var host = new FakeMatchRuntimeHost();
        var adapter = CreateGoingLiveAdapter(host);
        for (var round = 0; round < 12; round++)
        {
            adapter.OnNativeRoundStart(isWarmup: false);
            adapter.OnNativeRoundEnd(EngineSide.CounterTerrorist);
        }
        Apply(adapter, "confirm_side_swap");
        adapter.OnNativeRoundStart(isWarmup: false);

        var finished = adapter.OnNativeRoundEnd(EngineSide.Terrorist);

        Assert.True(finished.MatchFinished);
        Assert.Equal(MatchStage.Completed, adapter.Snapshot.Stage);
        Assert.Contains("stop-demo", host.Log);
        Assert.Equal("restore-config", host.Log.Last());
    }

    [Fact]
    public void Fresh_bridge_recovers_web_snapshot_fail_closed_in_technical_pause()
    {
        var original = CreateGoingLiveAdapter();
        original.OnNativeRoundStart(isWarmup: false);
        original.OnNativeRoundEnd(EngineSide.CounterTerrorist);
        var saved = original.Snapshot;
        var host = new FakeMatchRuntimeHost();
        var recovered = new MatchRuntimeBridgeAdapter(host);

        var result = recovered.RecoverFromWeb(LoadPayload(), saved, original.RestorePoints);

        Assert.True(result.Ok);
        Assert.Equal(saved.ScoreA, recovered.Snapshot.ScoreA);
        Assert.Equal(saved.CompletedRounds, recovered.Snapshot.CompletedRounds);
        Assert.Equal(saved.RuntimeEpoch, recovered.Snapshot.RuntimeEpoch);
        Assert.Equal(saved.Stage, recovered.Snapshot.Stage);
        Assert.Equal(MatchPauseState.TechnicalPaused, recovered.Snapshot.PauseState);
        Assert.Equal(original.RestorePoints.Count, recovered.RestorePoints.Count);
        Assert.Equal(["apply-config", "pause-match", "start-demo"], host.Log);
    }

    [Fact]
    public void Recovered_restore_point_restores_persisted_bridge_player_stats()
    {
        var original = CreateGoingLiveAdapter();
        original.OnNativeRoundStart(isWarmup: false);
        original.AttachBridgeStatsCheckpoint(1, new Dictionary<string, BridgePlayerStats>
        {
            ["76561198000000001"] = new("A1", 3, 2, 1, 450),
        });
        original.OnNativeRoundEnd(EngineSide.CounterTerrorist);
        var recovered = new MatchRuntimeBridgeAdapter(new FakeMatchRuntimeHost());
        Assert.True(recovered.RecoverFromWeb(LoadPayload(), original.Snapshot, original.RestorePoints).Ok);

        var restored = ApplyPayload(recovered, "restore", new { targetRound = 1 });
        var stats = recovered.TakePendingBridgeStatsRestore();

        Assert.True(restored.Ok);
        Assert.Equal(new BridgePlayerStats("A1", 3, 2, 1, 450), stats?["76561198000000001"]);
    }

    [Fact]
    public void Match_runtime_web_command_is_a_blocked_match_control_transaction()
    {
        var command = new PluginCommand
        {
            Type = MatchRuntimeBridgeAdapter.WebCommandType,
            Payload = CommandPayload("begin_warmup"),
        };

        Assert.True(WebCommandGameThreadDispatcher.IsMatchControlPluginCommand(command));
        Assert.True(WebCommandGameThreadDispatcher.IsBlockedByPendingCvarRestore(command, hasPendingCvarRestore: true));
    }

    [Fact]
    public void Active_caoren_runtime_blocks_legacy_match_mutations_but_accepts_its_own_commands()
    {
        var runtime = new PluginCommand
        {
            Type = MatchRuntimeBridgeAdapter.WebCommandType,
            Payload = CommandPayload("begin_warmup"),
        };
        var assignments = new PluginCommand
        {
            Type = "APPLY_TEAM_ASSIGNMENTS",
            Payload = JsonSerializer.SerializeToElement(new { }),
        };
        var restart = new PluginCommand
        {
            Type = "EXECUTE_SERVER_COMMAND",
            Payload = JsonSerializer.SerializeToElement(new { command = "mp_restartgame 1" }),
        };

        Assert.False(WebCommandGameThreadDispatcher.IsBlockedByCaorenRuntime(runtime, runtimeActive: true));
        Assert.True(WebCommandGameThreadDispatcher.IsBlockedByCaorenRuntime(assignments, runtimeActive: true));
        Assert.True(WebCommandGameThreadDispatcher.IsBlockedByCaorenRuntime(restart, runtimeActive: true));
        Assert.False(WebCommandGameThreadDispatcher.IsBlockedByCaorenRuntime(restart, runtimeActive: false));
    }

    [Theory]
    [InlineData("mp_backup_restore_load_file round1.txt")]
    [InlineData("tv_record match.dem")]
    [InlineData("mp_swapteams")]
    public void Runtime_only_server_commands_remain_blocked_from_the_public_web_command_policy(string command)
    {
        Assert.False(BridgeServerCommandPolicy.IsAllowed(command));
    }

    [Fact]
    public void Compatibility_registration_contains_match_and_practice_aliases_without_duplicates()
    {
        var commands = global::CaorenCupPlugin.CaorenCupPlugin.MatchCompatibilityCommandNames;

        Assert.Contains("css_ready", commands);
        Assert.Contains("css_pause", commands);
        Assert.Contains("css_stop", commands);
        Assert.Contains("css_restore", commands);
        Assert.Contains("css_prac", commands);
        Assert.Contains("css_rethrow", commands);
        Assert.Contains("css_savenade", commands);
        Assert.Contains("css_loadnade", commands);
        Assert.Equal(commands.Count, commands.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(PracticeStage.Active, "load", true)]
    [InlineData(PracticeStage.Starting, "begin_warmup", true)]
    [InlineData(PracticeStage.Inactive, "load", false)]
    [InlineData(PracticeStage.Stopping, "abort", false)]
    public void Practice_blocks_only_match_starting_actions(PracticeStage stage, string action, bool expected)
    {
        Assert.Equal(expected, global::CaorenCupPlugin.CaorenCupPlugin.IsPracticeBlockingMatchAction(stage, action));
    }

    [Theory]
    [InlineData("A", "CT", "CT")]
    [InlineData("B", "CT", "T")]
    [InlineData("A", "T", "T")]
    [InlineData("B", "T", "CT")]
    public void Runtime_roster_assignment_respects_initial_side(string roster, string teamAInitialSide, string expected)
    {
        Assert.Equal(expected, global::CaorenCupPlugin.CaorenCupPlugin.ResolveRuntimeAssignmentSide(roster, teamAInitialSide));
    }

    [Fact]
    public void Actual_host_demo_filename_contains_match_map_and_team_names()
    {
        var definition = new MatchDefinition(
            "match-9", 1, "de_mirage", "草一队", "草二队", EngineSide.CounterTerrorist,
            [new MatchSeat("a1", RosterSide.A, "76561198000000001", "A1", true),
             new MatchSeat("b1", RosterSide.B, "76561198100000001", "B1", true)]);

        var fileName = CounterStrikeMatchRuntimeHost.BuildDemoFileName(
            definition,
            new DateTime(2026, 8, 23, 1, 2, 3, DateTimeKind.Utc));

        Assert.Contains("match-9", fileName);
        Assert.Contains("de_mirage", fileName);
        Assert.Contains("草一队-vs-草二队", fileName);
    }

    [Fact]
    public void Runtime_feature_is_disabled_by_default_and_heartbeat_contract_carries_controller_state()
    {
        var config = new CaorenConfig();
        var request = new PluginHeartbeatRequest
        {
            MapName = "de_mirage",
            MatchController = "caoren",
            MatchRuntime = Snapshot(),
            MatchRuntimeEvents =
            [
                new MatchEvent(1, "match_started", "match-heartbeat", 2, 4, "round-1"),
            ],
        };
        var options = global::CaorenCupPlugin.CaorenCupPlugin.CreatePluginJsonOptions();
        var json = JsonSerializer.Serialize(request, options);
        var response = JsonSerializer.Deserialize<PluginHeartbeatResponse>(json, options);

        Assert.False(config.CaorenMatchRuntimeEnabled);
        Assert.NotNull(response);
        Assert.Equal("caoren", response!.MatchController);
        Assert.Equal(MatchStage.SecondHalf, response.MatchRuntime?.Stage);
        Assert.Equal(4, response.MatchRuntime?.RuntimeEpoch);
        Assert.Contains("\"matchRuntimeEvents\"", json);
        Assert.Contains("\"sequence\":1", json);
        Assert.Contains("\"stage\":\"secondHalf\"", json);
    }

    [Theory]
    [InlineData(false, "caoren", MatchStage.FirstHalf, false)]
    [InlineData(true, "matchzy", MatchStage.FirstHalf, false)]
    [InlineData(true, "caoren", MatchStage.Disabled, false)]
    [InlineData(true, "caoren", MatchStage.Completed, false)]
    [InlineData(true, "caoren", MatchStage.FirstHalf, true)]
    public void Native_round_routing_requires_enabled_caoren_active_runtime(
        bool enabled,
        string controller,
        MatchStage stage,
        bool expected)
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "ShouldUseCaorenMatchRuntime",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, Assert.IsType<bool>(method!.Invoke(null, [enabled, controller, stage])));
    }

    private static MatchRuntimeBridgeAdapter CreateLoadedAdapter(FakeMatchRuntimeHost? host = null)
    {
        var adapter = new MatchRuntimeBridgeAdapter(host ?? new FakeMatchRuntimeHost());
        Assert.True(adapter.ApplyWebCommand(LoadPayload(), true, "caoren").Ok);
        return adapter;
    }

    private static MatchRuntimeBridgeAdapter CreateGoingLiveAdapter(FakeMatchRuntimeHost? host = null)
    {
        var adapter = CreateLoadedAdapter(host);
        ApplyPayload(adapter, "observe_player", new { steamId = "76561198000000001", side = "CT", connected = true });
        ApplyPayload(adapter, "observe_player", new { steamId = "76561198100000001", side = "T", connected = true });
        ApplyPayload(adapter, "set_ready", new { steamId = "76561198000000001", ready = true });
        ApplyPayload(adapter, "set_ready", new { steamId = "76561198100000001", ready = true });
        Apply(adapter, "begin_warmup");
        Apply(adapter, "begin_ready_check");
        ApplyPayload(adapter, "begin_going_live", new { force = false, abilitySyncConfirmed = true });
        return adapter;
    }

    private static MatchCommandResult Apply(MatchRuntimeBridgeAdapter adapter, string action)
    {
        var result = adapter.ApplyWebCommand(CommandPayload(action), true, "caoren");
        Assert.True(result.Ok, result.Message);
        return result;
    }

    private static MatchCommandResult ApplyPayload(MatchRuntimeBridgeAdapter adapter, string action, object extra)
    {
        var basePayload = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["matchId"] = "match-bridge",
            ["revision"] = 3,
            ["runtimeEpoch"] = 1,
        };
        foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            basePayload[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
        return adapter.ApplyWebCommand(JsonSerializer.SerializeToElement(basePayload), true, "caoren");
    }

    private static JsonElement LoadPayload() => JsonSerializer.SerializeToElement(new
    {
        action = "load",
        matchId = "match-bridge",
        revision = 3,
        mapName = "de_mirage",
        teamAName = "A队",
        teamBName = "B队",
        teamAInitialSide = "CT",
        seats = new[]
        {
            new { seatId = "seat-a1", roster = "A", steamId = "76561198000000001", name = "A1", isCaptain = true },
            new { seatId = "seat-b1", roster = "B", steamId = "76561198100000001", name = "B1", isCaptain = true },
        },
    });

    private static JsonElement CommandPayload(string action, long runtimeEpoch = 1) => JsonSerializer.SerializeToElement(new
    {
        action,
        matchId = "match-bridge",
        revision = 3,
        runtimeEpoch,
    });

    private static MatchRuntimeSnapshot Snapshot() => new(
        "match-heartbeat",
        2,
        4,
        MatchStage.SecondHalf,
        MatchPauseState.Running,
        9,
        8,
        17,
        0,
        0,
        EngineSide.Terrorist,
        false,
        null,
        30);

    private sealed class FakeMatchRuntimeHost : IMatchRuntimeHost
    {
        public List<string> Log { get; } = [];
        public List<string> MutableDemoSegments { get; } = [];
        public IReadOnlyList<string> DemoSegments => MutableDemoSegments;

        public void ApplyFormalMatchConfig() => Log.Add("apply-config");

        public void ExecuteGoingLive() => Log.Add("going-live");

        public void SwapSides() => Log.Add("swap-sides");

        public void StartDemo(MatchDefinition definition)
        {
            Log.Add("start-demo");
            MutableDemoSegments.Add("fake.dem");
        }

        public void StopDemo() => Log.Add("stop-demo");

        public void PauseMatch() => Log.Add("pause-match");

        public void UnpauseMatch() => Log.Add("unpause-match");

        public bool RestoreFormalMatchConfig()
        {
            Log.Add("restore-config");
            return true;
        }

        public string ReadCvar(string name, string fallback) => fallback;

        public void Execute(string command) { }

        public void Pause() => Log.Add("pause-match");

        public void RestoreValveBackup(string fileName) => Log.Add("restore:" + fileName);

        public void StartDemo(string fileName) { }
    }
}
