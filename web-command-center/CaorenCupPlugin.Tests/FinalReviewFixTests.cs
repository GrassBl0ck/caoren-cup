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
                (Action)(() => executed = true)
            ]));

        Assert.True(executionResult);
        Assert.True(executed);
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
            [DuelControlMode.None, DuelLifecycle.Idle, true])));
        Assert.True(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.GameManaged, DuelLifecycle.Running, false])));
        Assert.False(Assert.IsType<bool>(method.Invoke(
            null,
            [DuelControlMode.None, DuelLifecycle.Idle, false])));
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

    private static PluginCommand CreateServerCommand(string serverCommand) => new()
    {
        Id = "server-command",
        Type = "EXECUTE_SERVER_COMMAND",
        Payload = JsonSerializer.SerializeToElement(new { command = serverCommand })
    };

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

    private static void Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName);
        Assert.NotNull(method);
        method.Invoke(target, arguments);
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
