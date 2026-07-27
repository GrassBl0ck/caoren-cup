namespace CaorenCupPlugin;

internal readonly record struct HeartbeatRequestStamp(
    long Sequence,
    long BarrierGeneration);

internal sealed class HeartbeatResponseOrder
{
    private readonly object _sync = new();
    private long _issuedSequence;
    private long _minimumAcceptedSequenceExclusive;
    private long _lastAcceptedSequence;
    private long _barrierGeneration;

    public HeartbeatRequestStamp NextRequest()
    {
        lock (_sync)
        {
            _issuedSequence++;
            return new HeartbeatRequestStamp(_issuedSequence, _barrierGeneration);
        }
    }

    public long NextRequestSequence() => NextRequest().Sequence;

    public void BeginBarrier()
    {
        lock (_sync)
        {
            _issuedSequence++;
            _minimumAcceptedSequenceExclusive = _issuedSequence;
            _barrierGeneration++;
        }
    }

    public bool TryAccept(long requestSequence)
    {
        lock (_sync)
        {
            if (requestSequence <= _minimumAcceptedSequenceExclusive ||
                requestSequence <= _lastAcceptedSequence)
            {
                return false;
            }

            _lastAcceptedSequence = requestSequence;
            return true;
        }
    }

    public bool IsCurrentGeneration(long barrierGeneration)
    {
        lock (_sync) return barrierGeneration == _barrierGeneration;
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
