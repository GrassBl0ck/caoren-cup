namespace CS2MiniGames.Framework;

internal sealed class FixedRateCenterHtmlGate
{
    private readonly TimeSpan _interval;
    private bool _hasSent;
    private TimeSpan _nextSendAt;

    internal FixedRateCenterHtmlGate(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _interval = interval;
    }

    internal bool ShouldSend(TimeSpan now)
    {
        if (!_hasSent)
        {
            _hasSent = true;
            _nextSendAt = now + _interval;
            return true;
        }

        if (now < _nextSendAt)
        {
            return false;
        }

        do
        {
            _nextSendAt += _interval;
        }
        while (_nextSendAt <= now);

        return true;
    }
}
