using System.Collections.ObjectModel;

namespace CS2MiniGames.Tetris.Core;

public static class TetrominoCatalog
{
    private static readonly IReadOnlyDictionary<(TetrominoType Type, RotationState Rotation), IReadOnlyList<Cell>>
        CellsByPieceAndRotation = CreateCatalog();

    public static IReadOnlyList<Cell> GetCells(TetrominoType type, RotationState rotation) =>
        CellsByPieceAndRotation[(type, rotation)];

    private static IReadOnlyDictionary<(TetrominoType Type, RotationState Rotation), IReadOnlyList<Cell>>
        CreateCatalog()
    {
        var oCells = Cells(new(1, 0), new(2, 0), new(1, 1), new(2, 1));
        var catalog = new Dictionary<(TetrominoType, RotationState), IReadOnlyList<Cell>>
        {
            [(TetrominoType.I, RotationState.Spawn)] = Cells(new(0, 1), new(1, 1), new(2, 1), new(3, 1)),
            [(TetrominoType.I, RotationState.Right)] = Cells(new(2, 0), new(2, 1), new(2, 2), new(2, 3)),
            [(TetrominoType.I, RotationState.Reverse)] = Cells(new(0, 2), new(1, 2), new(2, 2), new(3, 2)),
            [(TetrominoType.I, RotationState.Left)] = Cells(new(1, 0), new(1, 1), new(1, 2), new(1, 3)),

            [(TetrominoType.O, RotationState.Spawn)] = oCells,
            [(TetrominoType.O, RotationState.Right)] = oCells,
            [(TetrominoType.O, RotationState.Reverse)] = oCells,
            [(TetrominoType.O, RotationState.Left)] = oCells,

            [(TetrominoType.T, RotationState.Spawn)] = Cells(new(1, 0), new(0, 1), new(1, 1), new(2, 1)),
            [(TetrominoType.T, RotationState.Right)] = Cells(new(1, 0), new(1, 1), new(2, 1), new(1, 2)),
            [(TetrominoType.T, RotationState.Reverse)] = Cells(new(0, 1), new(1, 1), new(2, 1), new(1, 2)),
            [(TetrominoType.T, RotationState.Left)] = Cells(new(1, 0), new(0, 1), new(1, 1), new(1, 2)),

            [(TetrominoType.J, RotationState.Spawn)] = Cells(new(0, 0), new(0, 1), new(1, 1), new(2, 1)),
            [(TetrominoType.J, RotationState.Right)] = Cells(new(1, 0), new(2, 0), new(1, 1), new(1, 2)),
            [(TetrominoType.J, RotationState.Reverse)] = Cells(new(0, 1), new(1, 1), new(2, 1), new(2, 2)),
            [(TetrominoType.J, RotationState.Left)] = Cells(new(1, 0), new(1, 1), new(0, 2), new(1, 2)),

            [(TetrominoType.L, RotationState.Spawn)] = Cells(new(2, 0), new(0, 1), new(1, 1), new(2, 1)),
            [(TetrominoType.L, RotationState.Right)] = Cells(new(1, 0), new(1, 1), new(1, 2), new(2, 2)),
            [(TetrominoType.L, RotationState.Reverse)] = Cells(new(0, 1), new(1, 1), new(2, 1), new(0, 2)),
            [(TetrominoType.L, RotationState.Left)] = Cells(new(0, 0), new(1, 0), new(1, 1), new(1, 2)),

            [(TetrominoType.S, RotationState.Spawn)] = Cells(new(1, 0), new(2, 0), new(0, 1), new(1, 1)),
            [(TetrominoType.S, RotationState.Right)] = Cells(new(1, 0), new(1, 1), new(2, 1), new(2, 2)),
            [(TetrominoType.S, RotationState.Reverse)] = Cells(new(1, 1), new(2, 1), new(0, 2), new(1, 2)),
            [(TetrominoType.S, RotationState.Left)] = Cells(new(0, 0), new(0, 1), new(1, 1), new(1, 2)),

            [(TetrominoType.Z, RotationState.Spawn)] = Cells(new(0, 0), new(1, 0), new(1, 1), new(2, 1)),
            [(TetrominoType.Z, RotationState.Right)] = Cells(new(2, 0), new(1, 1), new(2, 1), new(1, 2)),
            [(TetrominoType.Z, RotationState.Reverse)] = Cells(new(0, 1), new(1, 1), new(1, 2), new(2, 2)),
            [(TetrominoType.Z, RotationState.Left)] = Cells(new(1, 0), new(0, 1), new(1, 1), new(0, 2))
        };

        return new ReadOnlyDictionary<(TetrominoType, RotationState), IReadOnlyList<Cell>>(catalog);
    }

    private static IReadOnlyList<Cell> Cells(params Cell[] cells) => Array.AsReadOnly(cells);
}
