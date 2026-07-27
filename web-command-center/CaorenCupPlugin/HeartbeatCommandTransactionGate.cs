namespace CaorenCupPlugin;

internal enum HeartbeatResponseDisposition
{
    Ready,
    Deferred,
    Expired,
    Stale
}

internal enum PluginCommandExecutionResult
{
    Completed,
    Deferred,
    FinalizedAwaitingAck
}

internal sealed class HeartbeatCommandTransactionGate
{
    private const int CompletedCommandCacheLimit = 1024;
    private readonly Queue<PendingPluginCommand> _deferred = new();
    private readonly Dictionary<string, PendingPluginCommand> _deferredById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlightIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _completedIdOrder = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _transactionLock = new(1, 1);

    public int DeferredCount
    {
        get
        {
            lock (_sync) return _deferred.Count;
        }
    }

    public async Task ProcessResponseAsync(
        PluginHeartbeatResponse? response,
        HeartbeatResponseDisposition disposition,
        Func<PluginCommand, Task<PluginCommandExecutionResult>> applyAndAckAsync,
        Func<PluginCommand, Task<bool>> rejectAndAckAsync,
        Func<PluginCommand, Task<bool>> ackOnlyAsync)
    {
        ArgumentNullException.ThrowIfNull(applyAndAckAsync);
        ArgumentNullException.ThrowIfNull(rejectAndAckAsync);
        ArgumentNullException.ThrowIfNull(ackOnlyAsync);
        if (response?.Commands is not { Count: > 0 }) return;

        await _transactionLock.WaitAsync();
        try
        {
            var startedApplications = new List<StartedPluginCommand>();
            foreach (var command in response.Commands)
            {
                if (IsPendingOrInFlight(command)) continue;
                if (IsCompleted(command))
                {
                    if (!await ackOnlyAsync(command))
                    {
                        Enqueue(command, PendingPluginCommandState.FinalizedAwaitingAck);
                    }
                    continue;
                }

                if (!WebCommandGameThreadDispatcher.IsMatchControlPluginCommand(command))
                {
                    StartApplication(command, applyAndAckAsync, startedApplications);
                    continue;
                }

                if (disposition is HeartbeatResponseDisposition.Expired or HeartbeatResponseDisposition.Stale)
                {
                    if (await rejectAndAckAsync(command))
                    {
                        MarkCompleted(command);
                    }
                    else
                    {
                        Enqueue(command, PendingPluginCommandState.FinalizedAwaitingAck);
                    }
                    continue;
                }

                if (disposition == HeartbeatResponseDisposition.Deferred || DeferredCount > 0)
                {
                    Enqueue(command, PendingPluginCommandState.PendingApplication);
                    continue;
                }

                StartApplication(command, applyAndAckAsync, startedApplications);
            }

            foreach (var started in startedApplications)
            {
                try
                {
                    StoreResult(started.Command, await started.Completion);
                }
                finally
                {
                    ClearInFlight(started.Command);
                }
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    public async Task DrainAsync(
        Func<bool> isReady,
        Func<PluginCommand, Task<PluginCommandExecutionResult>> applyAndAckAsync,
        Func<PluginCommand, Task<bool>> ackOnlyAsync)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(applyAndAckAsync);
        ArgumentNullException.ThrowIfNull(ackOnlyAsync);

        await _transactionLock.WaitAsync();
        try
        {
            while (true)
            {
                PendingPluginCommand? pending;
                lock (_sync)
                {
                    pending = _deferred.Count > 0 ? _deferred.Peek() : null;
                }

                if (pending == null) return;
                if (pending.State == PendingPluginCommandState.FinalizedAwaitingAck)
                {
                    if (!await ackOnlyAsync(pending.Command)) return;
                    DequeueCompleted(pending);
                    continue;
                }

                if (!isReady()) return;
                var result = await applyAndAckAsync(pending.Command);
                if (result == PluginCommandExecutionResult.Deferred) return;
                if (result == PluginCommandExecutionResult.FinalizedAwaitingAck)
                {
                    pending.State = PendingPluginCommandState.FinalizedAwaitingAck;
                    return;
                }

                DequeueCompleted(pending);
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    private bool IsPendingOrInFlight(PluginCommand command)
    {
        var commandId = NormalizeCommandId(command.Id);
        if (commandId == null) return false;
        lock (_sync)
        {
            return _deferredById.ContainsKey(commandId) || _inFlightIds.Contains(commandId);
        }
    }

    private bool IsCompleted(PluginCommand command)
    {
        var commandId = NormalizeCommandId(command.Id);
        if (commandId == null) return false;
        lock (_sync) return _completedIds.Contains(commandId);
    }

    private void StoreResult(PluginCommand command, PluginCommandExecutionResult result)
    {
        switch (result)
        {
            case PluginCommandExecutionResult.Completed:
                MarkCompleted(command);
                break;
            case PluginCommandExecutionResult.Deferred:
                Enqueue(command, PendingPluginCommandState.PendingApplication);
                break;
            case PluginCommandExecutionResult.FinalizedAwaitingAck:
                Enqueue(command, PendingPluginCommandState.FinalizedAwaitingAck);
                break;
        }
    }

    private void StartApplication(
        PluginCommand command,
        Func<PluginCommand, Task<PluginCommandExecutionResult>> applyAndAckAsync,
        List<StartedPluginCommand> startedApplications)
    {
        var commandId = NormalizeCommandId(command.Id);
        if (commandId != null)
        {
            lock (_sync)
            {
                if (!_inFlightIds.Add(commandId)) return;
            }
        }

        try
        {
            startedApplications.Add(new StartedPluginCommand(
                command,
                applyAndAckAsync(command)));
        }
        catch
        {
            ClearInFlight(command);
            throw;
        }
    }

    private void ClearInFlight(PluginCommand command)
    {
        var commandId = NormalizeCommandId(command.Id);
        if (commandId == null) return;
        lock (_sync) _inFlightIds.Remove(commandId);
    }

    private void Enqueue(PluginCommand command, PendingPluginCommandState state)
    {
        lock (_sync)
        {
            var commandId = NormalizeCommandId(command.Id);
            if (commandId != null && _deferredById.ContainsKey(commandId)) return;

            var pending = new PendingPluginCommand(command, state);
            _deferred.Enqueue(pending);
            if (commandId != null) _deferredById[commandId] = pending;
        }
    }

    private void DequeueCompleted(PendingPluginCommand pending)
    {
        lock (_sync)
        {
            var dequeued = _deferred.Dequeue();
            if (!ReferenceEquals(dequeued, pending))
            {
                throw new InvalidOperationException("Deferred plugin command order changed unexpectedly.");
            }

            var commandId = NormalizeCommandId(pending.Command.Id);
            if (commandId != null) _deferredById.Remove(commandId);
            MarkCompletedUnsafe(pending.Command);
        }
    }

    private void MarkCompleted(PluginCommand command)
    {
        lock (_sync) MarkCompletedUnsafe(command);
    }

    private void MarkCompletedUnsafe(PluginCommand command)
    {
        var commandId = NormalizeCommandId(command.Id);
        if (commandId == null || !_completedIds.Add(commandId)) return;

        _completedIdOrder.Enqueue(commandId);
        while (_completedIdOrder.Count > CompletedCommandCacheLimit)
        {
            _completedIds.Remove(_completedIdOrder.Dequeue());
        }
    }

    private static string? NormalizeCommandId(string? commandId) =>
        string.IsNullOrWhiteSpace(commandId) ? null : commandId.Trim();

    private sealed class PendingPluginCommand(
        PluginCommand command,
        PendingPluginCommandState state)
    {
        public PluginCommand Command { get; } = command;

        public PendingPluginCommandState State { get; set; } = state;
    }

    private sealed record StartedPluginCommand(
        PluginCommand Command,
        Task<PluginCommandExecutionResult> Completion);

    private enum PendingPluginCommandState
    {
        PendingApplication,
        FinalizedAwaitingAck
    }
}
