using CounterStrikeSharp.API;
using CS2MiniGames.Framework;

namespace CS2MiniGames.Tests;

public sealed class MiniGameManagerTests
{
    [Fact]
    public void TryStartRejectsASecondSessionForTheSamePlayerSlot()
    {
        var manager = new MiniGameManager();
        var first = new FakeSession(7);
        var second = new FakeSession(7);

        Assert.True(manager.TryStart(first));
        Assert.False(manager.TryStart(second));
        Assert.True(manager.TryGet(7, out var active));
        Assert.Same(first, active);
    }

    [Fact]
    public void TryGetReturnsAnActiveSessionThatHandlesActionsFromARealTracker()
    {
        var manager = new MiniGameManager();
        var session = new FakeSession(11);
        var tracker = new PlayerInputTracker();
        manager.TryStart(session);

        var found = manager.TryGet(11, out var active);
        var actions = tracker.Read(PlayerButtons.Reload, TimeSpan.Zero);
        active!.HandleActions(actions);

        Assert.True(found);
        Assert.Equal([MiniGameAction.RotateCounterClockwise], session.HandledActions);
        Assert.False(manager.TryGet(12, out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void CloseRemovesTheSessionAndCallsCloseExactlyOnce()
    {
        var manager = new MiniGameManager();
        var session = new FakeSession(3);
        manager.TryStart(session);

        Assert.True(manager.Close(3));
        Assert.False(manager.Close(3));

        Assert.Equal(1, session.CloseCount);
        Assert.True(session.IsClosed);
        Assert.False(manager.TryGet(3, out _));
    }

    [Fact]
    public void CloseAllIsIdempotent()
    {
        var manager = new MiniGameManager();
        var first = new FakeSession(1);
        var second = new FakeSession(2);
        manager.TryStart(first);
        manager.TryStart(second);

        manager.CloseAll();
        manager.CloseAll();

        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, second.CloseCount);
        Assert.False(manager.TryGet(1, out _));
        Assert.False(manager.TryGet(2, out _));
    }

    [Fact]
    public void UpdateAllReportsOneSessionFailureAndContinuesUpdatingOthers()
    {
        var errors = new List<(IMiniGameSession Session, Exception Error)>();
        var manager = new MiniGameManager((session, error) => errors.Add((session, error)));
        var failing = new FakeSession(4) { UpdateError = new InvalidOperationException("boom") };
        var healthy = new FakeSession(5);
        manager.TryStart(failing);
        manager.TryStart(healthy);

        manager.UpdateAll(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, failing.UpdateCount);
        Assert.Equal(1, healthy.UpdateCount);
        Assert.Equal(TimeSpan.FromMilliseconds(16), healthy.LastElapsed);
        var reported = Assert.Single(errors);
        Assert.Same(failing, reported.Session);
        Assert.Same(failing.UpdateError, reported.Error);
    }

    [Fact]
    public void UpdateAllUsesASnapshotSoClosingDuringUpdateDoesNotStopOtherSessions()
    {
        var manager = new MiniGameManager();
        var closing = new FakeSession(20);
        var healthy = new FakeSession(21);
        closing.OnUpdate = () => manager.Close(closing.PlayerSlot);
        manager.TryStart(closing);
        manager.TryStart(healthy);

        manager.UpdateAll(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, closing.UpdateCount);
        Assert.Equal(1, closing.CloseCount);
        Assert.Equal(1, healthy.UpdateCount);
        Assert.False(manager.TryGet(closing.PlayerSlot, out _));
        Assert.True(manager.TryGet(healthy.PlayerSlot, out _));
    }

    private sealed class FakeSession(int playerSlot) : IMiniGameSession
    {
        public int PlayerSlot { get; } = playerSlot;

        public bool IsClosed { get; private set; }

        public long Revision => UpdateCount;

        public int CloseCount { get; private set; }

        public int UpdateCount { get; private set; }

        public TimeSpan LastElapsed { get; private set; }

        public Exception? UpdateError { get; init; }

        public Action? OnUpdate { get; set; }

        public List<MiniGameAction> HandledActions { get; } = [];

        public void HandleActions(IReadOnlyCollection<MiniGameAction> actions) =>
            HandledActions.AddRange(actions);

        public void Update(TimeSpan elapsed)
        {
            UpdateCount++;
            LastElapsed = elapsed;
            OnUpdate?.Invoke();

            if (UpdateError is not null)
            {
                throw UpdateError;
            }
        }

        public string Render() => $"slot:{PlayerSlot}";

        public void Close()
        {
            CloseCount++;
            IsClosed = true;
        }
    }
}
