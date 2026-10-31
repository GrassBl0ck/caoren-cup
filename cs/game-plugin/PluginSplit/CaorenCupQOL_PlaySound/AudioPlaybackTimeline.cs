namespace CaorenCup.QOL.PlaySound;

public enum AudioPlaybackState { Playing, Paused, Ended, Stopped }
public sealed record AudioTimelineSnapshot(AudioPlaybackState State, double ElapsedSeconds,
    double PositionSeconds, bool LoopEnabled, double? EndAtElapsedSeconds, bool FinishingCycle = false);

/// <summary>正式通道的纯时间状态；暂停冻结进度，关闭循环在当前一轮末结束。</summary>
public sealed class AudioPlaybackTimeline
{
    private double _anchor;
    private double _elapsed;
    private bool _paused;
    private bool _stopped;
    private double _end;
    private bool _finishingCycle;
    public double DurationSeconds { get; }
    public bool LoopEnabled { get; private set; }
    public AudioPlaybackTimeline(double durationSeconds, double now, bool loop = false)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
        DurationSeconds = durationSeconds;
        _anchor = now;
        LoopEnabled = loop;
        _end = loop ? double.PositiveInfinity : durationSeconds;
    }
    private double Elapsed(double now)
    {
        if (!double.IsFinite(now) || now < _anchor) throw new ArgumentOutOfRangeException(nameof(now));
        return _elapsed + (_paused || _stopped ? 0 : now - _anchor);
    }
    public AudioTimelineSnapshot Snapshot(double now)
    {
        var elapsed = Elapsed(now);
        var ended = elapsed >= _end;
        var state = _stopped ? AudioPlaybackState.Stopped : ended ? AudioPlaybackState.Ended
            : _paused ? AudioPlaybackState.Paused : AudioPlaybackState.Playing;
        var position = ended ? DurationSeconds : elapsed % DurationSeconds;
        return new(state, Math.Min(elapsed, _end), position, LoopEnabled,
            double.IsPositiveInfinity(_end) ? null : _end, _finishingCycle && !ended && !_stopped);
    }
    public bool Pause(double now)
    {
        if (Snapshot(now).State != AudioPlaybackState.Playing) return false;
        _elapsed = Elapsed(now); _anchor = now; _paused = true;
        return true;
    }
    public bool Resume(double now)
    {
        if (Snapshot(now).State != AudioPlaybackState.Paused) return false;
        _anchor = now; _paused = false;
        return true;
    }
    public bool SetLoop(bool enabled, double now)
    {
        if (Snapshot(now).State is AudioPlaybackState.Ended or AudioPlaybackState.Stopped) return false;
        if (enabled == LoopEnabled) return true;
        var elapsed = Elapsed(now);
        LoopEnabled = enabled;
        _finishingCycle = !enabled;
        _end = enabled ? double.PositiveInfinity : (Math.Floor(elapsed / DurationSeconds) + 1) * DurationSeconds;
        return true;
    }
    public void Stop(double now)
    {
        _elapsed = Math.Min(Elapsed(now), _end); _anchor = now; _stopped = true;
    }
}
