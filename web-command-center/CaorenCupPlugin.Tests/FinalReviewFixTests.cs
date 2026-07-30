using CaorenCupPlugin;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class FinalReviewFixTests
{
    [Fact]
    public void Cleanup_restart_round_start_remains_suppressed_until_a_no_match_heartbeat()
    {
        var isolation = CreateTelemetryIsolationState();

        Invoke(isolation, "Begin", (object?)null);
        Invoke(isolation, "BeginCleanupRestart");

        Assert.True(ReadBool(isolation, "IsActive"));
        Invoke(isolation, "CompleteCleanupRoundStart");
        Assert.True(ReadBool(isolation, "IsActive"));

        Invoke(isolation, "ObserveHeartbeat", (object?)null);

        Assert.False(ReadBool(isolation, "IsActive"));
    }

    [Fact]
    public void Confirmed_takeover_does_not_release_the_stale_web_match_id()
    {
        var isolation = CreateTelemetryIsolationState();

        Invoke(isolation, "Begin", "web-match-before-duel");
        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "ObserveHeartbeat", "web-match-before-duel");
        Invoke(isolation, "CompleteCleanupRoundStart");

        Assert.True(ReadBool(isolation, "IsActive"));

        Invoke(isolation, "ObserveHeartbeat", "web-match-after-duel");

        Assert.False(ReadBool(isolation, "IsActive"));
    }

    [Fact]
    public void Duel_start_round_and_heartbeat_cannot_release_isolation_before_cleanup_begins()
    {
        var isolation = CreateTelemetryIsolationState();

        Invoke(isolation, "Begin", "web-match-before-duel");
        Invoke(isolation, "CompleteCleanupRoundStart");
        Invoke(isolation, "ObserveHeartbeat", (object?)null);

        Assert.True(ReadBool(isolation, "IsActive"));

        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "CompleteCleanupRoundStart");
        Assert.True(ReadBool(isolation, "IsActive"));
        Invoke(isolation, "ObserveHeartbeat", (object?)null);
        Assert.False(ReadBool(isolation, "IsActive"));
    }

    [Fact]
    public void Cleanup_restart_pending_is_exposed_until_its_round_start_is_consumed()
    {
        var isolation = CreateTelemetryIsolationState();
        Invoke(isolation, "Begin", "taken-over-match-a");

        Invoke(isolation, "BeginCleanupRestart");

        Assert.True(ReadBool(isolation, "CleanupRestartPending"));

        Invoke(isolation, "CompleteCleanupRoundStart");

        Assert.False(ReadBool(isolation, "CleanupRestartPending"));
    }

    [Fact]
    public void New_begin_preserves_previously_taken_over_match_id_history()
    {
        var isolation = CreateTelemetryIsolationState();
        Invoke(isolation, "Begin", "taken-over-match-a");
        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "ObserveHeartbeatState", new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b"
        });
        Invoke(isolation, "CompleteCleanupRoundStart");
        Assert.False(ReadBool(isolation, "IsActive"));

        Invoke(isolation, "Begin", (object?)null);
        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "CompleteCleanupRoundStart");
        var disposition = InvokeResult(
            isolation,
            "ObserveHeartbeatState",
            new PluginHeartbeatResponse { MatchId = "taken-over-match-a" });

        Assert.Equal("Stale", disposition?.ToString());
        Assert.True(ReadBool(isolation, "IsActive"));
    }

    [Fact]
    public void Taken_over_match_id_remains_stale_after_isolation_has_released()
    {
        var isolation = CreateTelemetryIsolationState();
        Invoke(isolation, "Begin", "taken-over-match-a");
        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "ObserveHeartbeatState", new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b"
        });
        Invoke(isolation, "CompleteCleanupRoundStart");
        Assert.False(ReadBool(isolation, "IsActive"));

        var disposition = InvokeResult(
            isolation,
            "ObserveHeartbeatState",
            new PluginHeartbeatResponse { MatchId = "taken-over-match-a" });

        Assert.Equal("Stale", disposition?.ToString());
        Assert.False(ReadBool(isolation, "IsActive"));
    }

    [Fact]
    public void Pending_cvar_restore_prevents_safe_heartbeat_release_until_recovery()
    {
        var isolation = CreateTelemetryIsolationState();
        var safeHeartbeat = new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b",
            CurrentRound = 8,
            ScoreCT = 5,
            ScoreT = 3
        };
        Invoke(isolation, "Begin", "taken-over-match-a");
        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "UpdateCvarRestoreReady", false);
        Invoke(isolation, "ObserveHeartbeatState", safeHeartbeat);
        Invoke(isolation, "CompleteCleanupRoundStart");

        Assert.False(ReadBool(isolation, "CleanupRestartPending"));
        Assert.True(ReadBool(isolation, "IsActive"));
        Assert.False(ReadBool(isolation, "HasReleasedHeartbeatState"));

        Invoke(isolation, "UpdateCvarRestoreReady", true);

        Assert.False(ReadBool(isolation, "IsActive"));
        Assert.True(ReadBool(isolation, "HasReleasedHeartbeatState"));
        var released = Assert.IsType<PluginHeartbeatResponse>(ReadProperty(
            isolation,
            "ReleasedHeartbeatState"));
        Assert.Equal("safe-match-b", released.MatchId);
        Assert.Equal(8, released.CurrentRound);
        Assert.Equal(5, released.ScoreCT);
        Assert.Equal(3, released.ScoreT);
    }

    [Fact]
    public void Later_stale_response_does_not_discard_an_already_buffered_safe_match()
    {
        var isolation = CreateTelemetryIsolationState();
        Invoke(isolation, "Begin", "taken-over-match-a");
        Invoke(isolation, "BeginCleanupRestart");
        Invoke(isolation, "ObserveHeartbeatState", new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b",
            CurrentRound = 9
        });

        var staleDisposition = InvokeResult(
            isolation,
            "ObserveHeartbeatState",
            new PluginHeartbeatResponse { MatchId = "taken-over-match-a" });
        Invoke(isolation, "CompleteCleanupRoundStart");

        Assert.Equal("Stale", staleDisposition?.ToString());
        Assert.False(ReadBool(isolation, "IsActive"));
        var released = Assert.IsType<PluginHeartbeatResponse>(ReadProperty(
            isolation,
            "ReleasedHeartbeatState"));
        Assert.Equal("safe-match-b", released.MatchId);
        Assert.Equal(9, released.CurrentRound);
    }

    [Fact]
    public async Task Web_command_parsed_before_duel_start_cannot_apply_after_the_duel_starts()
    {
        var dispatcherType = typeof(DuelGameSession).Assembly
            .GetType("CaorenCupPlugin.WebCommandGameThreadDispatcher");
        Assert.NotNull(dispatcherType);
        var scheduleMethod = dispatcherType!.GetMethod(
            "ScheduleAsync",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(scheduleMethod);

        var session = new DuelGameSession();
        using var payloadDocument = JsonDocument.Parse("{}");
        var command = new PluginCommand
        {
            Id = "configure-before-duel",
            Type = "CONFIGURE_DUEL_MODE",
            Payload = payloadDocument.RootElement.Clone()
        };
        Action? scheduledApplication = null;
        Action<Action> schedule = callback => scheduledApplication = callback;
        var applicationCalled = false;
        Action<PluginCommand> apply = _ => applicationCalled = true;

        var scheduledTask = Assert.IsType<Task<bool>>(scheduleMethod!.Invoke(
            null,
            [schedule, command, (Func<DuelControlMode>)(() => session.ControlMode), apply]));

        Assert.False(scheduledTask.IsCompleted);
        Assert.True(session.TryStart(
            [
                new DuelParticipant("t1", "T玩家", DuelTeam.Terrorist),
                new DuelParticipant("ct1", "CT玩家", DuelTeam.CounterTerrorist)
            ],
            false,
            out _));

        Assert.NotNull(scheduledApplication);
        scheduledApplication!();

        Assert.False(await scheduledTask);
        Assert.False(applicationCalled);
    }

    [Fact]
    public void Production_game_thread_scheduler_runs_during_server_hibernation()
    {
        var schedulerType = typeof(DuelGameSession).Assembly
            .GetType("CaorenCupPlugin.GameThreadApplicationScheduler");
        Assert.NotNull(schedulerType);
        var property = schedulerType!.GetProperty(
            "HibernationSafeServerSchedule",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var schedule = Assert.IsType<Action<Action>>(property!.GetValue(null));

        Assert.Equal("NextWorldUpdate", schedule.Method.Name);
        Assert.Equal("CounterStrikeSharp.API.Server", schedule.Method.DeclaringType?.FullName);
    }

    [Fact]
    public void Production_map_start_lifecycle_scheduler_runs_during_server_hibernation()
    {
        var property = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetProperty(
            "MapStartLifecycleSchedule",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var schedule = Assert.IsType<Action<Action>>(property!.GetValue(null));
        var onMapStart = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "OnMapStart",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var getterToken = BitConverter.GetBytes(property.GetMethod!.MetadataToken);
        var onMapStartIl = onMapStart?.GetMethodBody()?.GetILAsByteArray();

        Assert.Equal("NextWorldUpdate", schedule.Method.Name);
        Assert.Equal("CounterStrikeSharp.API.Server", schedule.Method.DeclaringType?.FullName);
        Assert.NotNull(onMapStartIl);
        Assert.True(onMapStartIl.AsSpan().IndexOf(getterToken) >= 0);
    }

    [Fact]
    public void Heartbeat_response_started_before_cleanup_cannot_rebind_after_a_new_response()
    {
        var order = CreateHeartbeatResponseOrder();
        var stalePreCleanupResponse = InvokeLong(order, "NextRequestSequence");
        Invoke(order, "BeginBarrier");
        var freshPostCleanupResponse = InvokeLong(order, "NextRequestSequence");

        Assert.False(InvokeBool(order, "TryAccept", stalePreCleanupResponse));
        Assert.True(InvokeBool(order, "TryAccept", freshPostCleanupResponse));
        Assert.False(InvokeBool(order, "TryAccept", stalePreCleanupResponse));
    }

    [Fact]
    public void Older_heartbeat_response_is_rejected_after_a_newer_response_was_applied()
    {
        var order = CreateHeartbeatResponseOrder();
        var olderResponse = InvokeLong(order, "NextRequestSequence");
        var newerResponse = InvokeLong(order, "NextRequestSequence");

        Assert.True(InvokeBool(order, "TryAccept", newerResponse));
        Assert.False(InvokeBool(order, "TryAccept", olderResponse));
    }

    [Fact]
    public void Heartbeat_request_generation_is_invalidated_by_the_next_barrier()
    {
        var order = CreateHeartbeatResponseOrder();
        var request = InvokeResult(order, "NextRequest");
        Assert.NotNull(request);
        var generation = Assert.IsType<long>(ReadProperty(request!, "BarrierGeneration"));

        Assert.True(InvokeBool(order, "IsCurrentGeneration", generation));

        Invoke(order, "BeginBarrier");

        Assert.False(InvokeBool(order, "IsCurrentGeneration", generation));
    }

    [Fact]
    public void Taken_over_match_id_can_be_rechecked_when_a_queued_command_is_applied()
    {
        var isolation = CreateTelemetryIsolationState();
        Invoke(isolation, "Begin", "  taken-over-match-a  ");

        Assert.True(InvokeBool(isolation, "IsStaleMatchId", "taken-over-match-a"));
        Assert.False(InvokeBool(isolation, "IsStaleMatchId", "safe-match-b"));
    }

    [Fact]
    public async Task Rejected_heartbeat_state_still_awaits_its_command_processor()
    {
        var processorType = typeof(DuelGameSession).Assembly
            .GetType("CaorenCupPlugin.HeartbeatResponseProcessor");
        Assert.NotNull(processorType);
        var processMethod = processorType!.GetMethod(
            "ProcessAsync",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(processMethod);
        var response = new PluginHeartbeatResponse
        {
            Commands =
            [
                new PluginCommand
                {
                    Id = "queued-before-barrier",
                    Type = "CONFIGURE_DUEL_MODE",
                    Payload = JsonSerializer.SerializeToElement(new { })
                }
            ]
        };
        var processedCommandIds = new List<string?>();
        Func<Task<bool>> rejectState = () => Task.FromResult(false);
        Func<PluginCommand, Task> processCommand = command =>
        {
            processedCommandIds.Add(command.Id);
            return Task.CompletedTask;
        };

        var processing = Assert.IsType<Task<bool>>(processMethod!.Invoke(
            null,
            [response, rejectState, processCommand]));

        Assert.False(await processing);
        Assert.Equal(["queued-before-barrier"], processedCommandIds);
    }

    [Fact]
    public async Task Heartbeat_processor_passes_state_disposition_to_commands_after_classification()
    {
        var response = new PluginHeartbeatResponse
        {
            MatchId = "taken-over-match-a",
            Commands = [CreateServerCommand("mp_restartgame 1", "old-a-restart")]
        };
        var events = new List<string>();
        Func<Task<HeartbeatResponseDisposition>> classifier = () =>
        {
            events.Add("state");
            return Task.FromResult(HeartbeatResponseDisposition.Stale);
        };
        Func<PluginHeartbeatResponse?, HeartbeatResponseDisposition, Task> handler =
            (state, disposition) =>
            {
                events.Add($"commands:{state?.Commands?[0].Id}:{disposition}");
                return Task.CompletedTask;
            };

        var disposition = await HeartbeatResponseProcessor.ProcessTransactionAsync(
            response,
            classifier,
            handler);

        Assert.Equal(["state", "commands:old-a-restart:Stale"], events);
        Assert.Equal(HeartbeatResponseDisposition.Stale, disposition);
    }

    [Fact]
    public void Production_heartbeat_path_owns_the_transaction_gate_and_returns_a_disposition()
    {
        var pluginType = typeof(global::CaorenCupPlugin.CaorenCupPlugin);
        var gateField = pluginType.GetField(
            "_heartbeatCommandTransactions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var classifier = pluginType.GetMethod(
            "ApplyHeartbeatStateOnGameThread",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var applyAndAck = pluginType.GetMethod(
            "TryApplyAndAckPluginCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var acknowledge = pluginType.GetMethod(
            "AckCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(gateField);
        Assert.Equal("HeartbeatCommandTransactionGate", gateField!.FieldType.Name);
        Assert.NotNull(classifier);
        Assert.Equal(typeof(HeartbeatResponseDisposition), classifier!.ReturnType);
        Assert.NotNull(applyAndAck);
        Assert.Equal(typeof(Task<PluginCommandExecutionResult>), applyAndAck!.ReturnType);
        Assert.NotNull(acknowledge);
        Assert.Equal(typeof(Task<bool>), acknowledge!.ReturnType);
    }

    [Fact]
    public async Task Expired_response_rejects_and_acks_match_control_without_applying_it()
    {
        var gate = CreateHeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            MatchId = "taken-over-match-a",
            Commands =
            [
                new PluginCommand
                {
                    Id = "old-a-configure",
                    Type = "CONFIGURE_DUEL_MODE",
                    Payload = JsonSerializer.SerializeToElement(new { })
                }
            ]
        };
        var events = new List<string>();
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = command =>
        {
            events.Add($"apply:{command.Id}");
            events.Add($"ack:{command.Id}");
            return Task.FromResult(PluginCommandExecutionResult.Completed);
        };
        Func<PluginCommand, Task<bool>> rejectAndAck = command =>
        {
            events.Add($"reject:{command.Id}");
            events.Add($"ack:{command.Id}");
            return Task.FromResult(true);
        };
        Func<PluginCommand, Task<bool>> ackOnly = _ => Task.FromResult(true);

        await InvokeTask(
            gate,
            "ProcessResponseAsync",
            response,
            ReadEnum("CaorenCupPlugin.HeartbeatResponseDisposition", "Expired"),
            applyAndAck,
            rejectAndAck,
            ackOnly,
            0L);

        Assert.Equal(["reject:old-a-configure", "ack:old-a-configure"], events);
        Assert.Equal(0, ReadInt(gate, "DeferredCount"));
    }

    [Fact]
    public async Task Safe_response_match_control_waits_then_applies_and_acks_in_order()
    {
        var gate = CreateHeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b",
            Commands =
            [
                new PluginCommand
                {
                    Id = "safe-b-configure",
                    Type = "CONFIGURE_DUEL_MODE",
                    Payload = JsonSerializer.SerializeToElement(new { })
                },
                CreateServerCommand("mp_restartgame 1", "safe-b-restart")
            ]
        };
        var events = new List<string>();
        var sources = new List<(string? MatchId, long Generation)>();
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = command =>
        {
            sources.Add((command.HeartbeatMatchId, command.BarrierGeneration));
            events.Add($"apply:{command.Id}");
            events.Add($"ack:{command.Id}");
            return Task.FromResult(PluginCommandExecutionResult.Completed);
        };
        Func<PluginCommand, Task<bool>> rejectAndAck = command =>
        {
            events.Add($"reject:{command.Id}");
            events.Add($"ack:{command.Id}");
            return Task.FromResult(true);
        };
        Func<PluginCommand, Task<bool>> ackOnly = _ => Task.FromResult(true);

        await InvokeTask(
            gate,
            "ProcessResponseAsync",
            response,
            ReadEnum("CaorenCupPlugin.HeartbeatResponseDisposition", "Deferred"),
            applyAndAck,
            rejectAndAck,
            ackOnly,
            42L);

        Assert.Empty(events);
        Assert.Equal(2, ReadInt(gate, "DeferredCount"));

        await InvokeTask(
            gate,
            "DrainAsync",
            (Func<bool>)(() => true),
            applyAndAck,
            ackOnly);

        Assert.Equal(
            [
                "apply:safe-b-configure",
                "ack:safe-b-configure",
                "apply:safe-b-restart",
                "ack:safe-b-restart"
            ],
            events);
        Assert.Equal(
            [("safe-match-b", 42L), ("safe-match-b", 42L)],
            sources);
        Assert.Equal(0, ReadInt(gate, "DeferredCount"));
    }

    [Fact]
    public async Task Retransmitted_deferred_command_is_queued_and_applied_only_once()
    {
        var gate = CreateHeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b",
            Commands = [CreateServerCommand("mp_restartgame 1", "safe-b-restart")]
        };
        var events = new List<string>();
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = command =>
        {
            events.Add($"apply:{command.Id}");
            events.Add($"ack:{command.Id}");
            return Task.FromResult(PluginCommandExecutionResult.Completed);
        };
        Func<PluginCommand, Task<bool>> rejectAndAck = _ => Task.FromResult(true);
        Func<PluginCommand, Task<bool>> ackOnly = _ => Task.FromResult(true);
        var deferred = ReadEnum("CaorenCupPlugin.HeartbeatResponseDisposition", "Deferred");

        await InvokeTask(gate, "ProcessResponseAsync", response, deferred, applyAndAck, rejectAndAck, ackOnly, 0L);
        await InvokeTask(gate, "ProcessResponseAsync", response, deferred, applyAndAck, rejectAndAck, ackOnly, 0L);

        Assert.Equal(1, ReadInt(gate, "DeferredCount"));

        await InvokeTask(gate, "DrainAsync", (Func<bool>)(() => true), applyAndAck, ackOnly);

        Assert.Equal(["apply:safe-b-restart", "ack:safe-b-restart"], events);
        Assert.Equal(0, ReadInt(gate, "DeferredCount"));
    }

    [Fact]
    public async Task Concurrent_heartbeat_command_transactions_are_serialized_in_arrival_order()
    {
        var gate = new HeartbeatCommandTransactionGate();
        var firstResponse = new PluginHeartbeatResponse
        {
            Commands = [CreateServerCommand("mp_restartgame 1", "first-command")]
        };
        var secondResponse = new PluginHeartbeatResponse
        {
            Commands = [CreateServerCommand("mp_roundtime 2", "second-command")]
        };
        var firstApplyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = async command =>
        {
            if (command.Id == "first-command")
            {
                firstApplyStarted.TrySetResult();
                await releaseFirstApply.Task;
            }
            return PluginCommandExecutionResult.Completed;
        };
        Func<PluginCommand, Task<bool>> acknowledge = _ => Task.FromResult(true);

        var first = gate.ProcessResponseAsync(
            firstResponse,
            HeartbeatResponseDisposition.Ready,
            applyAndAck,
            acknowledge,
            acknowledge);
        await firstApplyStarted.Task;
        var second = gate.ProcessResponseAsync(
            secondResponse,
            HeartbeatResponseDisposition.Deferred,
            applyAndAck,
            acknowledge,
            acknowledge);
        await Task.Yield();

        Assert.False(second.IsCompleted);

        releaseFirstApply.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, gate.DeferredCount);
    }

    [Fact]
    public async Task Delayed_server_command_completion_waits_for_the_actual_timer_callback()
    {
        Action? delayedCallback = null;
        var executionCount = 0;

        var completion = WebCommandGameThreadDispatcher.ScheduleDelayedExecution(
            callback => delayedCallback = callback,
            () =>
            {
                executionCount++;
                return true;
            });

        Assert.NotNull(delayedCallback);
        Assert.False(completion.IsCompleted);
        Assert.Equal(0, executionCount);

        delayedCallback!();

        Assert.True(await completion);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public void Production_delayed_server_command_path_returns_the_awaitable_completion()
    {
        var pluginType = typeof(global::CaorenCupPlugin.CaorenCupPlugin);
        var apply = pluginType.GetMethod(
            "ApplyPluginCommandOnGameThreadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var execute = pluginType.GetMethod(
            "ExecuteAllowedServerCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var delayed = typeof(WebCommandGameThreadDispatcher).GetMethod(
            "ScheduleDelayedExecution",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var sourceCurrent = pluginType.GetMethod(
            "IsHeartbeatCommandSourceCurrent",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(apply);
        Assert.NotNull(execute);
        Assert.NotNull(delayed);
        Assert.NotNull(sourceCurrent);
        Assert.Equal(typeof(Task<bool>), apply!.ReturnType);
        Assert.Equal(typeof(Task<bool>), execute!.ReturnType);
        Assert.Equal(typeof(HeartbeatPluginCommand), apply.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(HeartbeatPluginCommand), execute.GetParameters()[2].ParameterType);

        var applyIl = apply.GetMethodBody()?.GetILAsByteArray();
        var executeIl = execute.GetMethodBody()?.GetILAsByteArray();
        Assert.NotNull(applyIl);
        Assert.NotNull(executeIl);
        Assert.True(applyIl.AsSpan().IndexOf(BitConverter.GetBytes(execute.MetadataToken)) >= 0);
        Assert.True(executeIl.AsSpan().IndexOf(BitConverter.GetBytes(delayed!.MetadataToken)) >= 0);
        var sourceCurrentToken = BitConverter.GetBytes(sourceCurrent!.MetadataToken);
        Assert.True(applyIl.AsSpan().IndexOf(sourceCurrentToken) >= 0);
        Assert.True(executeIl.AsSpan().IndexOf(sourceCurrentToken) >= 0);
    }

    [Fact]
    public async Task Ack_failure_retries_only_ack_without_reapplying_the_command()
    {
        var gate = new HeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            Commands = [CreateServerCommand("mp_restartgame 1", "ack-retry-command")]
        };
        var applicationCount = 0;
        var ackOnlyCount = 0;
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = _ =>
        {
            applicationCount++;
            return Task.FromResult(PluginCommandExecutionResult.FinalizedAwaitingAck);
        };
        Func<PluginCommand, Task<bool>> ackOnly = _ =>
        {
            ackOnlyCount++;
            return Task.FromResult(true);
        };

        await gate.ProcessResponseAsync(
            response,
            HeartbeatResponseDisposition.Ready,
            applyAndAck,
            _ => Task.FromResult(true),
            ackOnly);

        Assert.Equal(1, applicationCount);
        Assert.Equal(1, gate.DeferredCount);

        await gate.DrainAsync(() => true, applyAndAck, ackOnly);

        Assert.Equal(1, applicationCount);
        Assert.Equal(1, ackOnlyCount);
        Assert.Equal(0, gate.DeferredCount);
    }

    [Fact]
    public async Task Deferred_command_retransmitted_by_stale_response_is_not_rejected_or_acked_early()
    {
        var gate = new HeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b",
            Commands = [CreateServerCommand("mp_restartgame 1", "deferred-retransmit")]
        };
        var staleRetransmission = new PluginHeartbeatResponse
        {
            MatchId = "taken-over-match-a",
            Commands = response.Commands
        };
        var applicationCount = 0;
        var rejectionCount = 0;
        var ackOnlyCount = 0;
        HeartbeatPluginCommand? appliedCommand = null;
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = command =>
        {
            appliedCommand = command;
            applicationCount++;
            return Task.FromResult(PluginCommandExecutionResult.Completed);
        };
        Func<PluginCommand, Task<bool>> rejectAndAck = _ =>
        {
            rejectionCount++;
            return Task.FromResult(true);
        };
        Func<PluginCommand, Task<bool>> ackOnly = _ =>
        {
            ackOnlyCount++;
            return Task.FromResult(true);
        };

        await gate.ProcessResponseAsync(
            response,
            HeartbeatResponseDisposition.Deferred,
            applyAndAck,
            rejectAndAck,
            ackOnly,
            7L);
        await gate.ProcessResponseAsync(
            staleRetransmission,
            HeartbeatResponseDisposition.Stale,
            applyAndAck,
            rejectAndAck,
            ackOnly,
            8L);

        Assert.Equal(0, applicationCount);
        Assert.Equal(0, rejectionCount);
        Assert.Equal(0, ackOnlyCount);
        Assert.Equal(1, gate.DeferredCount);

        await gate.DrainAsync(() => true, applyAndAck, ackOnly);

        Assert.Equal(1, applicationCount);
        Assert.NotNull(appliedCommand);
        Assert.Equal("safe-match-b", appliedCommand!.HeartbeatMatchId);
        Assert.Equal(7L, appliedCommand.BarrierGeneration);
        Assert.Equal(0, rejectionCount);
        Assert.Equal(0, ackOnlyCount);
        Assert.Equal(0, gate.DeferredCount);
    }

    [Fact]
    public async Task Same_batch_delayed_commands_are_all_registered_before_waiting_for_completion()
    {
        var gate = new HeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            Commands =
            [
                CreateServerCommand("mp_freezetime 3", "delayed-first"),
                CreateServerCommand("mp_roundtime 2", "delayed-second"),
                CreateServerCommand("mp_restartgame 1", "delayed-third")
            ]
        };
        var registrations = new List<string?>();
        var completions = new Dictionary<string, TaskCompletionSource<PluginCommandExecutionResult>>(
            StringComparer.Ordinal);
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = command =>
        {
            registrations.Add(command.Id);
            var completion = new TaskCompletionSource<PluginCommandExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Add(command.Id!, completion);
            return completion.Task;
        };
        Func<PluginCommand, Task<bool>> acknowledge = _ => Task.FromResult(true);

        var processing = gate.ProcessResponseAsync(
            response,
            HeartbeatResponseDisposition.Ready,
            applyAndAck,
            acknowledge,
            acknowledge);
        await Task.Yield();

        Assert.Equal(
            ["delayed-first", "delayed-second", "delayed-third"],
            registrations);

        foreach (var completion in completions.Values)
        {
            completion.TrySetResult(PluginCommandExecutionResult.Completed);
        }
        await processing;
    }

    [Fact]
    public async Task Deferred_batch_starts_every_application_before_awaiting_any_completion()
    {
        var gate = new HeartbeatCommandTransactionGate();
        var response = new PluginHeartbeatResponse
        {
            MatchId = "safe-match-b",
            Commands =
            [
                CreateServerCommand("mp_freezetime 3", "deferred-first"),
                CreateServerCommand("mp_roundtime 2", "deferred-second"),
                CreateServerCommand("mp_restartgame 1", "deferred-third")
            ]
        };
        var registrations = new List<string?>();
        var completions = new Dictionary<string, TaskCompletionSource<PluginCommandExecutionResult>>(
            StringComparer.Ordinal);
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAck = command =>
        {
            registrations.Add(command.Id);
            var completion = new TaskCompletionSource<PluginCommandExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Add(command.Id!, completion);
            return completion.Task;
        };
        Func<PluginCommand, Task<bool>> acknowledge = _ => Task.FromResult(true);

        await gate.ProcessResponseAsync(
            response,
            HeartbeatResponseDisposition.Deferred,
            applyAndAck,
            acknowledge,
            acknowledge);

        var draining = gate.DrainAsync(() => true, applyAndAck, acknowledge);
        await Task.Yield();

        Assert.Equal(
            ["deferred-first", "deferred-second", "deferred-third"],
            registrations);

        foreach (var completion in completions.Values)
        {
            completion.TrySetResult(PluginCommandExecutionResult.Completed);
        }
        await draining;
        Assert.Equal(0, gate.DeferredCount);
    }

    [Fact]
    public void Command_ack_not_found_is_terminal_but_server_errors_remain_retryable()
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "IsTerminalCommandAckStatus",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(
            null,
            [System.Net.HttpStatusCode.OK])));
        Assert.True(Assert.IsType<bool>(method.Invoke(
            null,
            [System.Net.HttpStatusCode.NotFound])));
        Assert.False(Assert.IsType<bool>(method.Invoke(
            null,
            [System.Net.HttpStatusCode.InternalServerError])));
    }

    [Fact]
    public void Safe_heartbeat_state_is_committed_only_after_cleanup_round_start_is_consumed()
    {
        var isolation = CreateTelemetryIsolationState();
        var safeHeartbeat = new PluginHeartbeatResponse
        {
            MatchId = "web-match-after-duel",
            CurrentRound = 7,
            ScoreCT = 3,
            ScoreT = 4
        };
        Invoke(isolation, "Begin", "web-match-before-duel");
        Invoke(isolation, "BeginCleanupRestart");

        Invoke(isolation, "ObserveHeartbeatState", safeHeartbeat);

        Assert.True(ReadBool(isolation, "IsActive"));
        Assert.False(ReadBool(isolation, "HasReleasedHeartbeatState"));

        Invoke(isolation, "CompleteCleanupRoundStart");

        Assert.False(ReadBool(isolation, "IsActive"));
        Assert.True(ReadBool(isolation, "HasReleasedHeartbeatState"));
        var released = Assert.IsType<PluginHeartbeatResponse>(ReadProperty(
            isolation,
            "ReleasedHeartbeatState"));
        Assert.Equal("web-match-after-duel", released.MatchId);
        Assert.Equal(7, released.CurrentRound);
        Assert.Equal(3, released.ScoreCT);
        Assert.Equal(4, released.ScoreT);
    }

    [Theory]
    [InlineData("changelevel de_dust2")]
    [InlineData("host_workshop_map 3250543760")]
    [InlineData("mp_restartgame 1")]
    [InlineData("mp_warmup_end")]
    [InlineData("mp_roundtime 1")]
    [InlineData("sv_showimpacts 1")]
    [InlineData("sv_showimpacts_time 4")]
    public void Game_managed_blocks_every_web_match_control_command(string serverCommand)
    {
        var method = GetDispatcherMethod("IsGameManagedMatchControlCommand");

        Assert.True(Assert.IsType<bool>(method.Invoke(
            null,
            [serverCommand, DuelControlMode.GameManaged])));
    }

    [Fact]
    public async Task Immediate_mp_restart_parsed_before_duel_start_is_blocked_at_application_time()
    {
        var scheduleMethod = GetDispatcherMethod("ScheduleAsync");
        var session = new DuelGameSession();
        var command = CreateServerCommand("mp_restartgame 1");
        Action? scheduledApplication = null;
        var applicationCalled = false;

        var scheduledTask = Assert.IsType<Task<bool>>(scheduleMethod.Invoke(
            null,
            [
                (Action<Action>)(callback => scheduledApplication = callback),
                command,
                (Func<DuelControlMode>)(() => session.ControlMode),
                (Action<PluginCommand>)(_ => applicationCalled = true)
            ]));

        Assert.True(session.TryStart(
            [
                new DuelParticipant("t1", "T玩家", DuelTeam.Terrorist),
                new DuelParticipant("ct1", "CT玩家", DuelTeam.CounterTerrorist)
            ],
            false,
            out _));
        Assert.NotNull(scheduledApplication);
        scheduledApplication!();

        Assert.False(await scheduledTask);
        Assert.False(applicationCalled);
    }

    [Fact]
    public async Task Cleanup_pending_defers_web_match_control_without_applying_it()
    {
        var scheduleMethod = GetDispatcherMethod("ScheduleTransactionAsync");
        var command = CreateServerCommand("mp_restartgame 1", "cleanup-pending-restart");
        Action? scheduledApplication = null;
        var applicationCalled = false;

        var scheduledTask = Assert.IsAssignableFrom<Task>(scheduleMethod.Invoke(
            null,
            [
                (Action<Action>)(callback => scheduledApplication = callback),
                command,
                (Func<DuelControlMode>)(() => DuelControlMode.None),
                (Func<bool>)(() => true),
                (Func<bool>)(() => true),
                (Func<PluginCommand, Task<bool>>)(_ =>
                {
                    applicationCalled = true;
                    return Task.FromResult(true);
                })
            ]));

        Assert.NotNull(scheduledApplication);
        scheduledApplication!();
        await scheduledTask;

        Assert.Equal("Deferred", ReadTaskResult(scheduledTask)?.ToString());
        Assert.False(applicationCalled);
    }

    [Fact]
    public async Task Pending_cvar_restore_defers_web_match_control_without_applying_it()
    {
        var scheduleMethod = GetDispatcherMethod("ScheduleTransactionAsync");
        var command = CreateServerCommand("mp_restartgame 1", "cvar-pending-restart");
        Action? scheduledApplication = null;
        var applicationCalled = false;

        var scheduledTask = Assert.IsAssignableFrom<Task>(scheduleMethod.Invoke(
            null,
            [
                (Action<Action>)(callback => scheduledApplication = callback),
                command,
                (Func<DuelControlMode>)(() => DuelControlMode.None),
                (Func<bool>)(() => false),
                (Func<bool>)(() => false),
                (Func<PluginCommand, Task<bool>>)(_ =>
                {
                    applicationCalled = true;
                    return Task.FromResult(true);
                })
            ]));

        Assert.NotNull(scheduledApplication);
        scheduledApplication!();
        await scheduledTask;

        Assert.Equal("Deferred", ReadTaskResult(scheduledTask)?.ToString());
        Assert.False(applicationCalled);
    }

    [Fact]
    public void Delayed_match_control_callback_rechecks_game_managed_mode_before_execution()
    {
        var tryExecuteMethod = GetDispatcherMethod("TryExecuteServerCommand");
        var session = new DuelGameSession();
        var executed = false;
        bool? executionResult = null;
        Action delayedCallback = () => executionResult = Assert.IsType<bool>(tryExecuteMethod.Invoke(
            null,
            [
                "sv_showimpacts_time 4",
                (Func<DuelControlMode>)(() => session.ControlMode),
                (Func<bool>)(() => false),
                (Func<bool>)(() => false),
                (Action)(() => executed = true)
            ]));

        Assert.True(session.TryStart(
            [
                new DuelParticipant("t1", "T玩家", DuelTeam.Terrorist),
                new DuelParticipant("ct1", "CT玩家", DuelTeam.CounterTerrorist)
            ],
            false,
            out _));
        delayedCallback();

        Assert.False(executionResult);
        Assert.False(executed);
    }

    [Fact]
    public void Delayed_match_control_callback_rechecks_pending_cvar_restore_before_execution()
    {
        var tryExecuteMethod = GetDispatcherMethod("TryExecuteServerCommand");
        var executed = false;
        var pendingCvarRestore = true;

        var executionResult = Assert.IsType<bool>(tryExecuteMethod.Invoke(
            null,
            [
                "mp_restartgame 1",
                (Func<DuelControlMode>)(() => DuelControlMode.None),
                (Func<bool>)(() => pendingCvarRestore),
                (Func<bool>)(() => false),
                (Action)(() => executed = true)
            ]));

        Assert.False(executionResult);
        Assert.False(executed);

        pendingCvarRestore = false;
        executionResult = Assert.IsType<bool>(tryExecuteMethod.Invoke(
            null,
            [
                "mp_restartgame 1",
                (Func<DuelControlMode>)(() => DuelControlMode.None),
                (Func<bool>)(() => pendingCvarRestore),
                (Func<bool>)(() => false),
                (Action)(() => executed = true)
            ]));

        Assert.True(executionResult);
        Assert.True(executed);
    }

    [Fact]
    public void Delayed_match_control_callback_rechecks_cleanup_restart_before_execution()
    {
        var tryExecuteMethod = GetDispatcherMethod("TryExecuteServerCommand");
        var executed = false;

        var executionResult = Assert.IsType<bool>(tryExecuteMethod.Invoke(
            null,
            [
                "mp_restartgame 1",
                (Func<DuelControlMode>)(() => DuelControlMode.None),
                (Func<bool>)(() => false),
                (Func<bool>)(() => true),
                (Action)(() => executed = true)
            ]));

        Assert.False(executionResult);
        Assert.False(executed);
    }

    [Fact]
    public void Failed_cvar_restore_remains_pending_and_retry_skips_already_restored_entries()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var executedCommands = new List<string>();
        var failMaxRoundsRestoreOnce = true;
        Action<string> execute = serverCommand =>
        {
            executedCommands.Add(serverCommand);
            if (serverCommand == "mp_maxrounds 24" && failMaxRoundsRestoreOnce)
            {
                failMaxRoundsRestoreOnce = false;
                throw new InvalidOperationException("transient restore failure");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((name, fallback) => name == "mp_maxrounds" ? "24" : "0"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");
        scope.Set("mp_winlimit", "1", "0");

        Assert.Throws<AggregateException>(scope.RestoreAll);

        Assert.Equal(1, ReadInt(scope, "PendingRestoreCount"));
        Assert.Contains("mp_maxrounds", ReadStringCollection(scope, "PendingRestoreNames"));
        Assert.DoesNotContain("mp_winlimit", ReadStringCollection(scope, "PendingRestoreNames"));

        scope.RestoreAll();

        Assert.Equal(0, ReadInt(scope, "PendingRestoreCount"));
        Assert.Equal(2, executedCommands.Count(command => command == "mp_maxrounds 24"));
        Assert.Equal(1, executedCommands.Count(command => command == "mp_winlimit 0"));
    }

    [Fact]
    public void Duel_runtime_cvars_are_applied_and_restored_as_one_scope()
    {
        var executed = new List<string>();
        var scope = CreateCvarScope((name, fallback) => name switch
        {
            "mp_weapons_allow_map_placed" => "1",
            "mp_death_drop_gun" => "1",
            "mp_maxrounds" => "24",
            "sv_showimpacts" => "1",
            "sv_showimpacts_time" => "4",
            "mp_endmatch_votenextmap" => "1",
            "mp_match_end_restart" => "0",
            _ => fallback
        }, executed.Add);

        scope.Apply(DuelRuntimePolicy.BuildCvarPlan(new DuelGameConfig()));
        Assert.Contains("mp_maxrounds 36", executed);
        Assert.Contains("mp_weapons_allow_map_placed 0", executed);
        Assert.Contains("mp_death_drop_gun 0", executed);
        Assert.Contains("sv_showimpacts 0", executed);
        Assert.Contains("sv_showimpacts_time 0", executed);
        Assert.Contains("mp_endmatch_votenextmap 0", executed);
        Assert.Contains("mp_match_end_restart 1", executed);

        scope.RestoreAll();
        Assert.Contains("mp_maxrounds 24", executed);
        Assert.Contains("mp_weapons_allow_map_placed 1", executed);
        Assert.Contains("mp_death_drop_gun 1", executed);
        Assert.Contains("sv_showimpacts 1", executed);
        Assert.Contains("sv_showimpacts_time 4", executed);
        Assert.Contains("mp_endmatch_votenextmap 1", executed);
        Assert.Contains("mp_match_end_restart 0", executed);
    }

    [Fact]
    public void Both_duel_control_modes_use_the_shared_runtime_activation_path()
    {
        var source = ReadPluginSource();
        Assert.Contains("private void ActivateDuelRuntime(DuelGameConfig config)", source);
        Assert.Contains("DuelRuntimePolicy.BuildWebManagedCvarPlan(config)", source);
        Assert.Contains("DuelRuntimePolicy.BuildCvarPlan(config)", source);
        Assert.Contains("ReadPayloadDouble(payload, \"roundTimeMinutes\", 1)", source);
        Assert.Contains("_duelSession.EnterWebManaged(config);", source);
        Assert.DoesNotContain(
            "_duelServerCvars.Set(\"mp_maxrounds\", config.TotalRounds.ToString()",
            source);
    }

    [Fact]
    public void Duel_weapon_rules_avoid_direct_entity_removal_and_use_delayed_retry()
    {
        var source = ReadPluginSource();
        Assert.Contains("AddCommandListener(\"drop\", OnDuelDropCommand, HookMode.Pre)", source);
        Assert.DoesNotContain("RemoveUnexpectedDuelFirearms", source);
        Assert.DoesNotContain("weapon.Remove()", source);
        Assert.Contains("QueuePreferredDuelWeapon(player, rule)", source);
        Assert.Contains("AddTimer(0.1f", source);
        Assert.Contains("allowRetry: false", source);
    }

    [Fact]
    public void Duel_kevlar_only_marks_networked_armor_state_changed()
    {
        var source = ReadPluginSource();
        var giveKevlar = SliceSource(
            source,
            "private static void GivePlayerKevlar(",
            "private static bool IsDuelSniperWeapon(");
        Assert.Contains("ItemServices?.As<CCSPlayer_ItemServices>()", giveKevlar);
        Assert.Contains("itemServices.HasHelmet = false", giveKevlar);
        Assert.Contains("player.PawnHasHelmet = false", giveKevlar);
        Assert.Contains("Utilities.SetStateChanged(player, \"CCSPlayerController\", \"m_bPawnHasHelmet\")", giveKevlar);
        Assert.Contains("Utilities.SetStateChanged(pawn, \"CCSPlayerPawn\", \"m_ArmorValue\")", giveKevlar);
        Assert.DoesNotContain("m_bHasHelmet", giveKevlar);
    }

    [Fact]
    public void Duel_final_round_waits_for_native_same_map_restart_then_cleans_up()
    {
        var source = ReadPluginSource();
        var roundEnd = SliceSource(source, "public HookResult OnRoundEnd(", "private void FinishGameManagedDuel(");
        var finalEventIndex = roundEnd.IndexOf("QueueEvent(\"round_end\"", StringComparison.Ordinal);
        var finalSnapshotIndex = roundEnd.IndexOf("QueueSnapshot()", StringComparison.Ordinal);
        var webCleanupIndex = roundEnd.IndexOf(
            "BeginDuelCleanup(DuelControlMode.WebManaged)",
            StringComparison.Ordinal);
        Assert.True(finalEventIndex >= 0);
        Assert.True(finalSnapshotIndex > finalEventIndex);
        Assert.True(webCleanupIndex > finalSnapshotIndex);
        Assert.Contains("if (wasGameManaged) return HookResult.Continue;", roundEnd);
        Assert.DoesNotContain("RestoreGameManagedDuelCvarsWithRetry", roundEnd);

        var finishGameManaged = SliceSource(
            source,
            "private void FinishGameManagedDuel(",
            "private void AbortGameManagedDuel(");
        Assert.Contains(
            "BeginDuelCleanup(DuelControlMode.GameManaged, waitForEngineRestart: true)",
            finishGameManaged);

        var beginCleanup = SliceSource(source, "private void BeginDuelCleanup(", "private void CompleteDuelCleanupAfterRestart(");
        Assert.Contains("if (waitForEngineRestart) return;", beginCleanup);
        Assert.Contains("Server.ExecuteCommand(\"mp_restartgame 1\")", beginCleanup);
        Assert.DoesNotContain("RestoreGameManagedDuelCvarsWithRetry", beginCleanup);

        var roundStart = SliceSource(source, "public HookResult OnRoundStart(", "public HookResult OnPlayerDeath(");
        Assert.True(
            roundStart.IndexOf("CompleteDuelCleanupAfterRestart(cleanupMode)", StringComparison.Ordinal) <
            roundStart.IndexOf("_currentRound++", StringComparison.Ordinal));

        var completeCleanup = SliceSource(
            source,
            "private void CompleteDuelCleanupAfterRestart(",
            "private void RestoreGameManagedDuelCvarsWithRetry(");
        Assert.Contains("RestoreGameManagedDuelCvarsWithRetry()", completeCleanup);

        var unload = SliceSource(source, "public override void Unload(", "private void StopTimers(");
        Assert.Contains("CleanupDuelImmediately()", unload);
        var mapStart = SliceSource(source, "private void OnMapStart(", "private void ClearLobbyReminderState(");
        Assert.Contains("immediateRestore: true", mapStart);
        Assert.Contains("CleanupDuelImmediately(DuelControlMode.WebManaged)", mapStart);
    }

    [Fact]
    public void Cvar_cleanup_retry_is_bounded_and_keeps_unresolved_entries_pending()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var restoreAttempts = 0;
        Action<string> execute = serverCommand =>
        {
            if (serverCommand == "mp_maxrounds 24")
            {
                restoreAttempts++;
                throw new InvalidOperationException("persistent restore failure");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((_, _) => "24"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");
        var failures = new List<AggregateException>();
        var retryMethod = typeof(DuelServerCvarScope).GetMethod("TryRestoreAll");
        Assert.NotNull(retryMethod);

        var restored = Assert.IsType<bool>(retryMethod!.Invoke(
            scope,
            [3, (Action<AggregateException>)(failure => failures.Add(failure))]));

        Assert.False(restored);
        Assert.Equal(3, restoreAttempts);
        Assert.Equal(3, failures.Count);
        Assert.Equal(1, ReadInt(scope, "PendingRestoreCount"));
    }

    [Fact]
    public void Periodic_safe_point_retries_cvars_after_bounded_cleanup_attempts()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var restoreAttempts = 0;
        Action<string> execute = serverCommand =>
        {
            if (serverCommand != "mp_maxrounds 24") return;
            restoreAttempts++;
            if (restoreAttempts <= 3)
            {
                throw new InvalidOperationException("cleanup-frame restore failure");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((_, _) => "24"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");

        Assert.False(scope.TryRestoreAll(3));
        Assert.False(scope.IsReadyForNewDuel);

        Assert.True(scope.RetryPendingAtSafePoint());

        Assert.True(scope.IsReadyForNewDuel);
        Assert.Equal(4, restoreAttempts);
        Assert.Equal(0, scope.PendingRestoreCount);
    }

    [Fact]
    public void Active_duel_cvar_scope_is_not_treated_as_cleanup_restore_work()
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "ShouldRetryPendingDuelCvars",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.False(Assert.IsType<bool>(method!.Invoke(null, [true, true])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [false, true])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [false, false])));
    }

    [Fact]
    public void New_duel_remains_blocked_until_every_pending_cvar_is_restored()
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);

        var restoreMaySucceed = false;
        Action<string> execute = serverCommand =>
        {
            if (serverCommand == "mp_maxrounds 24" && !restoreMaySucceed)
            {
                throw new InvalidOperationException("restore still unavailable");
            }
        };
        var scope = Assert.IsType<DuelServerCvarScope>(constructor!.Invoke(
            [
                (Func<string, string, string>)((_, _) => "24"),
                execute
            ]));
        scope.Set("mp_maxrounds", "36", "24");
        Assert.False(scope.TryRestoreAll(3));

        Assert.False(scope.IsReadyForNewDuel);
        Assert.False(scope.RetryPendingAtSafePoint());
        Assert.False(scope.IsReadyForNewDuel);

        restoreMaySucceed = true;
        Assert.True(scope.RetryPendingAtSafePoint());
        Assert.True(scope.IsReadyForNewDuel);
    }

    [Fact]
    public void Pending_cvar_restore_blocks_new_web_match_configuration_and_restart()
    {
        var method = GetDispatcherMethod("IsBlockedByPendingCvarRestore");
        using var payloadDocument = JsonDocument.Parse("{}");
        var configure = new PluginCommand
        {
            Id = "configure-next-match",
            Type = "CONFIGURE_DUEL_MODE",
            Payload = payloadDocument.RootElement.Clone()
        };
        var restart = CreateServerCommand("mp_restartgame 1");

        Assert.True(Assert.IsType<bool>(method.Invoke(null, [configure, true])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [restart, true])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [configure, false])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [restart, false])));
    }

    [Fact]
    public void Pending_cvar_restore_blocks_game_admin_map_change()
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "IsDuelMapChangeBlocked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, true, false])));
        Assert.True(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.GameManaged, DuelLifecycle.Running, false, false])));
        Assert.False(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false, false])));
    }

    [Fact]
    public void Cleanup_pending_blocks_new_game_admin_start()
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "IsDuelStartBlocked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(null, [true, true])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [false, false])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [false, true])));
    }

    [Fact]
    public void Cleanup_pending_blocks_game_admin_map_change()
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "IsDuelMapChangeBlocked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.True(Assert.IsType<bool>(method!.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false, true])));
        Assert.False(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false, false])));
    }

    [Fact]
    public void Game_managed_status_includes_participants_stage_and_remaining_rounds()
    {
        var session = new DuelGameSession(new DuelGameConfig(1, 1, 28, 1, "none"));
        Assert.True(session.TryStart(
            [
                new DuelParticipant("t1", "T甲", DuelTeam.Terrorist),
                new DuelParticipant("t2", "T乙", DuelTeam.Terrorist),
                new DuelParticipant("ct1", "CT丙", DuelTeam.CounterTerrorist)
            ],
            false,
            out _));
        session.MarkRoundStarted();
        session.RecordRoundEnd(DuelTeam.Terrorist);
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "BuildGameManagedDuelStatusLines",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var lines = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(method!.Invoke(null, [session]));
        var output = string.Join("\n", lines);

        Assert.Contains("T 参赛者（2）：T甲、T乙", output);
        Assert.Contains("CT 参赛者（1）：CT丙", output);
        Assert.Contains("当前阶段：步枪", output);
        Assert.Contains("剩余 29 回合", output);
    }

    [Fact]
    public void Admin_help_states_the_recommended_setup_order_and_confirmed_web_takeover()
    {
        var method = typeof(global::CaorenCupPlugin.CaorenCupPlugin).GetMethod(
            "BuildDuelAdminHelpLines",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var lines = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(method!.Invoke(null, null));
        var output = string.Join("\n", lines);

        var mapIndex = output.IndexOf("先切换地图", StringComparison.Ordinal);
        var reconnectIndex = output.IndexOf("等待玩家重连并选择 T/CT", StringComparison.Ordinal);
        var configureIndex = output.IndexOf("再配置", StringComparison.Ordinal);
        var startIndex = output.IndexOf("最后 /duel start", StringComparison.Ordinal);
        Assert.True(mapIndex >= 0);
        Assert.True(reconnectIndex > mapIndex);
        Assert.True(configureIndex > reconnectIndex);
        Assert.True(startIndex > configureIndex);
        Assert.Contains("/duel start confirm", output);
        Assert.Contains("替换现有网页管理状态", output);
    }

    private static PluginCommand CreateServerCommand(
        string serverCommand,
        string commandId = "server-command") => new()
    {
        Id = commandId,
        Type = "EXECUTE_SERVER_COMMAND",
        Payload = JsonSerializer.SerializeToElement(new { command = serverCommand })
    };

    private static DuelServerCvarScope CreateCvarScope(
        Func<string, string, string> read,
        Action<string> execute)
    {
        var constructor = typeof(DuelServerCvarScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(Func<string, string, string>), typeof(Action<string>)],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<DuelServerCvarScope>(constructor!.Invoke([read, execute]));
    }

    private static string ReadPluginSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "CaorenCupPlugin",
                "CaorenCupPlugin.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(
            "Could not locate CaorenCupPlugin.cs from the test output directory.");
    }

    private static string SliceSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker after {startMarker}: {endMarker}");
        return source[start..end];
    }

    private static MethodInfo GetDispatcherMethod(string methodName)
    {
        var type = typeof(DuelGameSession).Assembly.GetType("CaorenCupPlugin.WebCommandGameThreadDispatcher");
        Assert.NotNull(type);
        var method = type!.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!;
    }

    private static object CreateTelemetryIsolationState()
    {
        var type = typeof(DuelGameSession).Assembly.GetType("CaorenCupPlugin.DuelTelemetryIsolationState");
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!);
        Assert.NotNull(instance);
        return instance!;
    }

    private static object CreateHeartbeatResponseOrder()
    {
        var type = typeof(DuelGameSession).Assembly.GetType("CaorenCupPlugin.HeartbeatResponseOrder");
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!);
        Assert.NotNull(instance);
        return instance!;
    }

    private static object CreateHeartbeatCommandTransactionGate()
    {
        var type = typeof(DuelGameSession).Assembly
            .GetType("CaorenCupPlugin.HeartbeatCommandTransactionGate");
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!);
        Assert.NotNull(instance);
        return instance!;
    }

    private static object ReadEnum(string typeName, string value)
    {
        var type = typeof(DuelGameSession).Assembly.GetType(typeName);
        Assert.NotNull(type);
        return Enum.Parse(type!, value);
    }

    private static async Task InvokeTask(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(target, arguments));
        await task;
    }

    private static object? ReadTaskResult(Task task)
    {
        var property = task.GetType().GetProperty("Result");
        Assert.NotNull(property);
        return property!.GetValue(task);
    }

    private static void Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName);
        Assert.NotNull(method);
        method.Invoke(target, arguments);
    }

    private static object? InvokeResult(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName);
        Assert.NotNull(method);
        return method!.Invoke(target, arguments);
    }

    private static bool ReadBool(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property.GetValue(target));
    }

    private static long InvokeLong(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName);
        Assert.NotNull(method);
        return Assert.IsType<long>(method!.Invoke(target, arguments));
    }

    private static bool InvokeBool(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(target, arguments));
    }

    private static int ReadInt(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(target));
    }

    private static IReadOnlyCollection<string> ReadStringCollection(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IReadOnlyCollection<string>>(property.GetValue(target));
    }

    private static object? ReadProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return property!.GetValue(target);
    }
}
