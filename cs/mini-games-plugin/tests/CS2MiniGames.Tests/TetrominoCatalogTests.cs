using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrominoCatalogTests
{
    [Fact]
    public void DefinesExactlySevenTetrominoTypes()
    {
        Assert.Equal(7, Enum.GetValues<TetrominoType>().Length);
    }

    [Theory]
    [InlineData(TetrominoType.I)]
    [InlineData(TetrominoType.O)]
    [InlineData(TetrominoType.T)]
    [InlineData(TetrominoType.S)]
    [InlineData(TetrominoType.Z)]
    [InlineData(TetrominoType.J)]
    [InlineData(TetrominoType.L)]
    public void EveryRotationHasFourUniqueCells(TetrominoType type)
    {
        foreach (var rotation in Enum.GetValues<RotationState>())
        {
            var cells = TetrominoCatalog.GetCells(type, rotation);
            Assert.Equal(4, cells.Count);
            Assert.Equal(4, cells.Distinct().Count());
        }
    }

    [Fact]
    public void AllORotationsUseTheSameCoordinates()
    {
        var spawnCells = TetrominoCatalog.GetCells(TetrominoType.O, RotationState.Spawn);

        foreach (var rotation in Enum.GetValues<RotationState>())
        {
            Assert.True(spawnCells.SequenceEqual(TetrominoCatalog.GetCells(TetrominoType.O, rotation)));
        }
    }

    [Fact]
    public void ISpawnUsesStandardHorizontalCoordinates()
    {
        Assert.Equal(
            new[] { new Cell(0, 1), new Cell(1, 1), new Cell(2, 1), new Cell(3, 1) },
            TetrominoCatalog.GetCells(TetrominoType.I, RotationState.Spawn));
    }

    [Fact]
    public void ActivePieceMovesWithoutChangingItsTypeOrRotation()
    {
        var piece = new ActivePiece(TetrominoType.T, RotationState.Right, 3, 4);

        Assert.Equal(
            new ActivePiece(TetrominoType.T, RotationState.Right, 1, 9),
            piece.Move(-2, 5));
    }

    [Fact]
    public void ActivePieceRotatesWithoutChangingItsPositionOrType()
    {
        var piece = new ActivePiece(TetrominoType.L, RotationState.Spawn, 6, 8);

        Assert.Equal(
            new ActivePiece(TetrominoType.L, RotationState.Left, 6, 8),
            piece.RotateTo(RotationState.Left));
    }
}
