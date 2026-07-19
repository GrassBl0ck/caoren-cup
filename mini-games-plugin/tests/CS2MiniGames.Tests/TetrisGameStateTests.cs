using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisGameStateTests
{
    [Fact]
    public void SpawnsTheFirstPieceAtTheStandardPositionAndPreviewsTheSecond()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I);

        Assert.Equal(new ActivePiece(TetrominoType.T, RotationState.Spawn, 3, 0), game.ActivePiece);
        Assert.Equal(TetrominoType.I, game.NextPiece);
        Assert.Null(game.HoldPiece);
        Assert.False(game.IsGameOver);
    }

    [Fact]
    public void HorizontalMovementStopsAtBothWalls()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I);

        for (var i = 0; i < 4; i++)
        {
            game.MoveLeft();
        }

        Assert.Equal(0, game.ActivePiece.X);

        for (var i = 0; i < 8; i++)
        {
            game.MoveRight();
        }

        Assert.Equal(7, game.ActivePiece.X);
    }

    [Fact]
    public void GravityAccumulatorsBelongToEachGameInstance()
    {
        var first = CreateGame(TetrominoType.O, TetrominoType.I);
        var second = CreateGame(TetrominoType.O, TetrominoType.I);

        first.Advance(TimeSpan.FromMilliseconds(799));
        second.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, first.ActivePiece.Y);
        Assert.Equal(0, second.ActivePiece.Y);

        first.Advance(TimeSpan.FromMilliseconds(1));
        second.Advance(TimeSpan.FromMilliseconds(799));

        Assert.Equal(1, first.ActivePiece.Y);
        Assert.Equal(1, second.ActivePiece.Y);
    }

    [Fact]
    public void SoftDropMovesOneCellAndAddsOnePointOnlyWhenSuccessful()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I);

        game.SoftDrop();

        Assert.Equal(1, game.ActivePiece.Y);
        Assert.Equal(1, game.Score);

        SoftDropToFloor(game);
        var scoreAtFloor = game.Score;
        game.SoftDrop();

        Assert.Equal(20, game.ActivePiece.Y);
        Assert.Equal(scoreAtFloor, game.Score);
    }

    [Fact]
    public void HardDropScoresTwoPerCellAndLocksImmediately()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I, TetrominoType.T);

        game.HardDrop();

        Assert.Equal(40, game.Score);
        Assert.Equal(new ActivePiece(TetrominoType.I, RotationState.Spawn, 3, 0), game.ActivePiece);
        Assert.Equal(TetrominoType.O, game.Board.GetCell(4, 20));
        Assert.Equal(TetrominoType.O, game.Board.GetCell(5, 21));
    }

    [Fact]
    public void GhostIsTheLastValidDownwardPosition()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);

        var ghost = game.GhostPiece;

        Assert.Equal(new ActivePiece(TetrominoType.I, RotationState.Spawn, 3, 20), ghost);
        Assert.True(game.Board.CanPlace(ghost));
        Assert.False(game.Board.CanPlace(ghost.Move(0, 1)));
    }

    [Fact]
    public void EmptyHoldStoresTheActivePieceAndConsumesThePreview()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I, TetrominoType.O);

        game.Hold();

        Assert.Equal(TetrominoType.T, game.HoldPiece);
        Assert.Equal(new ActivePiece(TetrominoType.I, RotationState.Spawn, 3, 0), game.ActivePiece);
        Assert.Equal(TetrominoType.O, game.NextPiece);
    }

    [Fact]
    public void NonEmptyHoldSwapsWithoutConsumingPreviewAndResetsThePiece()
    {
        var game = CreateGame(
            TetrominoType.T,
            TetrominoType.I,
            TetrominoType.O,
            TetrominoType.L,
            TetrominoType.J);
        game.Hold();
        game.HardDrop();
        game.MoveLeft();
        game.RotateClockwise();

        game.Hold();

        Assert.Equal(TetrominoType.O, game.HoldPiece);
        Assert.Equal(new ActivePiece(TetrominoType.T, RotationState.Spawn, 3, 0), game.ActivePiece);
        Assert.Equal(TetrominoType.L, game.NextPiece);
    }

    [Fact]
    public void HoldCannotRepeatBeforeTheCurrentPieceLocks()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I, TetrominoType.O);
        game.Hold();
        var activeAfterFirstHold = game.ActivePiece;
        var holdAfterFirstHold = game.HoldPiece;
        var nextAfterFirstHold = game.NextPiece;

        game.Hold();

        Assert.Equal(activeAfterFirstHold, game.ActivePiece);
        Assert.Equal(holdAfterFirstHold, game.HoldPiece);
        Assert.Equal(nextAfterFirstHold, game.NextPiece);
    }

    [Fact]
    public void LockClearsRowsScoresAtThePreClearLevelAndThenUpdatesTheLevel()
    {
        var options = new TetrisGameOptions(LinesPerLevel: 1);
        var game = CreateGame(options, TetrominoType.I, TetrominoType.O, TetrominoType.T);
        FillBottomRowOutsideIPlacement(game.Board);

        game.HardDrop();

        Assert.Equal(140, game.Score);
        Assert.Equal(1, game.TotalLines);
        Assert.Equal(2, game.Level);
    }

    [Fact]
    public void GroundedPieceLocksAfterTheConfiguredDelay()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I, TetrominoType.T);
        SoftDropToFloor(game);

        game.Advance(TimeSpan.FromMilliseconds(499));

        Assert.Equal(TetrominoType.O, game.ActivePiece.Type);

        game.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(TetrominoType.I, game.ActivePiece.Type);
        Assert.Equal(TetrominoType.O, game.Board.GetCell(4, 20));
    }

    [Fact]
    public void SuccessfulGroundedRotationResetsTheLockDelay()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I, TetrominoType.O);
        SoftDropToFloor(game);
        game.Advance(TimeSpan.FromMilliseconds(499));

        game.RotateClockwise();
        game.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(TetrominoType.T, game.ActivePiece.Type);

        game.Advance(TimeSpan.FromMilliseconds(499));

        Assert.Equal(TetrominoType.I, game.ActivePiece.Type);
    }

    [Fact]
    public void GroundedMovementResetsTheLockDelayAtMostFifteenTimes()
    {
        var options = new TetrisGameOptions(InitialFallIntervalMs: 100_000);
        var game = CreateGame(options, TetrominoType.O, TetrominoType.I, TetrominoType.T);
        SoftDropToFloor(game);

        for (var i = 0; i < 15; i++)
        {
            game.Advance(TimeSpan.FromMilliseconds(499));
            if (i % 2 == 0)
            {
                game.MoveLeft();
            }
            else
            {
                game.MoveRight();
            }
        }

        game.Advance(TimeSpan.FromMilliseconds(499));
        game.MoveRight();
        game.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(TetrominoType.I, game.ActivePiece.Type);
    }

    [Fact]
    public void LeavingAPlatformAfterUsingAllLockResetsPreservesTheRemainingLockDelay()
    {
        var options = new TetrisGameOptions(InitialFallIntervalMs: 100_000);
        var game = CreateGame(options, TetrominoType.O, TetrominoType.I, TetrominoType.T);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 10));
        SoftDropToFloor(game);

        for (var i = 0; i < options.MaxLockResets; i++)
        {
            game.Advance(TimeSpan.FromMilliseconds(499));
            Assert.True(i % 2 == 0 ? game.MoveRight() : game.MoveLeft());
        }

        game.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(game.MoveRight());
        Assert.True(game.SoftDrop());
        SoftDropToFloor(game);

        game.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Equal(TetrominoType.O, game.ActivePiece.Type);

        game.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TetrominoType.I, game.ActivePiece.Type);
    }

    [Fact]
    public void RevisionChangesForVisibleMovementButNotForElapsedTimeAlone()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I, TetrominoType.O);
        var initialRevision = game.Revision;

        game.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(initialRevision, game.Revision);

        Assert.True(game.MoveLeft());
        Assert.True(game.Revision > initialRevision);
    }

    [Fact]
    public void RotatingAnOPieceDoesNotChangeTheVisibleRevision()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I, TetrominoType.T);
        var initialRevision = game.Revision;

        Assert.True(game.RotateClockwise());

        Assert.Equal(initialRevision, game.Revision);
    }

    [Fact]
    public void GroundedOPieceRotationResetsLockDelayWithoutChangingRevision()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I, TetrominoType.T);
        SoftDropToFloor(game);
        game.Advance(TimeSpan.FromMilliseconds(499));
        var revisionBeforeRotation = game.Revision;

        Assert.True(game.RotateClockwise());
        Assert.Equal(revisionBeforeRotation, game.Revision);

        game.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal(TetrominoType.O, game.ActivePiece.Type);

        game.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TetrominoType.I, game.ActivePiece.Type);
    }

    [Fact]
    public void BlockedSpawnEndsTheGame()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O, TetrominoType.T);
        SoftDropToFloor(game);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 0));

        game.Advance(TimeSpan.FromMilliseconds(500));

        Assert.True(game.IsGameOver);
        Assert.Equal(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 0), game.ActivePiece);
    }

    [Fact]
    public void RestartClearsStateResetsTheSourceAndStartsItsFreshSequence()
    {
        var source = new StubPieceSource(
            TetrominoType.I,
            TetrominoType.O,
            TetrominoType.T,
            TetrominoType.L);
        var game = new TetrisGameState(new TetrisGameOptions(), source);
        var originalBoard = game.Board;
        game.Hold();
        game.HardDrop();

        game.Restart();

        Assert.Equal(1, source.ResetCount);
        Assert.NotSame(originalBoard, game.Board);
        Assert.Equal(new ActivePiece(TetrominoType.I, RotationState.Spawn, 3, 0), game.ActivePiece);
        Assert.Equal(TetrominoType.O, game.NextPiece);
        Assert.Null(game.HoldPiece);
        Assert.Equal(0, game.Score);
        Assert.Equal(0, game.TotalLines);
        Assert.Equal(1, game.Level);
        Assert.False(game.IsGameOver);
        AssertBoardIsEmpty(game.Board);
    }

    private static TetrisGameState CreateGame(params TetrominoType[] pieces) =>
        CreateGame(new TetrisGameOptions(), pieces);

    private static TetrisGameState CreateGame(TetrisGameOptions options, params TetrominoType[] pieces) =>
        new(options, new StubPieceSource(pieces));

    private static void SoftDropToFloor(TetrisGameState game)
    {
        for (var i = 0; i < TetrisBoard.TotalHeight; i++)
        {
            game.SoftDrop();
        }
    }

    private static void FillBottomRowOutsideIPlacement(TetrisBoard board)
    {
        board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, -1, 20));
        board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 0, 20));
        board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 6, 20));
        board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 7, 20));
    }

    private static void AssertBoardIsEmpty(TetrisBoard board)
    {
        for (var y = 0; y < TetrisBoard.TotalHeight; y++)
        {
            for (var x = 0; x < TetrisBoard.Width; x++)
            {
                Assert.Null(board.GetCell(x, y));
            }
        }
    }

    private sealed class StubPieceSource : IPieceSource
    {
        private readonly TetrominoType[] _sequence;
        private Queue<TetrominoType> _pieces;

        public StubPieceSource(params TetrominoType[] sequence)
        {
            _sequence = sequence;
            _pieces = new Queue<TetrominoType>(_sequence);
        }

        public int ResetCount { get; private set; }

        public TetrominoType Next() => _pieces.Dequeue();

        public void Reset()
        {
            ResetCount++;
            _pieces = new Queue<TetrominoType>(_sequence);
        }
    }
}
