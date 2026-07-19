using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisRotationSystemTests
{
    [Theory]
    [InlineData(RotationState.Spawn, true, RotationState.Right)]
    [InlineData(RotationState.Right, true, RotationState.Reverse)]
    [InlineData(RotationState.Reverse, true, RotationState.Left)]
    [InlineData(RotationState.Left, true, RotationState.Spawn)]
    [InlineData(RotationState.Spawn, false, RotationState.Left)]
    [InlineData(RotationState.Left, false, RotationState.Reverse)]
    [InlineData(RotationState.Reverse, false, RotationState.Right)]
    [InlineData(RotationState.Right, false, RotationState.Spawn)]
    public void UnobstructedRotationsKeepTheOrigin(
        RotationState from,
        bool clockwise,
        RotationState expected)
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(TetrominoType.T, from, 3, 5);

        var rotated = TetrisRotationSystem.TryRotate(board, piece, clockwise, out var result);

        Assert.True(rotated);
        Assert.Equal(new ActivePiece(TetrominoType.T, expected, 3, 5), result);
    }

    [Fact]
    public void JlstzUsesTheDocumentedLeftWallKick()
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(TetrominoType.T, RotationState.Right, -1, 5);
        Assert.True(board.CanPlace(piece));

        var rotated = TetrisRotationSystem.TryRotate(board, piece, clockwise: false, out var result);

        Assert.True(rotated);
        Assert.Equal(new ActivePiece(TetrominoType.T, RotationState.Spawn, 0, 5), result);
    }

    [Fact]
    public void IUsesItsIndependentLeftWallKickTable()
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(TetrominoType.I, RotationState.Right, -2, 5);
        Assert.True(board.CanPlace(piece));

        var rotated = TetrisRotationSystem.TryRotate(board, piece, clockwise: false, out var result);

        Assert.True(rotated);
        Assert.Equal(new ActivePiece(TetrominoType.I, RotationState.Spawn, 0, 5), result);
    }

    [Fact]
    public void ORotationKeepsItsOccupiedCellsInPlace()
    {
        var board = new TetrisBoard();
        var piece = new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 5);
        var before = OccupiedCells(piece);

        var rotated = TetrisRotationSystem.TryRotate(board, piece, clockwise: true, out var result);

        Assert.True(rotated);
        Assert.Equal(RotationState.Right, result.Rotation);
        Assert.Equal(piece.X, result.X);
        Assert.Equal(piece.Y, result.Y);
        Assert.Equal(before, OccupiedCells(result));
    }

    [Fact]
    public void RotationFailsAndReturnsOriginalPieceWhenAllFiveKickTargetsCollide()
    {
        var board = new TetrisBoard();
        board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 1, 4));
        board.Lock(new ActivePiece(TetrominoType.I, RotationState.Spawn, 2, 6));
        var piece = new ActivePiece(TetrominoType.T, RotationState.Spawn, 3, 5);
        Assert.True(board.CanPlace(piece));

        var rotated = TetrisRotationSystem.TryRotate(board, piece, clockwise: true, out var result);

        Assert.False(rotated);
        Assert.Equal(piece, result);
    }

    private static Cell[] OccupiedCells(ActivePiece piece) =>
        TetrominoCatalog.GetCells(piece.Type, piece.Rotation)
            .Select(cell => new Cell(piece.X + cell.X, piece.Y + cell.Y))
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
}
