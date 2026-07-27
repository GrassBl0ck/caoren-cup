namespace CaorenCupPlugin;

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
        Action execution)
    {
        if (IsGameManagedMatchControlCommand(serverCommand, readControlMode()) ||
            (readHasPendingCvarRestore() && IsMatchControlServerCommand(serverCommand)))
        {
            return false;
        }

        execution();
        return true;
    }

    private static bool IsBlocked(PluginCommand command, DuelControlMode controlMode)
    {
        if (controlMode != DuelControlMode.GameManaged) return false;
        if (BlockedStateMutationTypes.Contains(command.Type ?? string.Empty)) return true;
        if (!string.Equals(command.Type, "EXECUTE_SERVER_COMMAND", StringComparison.OrdinalIgnoreCase) ||
            command.Payload.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !command.Payload.TryGetProperty("command", out var serverCommandElement))
        {
            return false;
        }

        return IsGameManagedMatchControlCommand(
            serverCommandElement.GetString()?.Trim() ?? string.Empty,
            controlMode);
    }
}
