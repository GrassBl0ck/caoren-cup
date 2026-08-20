namespace CS2MiniGames.Framework;

internal sealed class SnakeStyleCenterHtmlPump
{
    private readonly FrameSendPolicy _refreshPolicy;
    private CenterHtmlFrame? _cachedFrame;

    internal SnakeStyleCenterHtmlPump(TimeSpan minimumRefreshInterval)
    {
        _refreshPolicy = new FrameSendPolicy(minimumRefreshInterval);
    }

    internal CenterHtmlFrame Tick(
        long revision,
        TimeSpan now,
        Func<string> render,
        int durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(render);
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }

        var shouldRefresh = _refreshPolicy.ShouldSend(revision, now);
        if (_cachedFrame is null || shouldRefresh)
        {
            _cachedFrame = new CenterHtmlFrame(
                render(),
                durationSeconds,
                revision);
        }

        return _cachedFrame.Value;
    }
}

internal readonly record struct CenterHtmlFrame(
    string Html,
    int DurationSeconds,
    long Revision);
