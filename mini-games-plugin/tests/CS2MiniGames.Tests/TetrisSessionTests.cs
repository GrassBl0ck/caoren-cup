using CS2MiniGames.Framework;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisSessionTests
{
    [Theory]
    [InlineData(MiniGameAction.MoveLeft)]
    [InlineData(MiniGameAction.MoveRight)]
    [InlineData(MiniGameAction.SoftDrop)]
    [InlineData(MiniGameAction.HardDrop)]
    [InlineData(MiniGameAction.RotateClockwise)]
    [InlineData(MiniGameAction.RotateCounterClockwise)]
    [InlineData(MiniGameAction.Hold)]
    public void MapsEachGameplayActionToTheGame(MiniGameAction action)
    {
        var game = CreateGame();
        var before = Snapshot(game);
        var session = CreateSession(game);

        session.HandleActions([action]);

        AssertActionChangedOnlyItsExpectedState(action, before, game);
    }

    [Fact]
    public void CounterClockwiseRestartsInsteadOfRotatingAfterGameOver()
    {
        var source = new StubPieceSource(
            TetrominoType.I,
            TetrominoType.O,
            TetrominoType.T,
            TetrominoType.L);
        var game = new TetrisGameState(new TetrisGameOptions(), source);
        MakeNextSpawnGameOver(game);
        var session = CreateSession(game);

        session.Update(TimeSpan.FromMilliseconds(500));
        session.HandleActions([MiniGameAction.RotateCounterClockwise]);

        Assert.False(game.IsGameOver);
        Assert.Equal(1, source.ResetCount);
        Assert.Equal(RotationState.Spawn, game.ActivePiece.Rotation);
    }

    [Fact]
    public void ExitClosesAndCloseIsIdempotent()
    {
        var closeCount = 0;
        var session = CreateSession(CreateGame(), closePlayerBinding: () => closeCount++);

        session.HandleActions([MiniGameAction.Exit]);
        session.Close();

        Assert.True(session.IsClosed);
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void ClosedSessionIgnoresGameplayActionsAndUpdates()
    {
        var game = CreateGame();
        var before = Snapshot(game);
        var session = CreateSession(game);
        session.Close();

        session.HandleActions([MiniGameAction.MoveLeft, MiniGameAction.HardDrop]);
        session.Update(TimeSpan.FromSeconds(10));

        Assert.Equal(before, Snapshot(game));
    }

    [Fact]
    public void UpdateAdvancesOnlyItsOwnGame()
    {
        var firstGame = CreateGame();
        var secondGame = CreateGame();
        var session = CreateSession(firstGame);

        session.Update(TimeSpan.FromMilliseconds(800));

        Assert.Equal(1, firstGame.ActivePiece.Y);
        Assert.Equal(0, secondGame.ActivePiece.Y);
    }

    [Fact]
    public void RenderDelegatesToTheTetrisRenderer()
    {
        var game = CreateGame();
        var renderer = new TetrisRenderer();
        var session = new TetrisSession(7, game, renderer, _ => { }, () => { });

        Assert.Equal(renderer.Render(game), session.Render());
    }

    [Fact]
    public void RenderCachesTheSameRevisionAndRebuildsAfterVisibleChange()
    {
        var game = CreateGame();
        var renderer = new CountingRenderer();
        var session = new TetrisSession(7, game, renderer, _ => { }, () => { });

        var first = session.Render();
        var second = session.Render();

        Assert.Equal(first, second);
        Assert.Equal(1, renderer.RenderCount);

        Assert.True(game.MoveLeft());
        var changed = session.Render();

        Assert.NotEqual(first, changed);
        Assert.Equal(2, renderer.RenderCount);
    }

    [Fact]
    public void CrossingIntoGameOverSavesExactlyOnce()
    {
        var game = CreateGame();
        MakeNextSpawnGameOver(game);
        var results = new List<TetrisResult>();
        var session = CreateSession(game, results.Add);

        session.Update(TimeSpan.FromMilliseconds(500));
        session.Update(TimeSpan.FromSeconds(10));

        var result = Assert.Single(results);
        Assert.Equal(game.Score, result.Score);
        Assert.Equal(game.TotalLines, result.Lines);
        Assert.Equal(game.Level, result.Level);
    }

    [Fact]
    public void HardDropThatCrossesIntoGameOverSavesImmediately()
    {
        var game = CreateGame();
        MakeNextSpawnGameOver(game);
        var results = new List<TetrisResult>();
        var session = CreateSession(game, results.Add);

        session.HandleActions([MiniGameAction.HardDrop]);

        Assert.True(game.IsGameOver);
        Assert.Single(results);
    }

    [Fact]
    public void RestartAllowsTheNextGameOverToSaveOnceAgain()
    {
        var game = CreateGame();
        var results = new List<TetrisResult>();
        var session = CreateSession(game, results.Add);
        MakeNextSpawnGameOver(game);
        session.Update(TimeSpan.FromMilliseconds(500));

        session.HandleActions([MiniGameAction.RotateCounterClockwise]);
        MakeNextSpawnGameOver(game);
        session.Update(TimeSpan.FromMilliseconds(500));
        session.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(2, results.Count);
    }

    private static TetrisSession CreateSession(
        TetrisGameState game,
        Action<TetrisResult>? saveResult = null,
        Action? closePlayerBinding = null) =>
        new(
            playerSlot: 7,
            game,
            new TetrisRenderer(),
            saveResult ?? (_ => { }),
            closePlayerBinding ?? (() => { }));

    private static TetrisGameState CreateGame() =>
        new(
            new TetrisGameOptions(),
            new StubPieceSource(
                TetrominoType.T,
                TetrominoType.I,
                TetrominoType.O,
                TetrominoType.L,
                TetrominoType.J));

    private static void MakeNextSpawnGameOver(TetrisGameState game)
    {
        for (var i = 0; i < TetrisBoard.TotalHeight; i++)
        {
            game.SoftDrop();
        }

        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 0));
    }

    private static GameSnapshot Snapshot(TetrisGameState game) =>
        new(
            game.ActivePiece,
            game.HoldPiece,
            game.NextPiece,
            game.Score,
            game.TotalLines,
            game.Level,
            game.IsGameOver);

    private static void AssertActionChangedOnlyItsExpectedState(
        MiniGameAction action,
        GameSnapshot before,
        TetrisGameState game)
    {
        switch (action)
        {
            case MiniGameAction.MoveLeft:
                Assert.Equal(before.ActivePiece.X - 1, game.ActivePiece.X);
                break;
            case MiniGameAction.MoveRight:
                Assert.Equal(before.ActivePiece.X + 1, game.ActivePiece.X);
                break;
            case MiniGameAction.SoftDrop:
                Assert.Equal(before.ActivePiece.Y + 1, game.ActivePiece.Y);
                Assert.Equal(before.Score + 1, game.Score);
                break;
            case MiniGameAction.HardDrop:
                Assert.NotEqual(before.ActivePiece.Type, game.ActivePiece.Type);
                Assert.True(game.Score > before.Score);
                break;
            case MiniGameAction.RotateClockwise:
                Assert.Equal(RotationState.Right, game.ActivePiece.Rotation);
                break;
            case MiniGameAction.RotateCounterClockwise:
                Assert.Equal(RotationState.Left, game.ActivePiece.Rotation);
                break;
            case MiniGameAction.Hold:
                Assert.Equal(before.ActivePiece.Type, game.HoldPiece);
                Assert.Equal(before.NextPiece, game.ActivePiece.Type);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private sealed record GameSnapshot(
        ActivePiece ActivePiece,
        TetrominoType? HoldPiece,
        TetrominoType NextPiece,
        int Score,
        int TotalLines,
        int Level,
        bool IsGameOver);

    private sealed class StubPieceSource(params TetrominoType[] pieces) : IPieceSource
    {
        private readonly TetrominoType[] _sequence = pieces;
        private Queue<TetrominoType> _pieces = new(pieces);

        public int ResetCount { get; private set; }

        public TetrominoType Next() => _pieces.Dequeue();

        public void Reset()
        {
            ResetCount++;
            _pieces = new Queue<TetrominoType>(_sequence);
        }
    }

    private sealed class CountingRenderer : ITetrisRenderer
    {
        public int RenderCount { get; private set; }

        public string Render(TetrisGameState game)
        {
            RenderCount++;
            return $"revision:{game.Revision}";
        }
    }
}
