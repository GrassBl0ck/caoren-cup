namespace CS2MiniGames.Framework;

internal static class CenterHtmlFrameScheduler
{
    internal static void Queue(
        Action<Action> scheduleNextFrame,
        Action send)
    {
        ArgumentNullException.ThrowIfNull(scheduleNextFrame);
        ArgumentNullException.ThrowIfNull(send);

        scheduleNextFrame(send);
    }
}
