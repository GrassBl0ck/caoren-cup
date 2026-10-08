using CaorenCup.QOL.PlaySound;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AudioPlaybackTimelineTests
{
    [Fact]
    public void Pause_freezes_progress_and_resume_continues_same_position()
    {
        var timeline = new AudioPlaybackTimeline(30, 100);
        Assert.True(timeline.Pause(108));
        Assert.Equal(8, timeline.Snapshot(500).PositionSeconds);
        Assert.True(timeline.Resume(500));
        Assert.Equal(12, timeline.Snapshot(504).PositionSeconds);
    }
    [Fact]
    public void Loop_off_ends_at_current_cycle_boundary()
    {
        var timeline = new AudioPlaybackTimeline(30, 100, true);
        Assert.Equal(13, timeline.Snapshot(143).PositionSeconds);
        Assert.True(timeline.SetLoop(false, 143));
        Assert.Equal(60, timeline.Snapshot(143).EndAtElapsedSeconds);
        Assert.Equal(AudioPlaybackState.Playing, timeline.Snapshot(159).State);
        Assert.Equal(AudioPlaybackState.Ended, timeline.Snapshot(160).State);
        Assert.False(timeline.SetLoop(true, 160));
        Assert.False(timeline.Resume(160));
    }
    [Fact]
    public void Pause_does_not_consume_remaining_time_after_loop_off()
    {
        var timeline = new AudioPlaybackTimeline(30, 100, true);
        timeline.SetLoop(false, 143); timeline.Pause(145);
        Assert.Equal(AudioPlaybackState.Paused, timeline.Snapshot(200).State);
        timeline.Resume(200);
        Assert.Equal(AudioPlaybackState.Playing, timeline.Snapshot(214).State);
        Assert.Equal(AudioPlaybackState.Ended, timeline.Snapshot(215).State);
    }
    [Fact]
    public void Reenabling_loop_before_end_cancels_pending_end()
    {
        var timeline = new AudioPlaybackTimeline(30, 100, true);
        timeline.SetLoop(false, 143); timeline.SetLoop(true, 159);
        Assert.Null(timeline.Snapshot(500).EndAtElapsedSeconds);
        Assert.Equal(AudioPlaybackState.Playing, timeline.Snapshot(500).State);
    }
    [Fact]
    public void Two_channel_timelines_are_independent()
    {
        var music = new AudioPlaybackTimeline(30, 100, true);
        var broadcast = new AudioPlaybackTimeline(10, 102);
        music.Pause(107);
        Assert.Equal(7, music.Snapshot(110).PositionSeconds);
        Assert.Equal(8, broadcast.Snapshot(110).PositionSeconds);
        music.Stop(110);
        Assert.Equal(AudioPlaybackState.Stopped, music.Snapshot(111).State);
        Assert.Equal(AudioPlaybackState.Playing, broadcast.Snapshot(111).State);
        Assert.Equal(AudioPlaybackState.Ended, broadcast.Snapshot(112).State);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_duration_is_rejected(double duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlaybackTimeline(duration, 100));
}
