using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class CenterHtmlFrameSchedulerTests
{
    [Fact]
    public void QueuesTheSendWithoutRunningItUntilTheNextFrame()
    {
        Action? queued = null;
        var sendCount = 0;

        CenterHtmlFrameScheduler.Queue(
            scheduleNextFrame: action => queued = action,
            send: () => sendCount++);

        Assert.Equal(0, sendCount);
        Assert.NotNull(queued);

        queued();

        Assert.Equal(1, sendCount);
    }
}
