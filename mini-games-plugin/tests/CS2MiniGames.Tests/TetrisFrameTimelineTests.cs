using CS2MiniGames.Framework;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisFrameTimelineTests
{
    [Fact]
    public void DefaultGravityProducesNextDirtyFrameWithoutPlayerInput()
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
        var frameSender = new FrameSendPolicy(TimeSpan.FromMilliseconds(100));
        var frames = new List<Frame>();
        var now = TimeSpan.Zero;

        CaptureFrameIfEligible();
        for (var tick = 0; tick < 16; tick++)
        {
            var elapsed = TimeSpan.FromMilliseconds(50);
            now += elapsed;
            session.Update(elapsed);
            CaptureFrameIfEligible();
        }

        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(800)],
            frames.Select(frame => frame.At));
        Assert.True(frames[1].Revision > frames[0].Revision);
        Assert.NotEqual(frames[0].Html, frames[1].Html);
        Assert.Equal(1, game.ActivePiece.Y);

        void CaptureFrameIfEligible()
        {
            if (frameSender.ShouldSend(session.Revision, now))
            {
                frames.Add(new Frame(now, session.Revision, session.Render()));
            }
        }
    }

    private sealed record Frame(TimeSpan At, long Revision, string Html);

    private sealed class StubPieceSource(params TetrominoType[] pieces) : IPieceSource
    {
        private readonly TetrominoType[] _sequence = pieces;
        private Queue<TetrominoType> _pieces = new(pieces);

        public TetrominoType Next() => _pieces.Dequeue();

        public void Reset() => _pieces = new Queue<TetrominoType>(_sequence);
    }
}
