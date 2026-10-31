namespace CS2MiniGames.Framework;

public sealed class FrameSendPolicy
{
    private readonly TimeSpan _keepaliveInterval;
    private bool _hasSent;
    private long _lastRevision;
    private TimeSpan _lastSentAt;

    public FrameSendPolicy(TimeSpan keepaliveInterval)
    {
        if (keepaliveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(keepaliveInterval));
        }

        _keepaliveInterval = keepaliveInterval;
    }

    public bool ShouldSend(long revision, TimeSpan now)
    {
        if (_hasSent &&
            revision == _lastRevision &&
            now - _lastSentAt < _keepaliveInterval)
        {
            return false;
        }

        _hasSent = true;
        _lastRevision = revision;
        _lastSentAt = now;
        return true;
    }
}
