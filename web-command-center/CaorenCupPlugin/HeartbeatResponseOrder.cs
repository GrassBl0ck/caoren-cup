namespace CaorenCupPlugin;

internal sealed class HeartbeatResponseOrder
{
    private long _issuedSequence;
    private long _minimumAcceptedSequenceExclusive;
    private long _lastAcceptedSequence;

    public long NextRequestSequence() => Interlocked.Increment(ref _issuedSequence);

    public void BeginBarrier()
    {
        var barrierSequence = NextRequestSequence();
        Volatile.Write(ref _minimumAcceptedSequenceExclusive, barrierSequence);
    }

    public bool TryAccept(long requestSequence)
    {
        if (requestSequence <= Volatile.Read(ref _minimumAcceptedSequenceExclusive) ||
            requestSequence <= _lastAcceptedSequence)
        {
            return false;
        }

        _lastAcceptedSequence = requestSequence;
        return true;
    }
}

internal static class HeartbeatResponseProcessor
{
    public static async Task<HeartbeatResponseDisposition> ProcessTransactionAsync(
        PluginHeartbeatResponse? response,
        Func<Task<HeartbeatResponseDisposition>> classifyStateAsync,
        Func<PluginHeartbeatResponse?, HeartbeatResponseDisposition, Task> processCommandsAsync)
    {
        ArgumentNullException.ThrowIfNull(classifyStateAsync);
        ArgumentNullException.ThrowIfNull(processCommandsAsync);

        var disposition = await classifyStateAsync();
        await processCommandsAsync(response, disposition);
        return disposition;
    }

    public static async Task<bool> ProcessAsync(
        PluginHeartbeatResponse? response,
        Func<Task<bool>> applyStateAsync,
        Func<PluginCommand, Task> processCommandAsync)
    {
        ArgumentNullException.ThrowIfNull(applyStateAsync);
        ArgumentNullException.ThrowIfNull(processCommandAsync);

        var stateAccepted = await applyStateAsync();
        if (response?.Commands is { Count: > 0 })
        {
            foreach (var command in response.Commands)
            {
                await processCommandAsync(command);
            }
        }

        return stateAccepted;
    }
}
