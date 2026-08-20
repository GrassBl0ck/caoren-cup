using System.Text;
using CS2MiniGames.Framework;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;
using Xunit.Abstractions;

namespace CS2MiniGames.Tests;

public sealed class CenterHtmlCadenceComparisonTests(ITestOutputHelper output)
{
    [Fact]
    public void ComparesTwentyThirtyTwoAndSixtyFourHertzWithSixteenRealSessions()
    {
        var results = new[] { 20, 32, 64 }
            .Select(Simulate)
            .ToArray();

        Assert.Equal([3_200, 5_120, 10_240], results.Select(result => result.EventCount));
        Assert.All(results, result => Assert.Equal(208, result.RenderCount));
        Assert.All(results, result => Assert.Equal(16, result.MaximumPending));
        Assert.True(results[0].PayloadBytes < results[1].PayloadBytes);
        Assert.True(results[1].PayloadBytes < results[2].PayloadBytes);

        foreach (var result in results)
        {
            output.WriteLine(
                $"{result.RateHz}Hz: events={result.EventCount}, " +
                $"renders={result.RenderCount}, payloadBytes={result.PayloadBytes}, " +
                $"maximumPending={result.MaximumPending}");
        }
    }

    private static SimulationResult Simulate(int rateHz)
    {
        const int players = 16;
        const int ticksPerSecond = 64;
        const int seconds = 10;
        var sendInterval = TimeSpan.FromSeconds(1d / rateHz);
        var sendGates = Enumerable.Range(0, players)
            .Select(_ => new FixedRateCenterHtmlGate(sendInterval))
            .ToArray();
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
        var eventCount = 0;
        var renderCount = 0;
        long payloadBytes = 0;
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
            var now = TimeSpan.FromTicks(tickDuration.Ticks * tick);
            for (var player = 0; player < players; player++)
            {
                if (!sendGates[player].ShouldSend(now))
                {
                    continue;
                }

                var session = sessions[player];
                var frame = pumps[player].Tick(
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
                        eventCount++;
                        payloadBytes += Encoding.UTF8.GetByteCount(frame.Html);
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

        return new SimulationResult(
            rateHz,
            eventCount,
            renderCount,
            payloadBytes,
            maximumPending);
    }

    private sealed record SimulationResult(
        int RateHz,
        int EventCount,
        int RenderCount,
        long PayloadBytes,
        int MaximumPending);

    private sealed class StubPieceSource(params TetrominoType[] pieces) : IPieceSource
    {
        private readonly TetrominoType[] _sequence = pieces;
        private Queue<TetrominoType> _pieces = new(pieces);

        public TetrominoType Next() => _pieces.Dequeue();

        public void Reset() => _pieces = new Queue<TetrominoType>(_sequence);
    }
}
