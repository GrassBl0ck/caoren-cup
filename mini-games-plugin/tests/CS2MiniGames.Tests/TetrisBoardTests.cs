using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisBoardTests
{
    [Fact]
    public void UsesStandardBoardDimensions()
    {
        Assert.Equal(10, TetrisBoard.Width);
        Assert.Equal(22, TetrisBoard.TotalHeight);
        Assert.Equal(2, TetrisBoard.HiddenRows);
    }

    [Theory]
    [InlineData(TetrominoType.I, RotationState.Spawn, -1, 0)]
    [InlineData(TetrominoType.I, RotationState.Spawn, 7, 0)]
    [InlineData(TetrominoType.T, RotationState.Spawn, 0, -1)]
    [InlineData(TetrominoType.I, RotationState.Reverse, 0, 20)]
    public void CannotPlaceCellsOutsideTheBoard(
        TetrominoType type,
        RotationState rotation,
        int x,
        int y)
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(type, rotation, x, y);

        Assert.False(board.CanPlace(piece));
    }

    [Fact]
    public void CannotPlaceOverlappingLockedCells()
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(TetrominoType.T, RotationState.Spawn, 3, 4);
        board.Lock(piece);

        Assert.False(board.CanPlace(piece));
    }

    [Fact]
    public void LockWritesAllFourCells()
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(TetrominoType.T, RotationState.Spawn, 3, 4);

        board.Lock(piece);

        Assert.Equal(TetrominoType.T, board.GetCell(4, 4));
        Assert.Equal(TetrominoType.T, board.GetCell(3, 5));
        Assert.Equal(TetrominoType.T, board.GetCell(4, 5));
        Assert.Equal(TetrominoType.T, board.GetCell(5, 5));
    }

    [Fact]
    public void ClearingOneFullRowCompactsRowsDownward()
    {
        var board = new TetrisBoard();
        board.Lock(new ActivePiece(TetrominoType.I, RotationState.Spawn, 0, 20));
        board.Lock(new ActivePiece(TetrominoType.I, RotationState.Spawn, 4, 20));
        board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 7, 20));

        var cleared = board.ClearFullLines();

        Assert.Equal(1, cleared);
        Assert.Equal(TetrominoType.O, board.GetCell(8, 21));
        Assert.Equal(TetrominoType.O, board.GetCell(9, 21));
        Assert.Null(board.GetCell(0, 21));
    }

    [Fact]
    public void ClearingFourFullRowsReturnsFourAndEmptiesTheBoard()
    {
        var board = new TetrisBoard();
        foreach (var y in new[] { 18, 20 })
        {
            foreach (var x in new[] { -1, 1, 3, 5, 7 })
            {
                board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, x, y));
            }
        }

        var cleared = board.ClearFullLines();

        Assert.Equal(4, cleared);
        for (var y = 0; y < TetrisBoard.TotalHeight; y++)
        {
            for (var x = 0; x < TetrisBoard.Width; x++)
            {
                Assert.Null(board.GetCell(x, y));
            }
        }
    }
}
