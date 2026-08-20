using CS2MiniGames.Framework;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class SnakeStyleCenterHtmlPumpTests
{
    [Fact]
    public void ActiveSessionSendGateUsesThirtyTwoHertz()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(31.25),
            CS2MiniGamesPlugin.ActiveSessionCenterHtmlSendInterval);

        var gate = CS2MiniGamesPlugin.CreateActiveSessionCenterHtmlSendGate();

        Assert.True(gate.ShouldSend(TimeSpan.Zero));
        Assert.False(gate.ShouldSend(TimeSpan.FromMilliseconds(31)));
        Assert.True(gate.ShouldSend(TimeSpan.FromMilliseconds(31.25)));
    }

    [Fact]
    public void ActiveSessionFactoryUsesTheConfiguredCacheRefreshInterval()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(100),
            CS2MiniGamesPlugin.ActiveSessionMinimumFrameInterval);

        var pump = CS2MiniGamesPlugin.CreateActiveSessionCenterHtmlPump();
        var renderCount = 0;

        pump.Tick(1, TimeSpan.Zero, Render, durationSeconds: 5);
        pump.Tick(2, TimeSpan.FromMilliseconds(99), Render, durationSeconds: 5);
        pump.Tick(2, TimeSpan.FromMilliseconds(100), Render, durationSeconds: 5);

        Assert.Equal(2, renderCount);

        string Render()
        {
            renderCount++;
            return $"frame-{renderCount}";
        }
    }

    [Fact]
    public void UnchangedHtmlIsSentOnEveryServerTickWithoutRerendering()
    {
        var pump = new SnakeStyleCenterHtmlPump(TimeSpan.FromMilliseconds(100));
        var renderCount = 0;
        var frames = new List<CenterHtmlFrame>();

        for (var tick = 0; tick < 64; tick++)
        {
            var now = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 64 * tick);
            frames.Add(pump.Tick(
                revision: 1,
                now,
                render: () =>
                {
                    renderCount++;
                    return "cached-frame";
                },
                durationSeconds: 5));
        }

        Assert.Equal(64, frames.Count);
        Assert.All(frames, frame => Assert.Equal("cached-frame", frame.Html));
        Assert.Equal(1, renderCount);
    }

    [Fact]
    public void AutoGravityRefreshesTheCacheWhileEveryTickStillSendsAFrame()
    {
        var game = new TetrisGameState(
            new TetrisGameOptions(),
            new StubPieceSource(
                TetrominoType.T,
                TetrominoType.I,
                TetrominoType.O));
        var session = new TetrisSession(
            playerSlot: 7,
            game,
            new TetrisRenderer(),
            _ => { },
            () => { });
        var pump = new SnakeStyleCenterHtmlPump(TimeSpan.FromMilliseconds(100));
        var renderCount = 0;
        var frames = new List<CenterHtmlFrame>();
        var now = TimeSpan.Zero;

        for (var tick = 0; tick <= 16; tick++)
        {
            frames.Add(pump.Tick(
                session.Revision,
                now,
                render: () =>
                {
                    renderCount++;
                    return session.Render();
                },
                session.CenterHtmlDurationSeconds));

            if (tick < 16)
            {
                session.Update(TimeSpan.FromMilliseconds(50));
                now += TimeSpan.FromMilliseconds(50);
            }
        }

        Assert.Equal(17, frames.Count);
        Assert.Equal(2, renderCount);
        Assert.All(frames.Take(16), frame => Assert.Equal(frames[0], frame));
        Assert.NotEqual(frames[0].Html, frames[16].Html);
        Assert.True(frames[16].Revision > frames[0].Revision);
        Assert.Equal(1, game.ActivePiece.Y);
    }

    [Fact]
    public void SixteenPlayersAt64TicksQueue1024EventsPerSecondWithoutCallbackGrowth()
    {
        const int players = 16;
        const int ticksPerSecond = 64;
        const int seconds = 10;
        var pumps = Enumerable.Range(0, players)
            .Select(_ => new SnakeStyleCenterHtmlPump(TimeSpan.FromMilliseconds(100)))
            .ToArray();
        var sessions = Enumerable.Range(0, players)
            .Select(slot => new TetrisSession(
                slot,
                new TetrisGameState(
                    new TetrisGameOptions(),
                    new StubPieceSource(
                        TetrominoType.T,
                        TetrominoType.I,
                        TetrominoType.O)),
                new TetrisRenderer(),
                _ => { },
                () => { }))
            .ToArray();
        var pending = new List<Action>();
        var sendCount = 0;
        var renderCount = 0;
        var maximumPending = 0;
        var tickDuration = TimeSpan.FromTicks(
            TimeSpan.TicksPerSecond / ticksPerSecond);

        for (var tick = 0; tick < ticksPerSecond * seconds; tick++)
        {
            foreach (var callback in pending)
            {
                callback();
            }

            pending.Clear();
            var now = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / ticksPerSecond * tick);
            for (var player = 0; player < players; player++)
            {
                var pump = pumps[player];
                var session = sessions[player];
                var frame = pump.Tick(
                    session.Revision,
                    now,
                    render: () =>
                    {
                        renderCount++;
                        return session.Render();
                    },
                    session.CenterHtmlDurationSeconds);

                CenterHtmlFrameScheduler.Queue(
                    scheduleNextFrame: callback => pending.Add(callback),
                    send: () =>
                    {
                        Assert.NotEmpty(frame.Html);
                        sendCount++;
                    });
            }

            maximumPending = Math.Max(maximumPending, pending.Count);
            foreach (var session in sessions)
            {
                session.Update(tickDuration);
            }
        }

        foreach (var callback in pending)
        {
            callback();
        }

        Assert.Equal(players * ticksPerSecond * seconds, sendCount);
        Assert.Equal(players * 13, renderCount);
        Assert.Equal(players, maximumPending);
    }

    private sealed class StubPieceSource(params TetrominoType[] pieces) : IPieceSource
    {
        private readonly TetrominoType[] _sequence = pieces;
        private Queue<TetrominoType> _pieces = new(pieces);

        public TetrominoType Next() => _pieces.Dequeue();

        public void Reset() => _pieces = new Queue<TetrominoType>(_sequence);
    }
}
