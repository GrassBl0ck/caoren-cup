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

internal sealed record HeartbeatPluginCommand(
    PluginCommand Command,
    string? HeartbeatMatchId,
    long BarrierGeneration)
{
    public string? Id => Command.Id;

    public string? Type => Command.Type;
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
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAckAsync,
        Func<PluginCommand, Task<bool>> rejectAndAckAsync,
        Func<PluginCommand, Task<bool>> ackOnlyAsync,
        long barrierGeneration = 0)
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
                var heartbeatCommand = new HeartbeatPluginCommand(
                    command,
                    NormalizeMatchId(response.MatchId),
                    barrierGeneration);
                if (IsPendingOrInFlight(command)) continue;
                if (IsCompleted(command))
                {
                    if (!await ackOnlyAsync(command))
                    {
                        Enqueue(heartbeatCommand, PendingPluginCommandState.FinalizedAwaitingAck);
                    }
                    continue;
                }

                if (!WebCommandGameThreadDispatcher.IsMatchControlPluginCommand(command))
                {
                    StartApplication(heartbeatCommand, applyAndAckAsync, startedApplications);
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
                        Enqueue(heartbeatCommand, PendingPluginCommandState.FinalizedAwaitingAck);
                    }
                    continue;
                }

                if (disposition == HeartbeatResponseDisposition.Deferred || DeferredCount > 0)
                {
                    Enqueue(heartbeatCommand, PendingPluginCommandState.PendingApplication);
                    continue;
                }

                StartApplication(heartbeatCommand, applyAndAckAsync, startedApplications);
            }

            foreach (var started in startedApplications)
            {
                try
                {
                    StoreResult(started.HeartbeatCommand, await started.Completion);
                }
                finally
                {
                    ClearInFlight(started.HeartbeatCommand.Command);
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
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAckAsync,
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
                if (pending.State == PendingPluginCommandState.Completed)
                {
                    DequeueCompleted(pending);
                    continue;
                }

                if (pending.State == PendingPluginCommandState.FinalizedAwaitingAck)
                {
                    if (!await ackOnlyAsync(pending.HeartbeatCommand.Command)) return;
                    DequeueCompleted(pending);
                    continue;
                }

                if (!isReady()) return;
                var pendingBatch = SnapshotPendingApplicationBatch();
                var startedBatch = new List<StartedPendingPluginCommand>(pendingBatch.Count);
                foreach (var queuedCommand in pendingBatch)
                {
                    startedBatch.Add(new StartedPendingPluginCommand(
                        queuedCommand,
                        applyAndAckAsync(queuedCommand.HeartbeatCommand)));
                }

                var hasUnfinishedCommand = false;
                foreach (var started in startedBatch)
                {
                    var result = await started.Completion;
                    switch (result)
                    {
                        case PluginCommandExecutionResult.Completed:
                            started.Pending.State = PendingPluginCommandState.Completed;
                            break;
                        case PluginCommandExecutionResult.Deferred:
                            hasUnfinishedCommand = true;
                            break;
                        case PluginCommandExecutionResult.FinalizedAwaitingAck:
                            started.Pending.State = PendingPluginCommandState.FinalizedAwaitingAck;
                            hasUnfinishedCommand = true;
                            break;
                    }
                }

                if (hasUnfinishedCommand) return;
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

    private void StoreResult(
        HeartbeatPluginCommand heartbeatCommand,
        PluginCommandExecutionResult result)
    {
        switch (result)
        {
            case PluginCommandExecutionResult.Completed:
                MarkCompleted(heartbeatCommand.Command);
                break;
            case PluginCommandExecutionResult.Deferred:
                Enqueue(heartbeatCommand, PendingPluginCommandState.PendingApplication);
                break;
            case PluginCommandExecutionResult.FinalizedAwaitingAck:
                Enqueue(heartbeatCommand, PendingPluginCommandState.FinalizedAwaitingAck);
                break;
        }
    }

    private void StartApplication(
        HeartbeatPluginCommand heartbeatCommand,
        Func<HeartbeatPluginCommand, Task<PluginCommandExecutionResult>> applyAndAckAsync,
        List<StartedPluginCommand> startedApplications)
    {
        var command = heartbeatCommand.Command;
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
                heartbeatCommand,
                applyAndAckAsync(heartbeatCommand)));
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

    private void Enqueue(
        HeartbeatPluginCommand heartbeatCommand,
        PendingPluginCommandState state)
    {
        lock (_sync)
        {
            var command = heartbeatCommand.Command;
            var commandId = NormalizeCommandId(command.Id);
            if (commandId != null && _deferredById.ContainsKey(commandId)) return;

            var pending = new PendingPluginCommand(heartbeatCommand, state);
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

            var commandId = NormalizeCommandId(pending.HeartbeatCommand.Command.Id);
            if (commandId != null) _deferredById.Remove(commandId);
            MarkCompletedUnsafe(pending.HeartbeatCommand.Command);
        }
    }

    private List<PendingPluginCommand> SnapshotPendingApplicationBatch()
    {
        lock (_sync)
        {
            var batch = new List<PendingPluginCommand>();
            foreach (var pending in _deferred)
            {
                if (pending.State != PendingPluginCommandState.PendingApplication) break;
                batch.Add(pending);
            }
            return batch;
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

    private static string? NormalizeMatchId(string? matchId) =>
        string.IsNullOrWhiteSpace(matchId) ? null : matchId.Trim();

    private sealed class PendingPluginCommand(
        HeartbeatPluginCommand heartbeatCommand,
        PendingPluginCommandState state)
    {
        public HeartbeatPluginCommand HeartbeatCommand { get; } = heartbeatCommand;

        public PendingPluginCommandState State { get; set; } = state;
    }

    private sealed record StartedPluginCommand(
        HeartbeatPluginCommand HeartbeatCommand,
        Task<PluginCommandExecutionResult> Completion);

    private sealed record StartedPendingPluginCommand(
        PendingPluginCommand Pending,
        Task<PluginCommandExecutionResult> Completion);

    private enum PendingPluginCommandState
    {
        PendingApplication,
        Completed,
        FinalizedAwaitingAck
    }
}
