namespace CS2MiniGames.Framework;

public sealed class FrameSendPolicy
{
    private readonly TimeSpan _minimumInterval;
    private bool _hasSent;
    private long _lastSentRevision;
    private TimeSpan _lastSentAt;

    public FrameSendPolicy(TimeSpan minimumInterval)
    {
        if (minimumInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        }

        _minimumInterval = minimumInterval;
    }

    public bool ShouldSend(long revision, TimeSpan now)
    {
        if (!_hasSent)
        {
            RecordSend(revision, now);
            return true;
        }

        if (revision == _lastSentRevision || now - _lastSentAt < _minimumInterval)
        {
            return false;
        }

        RecordSend(revision, now);
        return true;
    }

    private void RecordSend(long revision, TimeSpan now)
    {
        _hasSent = true;
        _lastSentRevision = revision;
        _lastSentAt = now;
    }
}
