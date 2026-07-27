namespace CaorenCupPlugin;

internal enum PluginCommandTransactionDisposition
{
    Applied,
    Rejected,
    Deferred
}

internal static class GameThreadApplicationScheduler
{
    public static Action<Action> HibernationSafeServerSchedule =>
        CounterStrikeSharp.API.Server.NextWorldUpdate;

    public static Task ScheduleAsync(Action<Action> schedule, Action application)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            schedule(() =>
            {
                try
                {
                    application();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        return completion.Task;
    }
}

internal static class WebCommandGameThreadDispatcher
{
    private static readonly HashSet<string> BlockedStateMutationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RESET_LIVE_MATCH_STATS",
        "APPLY_TEAM_ASSIGNMENTS",
        "CLEAR_TEAM_ASSIGNMENTS",
        "CONFIGURE_DUEL_MODE"
    };

    public static Task<bool> ScheduleAsync(
        Action<Action> schedule,
        PluginCommand command,
        Func<DuelControlMode> readControlMode,
        Action<PluginCommand> application)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            schedule(() =>
            {
                try
                {
                    if (IsBlocked(command, readControlMode()))
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    application(command);
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        return completion.Task;
    }

    public static Task<PluginCommandTransactionDisposition> ScheduleTransactionAsync(
        Action<Action> schedule,
        PluginCommand command,
        Func<DuelControlMode> readControlMode,
        Func<bool> readCleanupRestartPending,
        Func<bool> readCvarRestoreReady,
        Func<PluginCommand, Task<bool>> application)
    {
        var completion = new TaskCompletionSource<PluginCommandTransactionDisposition>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            schedule(() =>
            {
                try
                {
                    if (IsMatchControlPluginCommand(command))
                    {
                        if (readControlMode() == DuelControlMode.GameManaged)
                        {
                            completion.TrySetResult(PluginCommandTransactionDisposition.Rejected);
                            return;
                        }

                        if (readCleanupRestartPending() || !readCvarRestoreReady())
                        {
                            completion.TrySetResult(PluginCommandTransactionDisposition.Deferred);
                            return;
                        }
                    }

                    var applicationTask = application(command);
                    _ = CompleteApplicationAsync(applicationTask, completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        return completion.Task;
    }

    public static Task<bool> ScheduleDelayedExecution(
        Action<Action> schedule,
        Func<bool> execution)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(execution);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            schedule(() =>
            {
                try
                {
                    completion.TrySetResult(execution());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        return completion.Task;
    }

    public static bool IsGameManagedMatchControlCommand(
        string serverCommand,
        DuelControlMode controlMode)
    {
        if (controlMode != DuelControlMode.GameManaged) return false;

        return IsMatchControlServerCommand(serverCommand);
    }

    public static bool IsBlockedByPendingCvarRestore(
        PluginCommand command,
        bool hasPendingCvarRestore)
    {
        if (!hasPendingCvarRestore) return false;
        return IsMatchControlPluginCommand(command);
    }

    public static bool IsMatchControlPluginCommand(PluginCommand command)
    {
        if (BlockedStateMutationTypes.Contains(command.Type ?? string.Empty)) return true;
        if (!string.Equals(command.Type, "EXECUTE_SERVER_COMMAND", StringComparison.OrdinalIgnoreCase) ||
            command.Payload.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !command.Payload.TryGetProperty("command", out var serverCommandElement))
        {
            return false;
        }

        return IsMatchControlServerCommand(serverCommandElement.GetString()?.Trim() ?? string.Empty);
    }

    private static bool IsMatchControlServerCommand(string serverCommand)
    {
        var commandName = serverCommand
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return string.Equals(commandName, "changelevel", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "host_workshop_map", StringComparison.OrdinalIgnoreCase) ||
            commandName.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) ||
            commandName.StartsWith("sv_showimpacts", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryExecuteServerCommand(
        string serverCommand,
        Func<DuelControlMode> readControlMode,
        Func<bool> readHasPendingCvarRestore,
        Func<bool> readCleanupRestartPending,
        Action execution)
    {
        if (IsGameManagedMatchControlCommand(serverCommand, readControlMode()) ||
            ((readHasPendingCvarRestore() || readCleanupRestartPending()) &&
                IsMatchControlServerCommand(serverCommand)))
        {
            return false;
        }

        execution();
        return true;
    }

    private static bool IsBlocked(PluginCommand command, DuelControlMode controlMode)
    {
        if (controlMode != DuelControlMode.GameManaged) return false;
        return IsMatchControlPluginCommand(command);
    }

    private static async Task CompleteApplicationAsync(
        Task<bool> applicationTask,
        TaskCompletionSource<PluginCommandTransactionDisposition> completion)
    {
        try
        {
            var applied = await applicationTask;
            completion.TrySetResult(applied
                ? PluginCommandTransactionDisposition.Applied
                : PluginCommandTransactionDisposition.Deferred);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }
}
