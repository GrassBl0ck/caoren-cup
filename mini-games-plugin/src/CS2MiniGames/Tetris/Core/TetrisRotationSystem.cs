namespace CS2MiniGames.Tetris.Core;

public static class TetrisRotationSystem
{
    private static readonly IReadOnlyDictionary<(RotationState From, RotationState To), IReadOnlyList<Cell>>
        JlstzKickOffsets = new Dictionary<(RotationState, RotationState), IReadOnlyList<Cell>>
        {
            [(RotationState.Spawn, RotationState.Right)] = Offsets(new(0, 0), new(-1, 0), new(-1, -1), new(0, 2), new(-1, 2)),
            [(RotationState.Right, RotationState.Spawn)] = Offsets(new(0, 0), new(1, 0), new(1, 1), new(0, -2), new(1, -2)),
            [(RotationState.Right, RotationState.Reverse)] = Offsets(new(0, 0), new(1, 0), new(1, 1), new(0, -2), new(1, -2)),
            [(RotationState.Reverse, RotationState.Right)] = Offsets(new(0, 0), new(-1, 0), new(-1, -1), new(0, 2), new(-1, 2)),
            [(RotationState.Reverse, RotationState.Left)] = Offsets(new(0, 0), new(1, 0), new(1, -1), new(0, 2), new(1, 2)),
            [(RotationState.Left, RotationState.Reverse)] = Offsets(new(0, 0), new(-1, 0), new(-1, 1), new(0, -2), new(-1, -2)),
            [(RotationState.Left, RotationState.Spawn)] = Offsets(new(0, 0), new(-1, 0), new(-1, 1), new(0, -2), new(-1, -2)),
            [(RotationState.Spawn, RotationState.Left)] = Offsets(new(0, 0), new(1, 0), new(1, -1), new(0, 2), new(1, 2))
        };

    private static readonly IReadOnlyDictionary<(RotationState From, RotationState To), IReadOnlyList<Cell>>
        IKickOffsets = new Dictionary<(RotationState, RotationState), IReadOnlyList<Cell>>
        {
            [(RotationState.Spawn, RotationState.Right)] = Offsets(new(0, 0), new(-2, 0), new(1, 0), new(-2, 1), new(1, -2)),
            [(RotationState.Right, RotationState.Spawn)] = Offsets(new(0, 0), new(2, 0), new(-1, 0), new(2, -1), new(-1, 2)),
            [(RotationState.Right, RotationState.Reverse)] = Offsets(new(0, 0), new(-1, 0), new(2, 0), new(-1, -2), new(2, 1)),
            [(RotationState.Reverse, RotationState.Right)] = Offsets(new(0, 0), new(1, 0), new(-2, 0), new(1, 2), new(-2, -1)),
            [(RotationState.Reverse, RotationState.Left)] = Offsets(new(0, 0), new(2, 0), new(-1, 0), new(2, -1), new(-1, 2)),
            [(RotationState.Left, RotationState.Reverse)] = Offsets(new(0, 0), new(-2, 0), new(1, 0), new(-2, 1), new(1, -2)),
            [(RotationState.Left, RotationState.Spawn)] = Offsets(new(0, 0), new(1, 0), new(-2, 0), new(1, 2), new(-2, -1)),
            [(RotationState.Spawn, RotationState.Left)] = Offsets(new(0, 0), new(-1, 0), new(2, 0), new(-1, -2), new(2, 1))
        };

    public static bool TryRotate(
        TetrisBoard board,
        ActivePiece piece,
        bool clockwise,
        out ActivePiece result)
    {
        var targetRotation = NextRotation(piece.Rotation, clockwise);
        var rotatedPiece = piece.RotateTo(targetRotation);

        if (piece.Type == TetrominoType.O)
        {
            if (board.CanPlace(rotatedPiece))
            {
                result = rotatedPiece;
                return true;
            }

            result = piece;
            return false;
        }

        var kickOffsets = piece.Type == TetrominoType.I ? IKickOffsets : JlstzKickOffsets;
        foreach (var offset in kickOffsets[(piece.Rotation, targetRotation)])
        {
            var candidate = rotatedPiece.Move(offset.X, offset.Y);
            if (board.CanPlace(candidate))
            {
                result = candidate;
                return true;
            }
        }

        result = piece;
        return false;
    }

    private static RotationState NextRotation(RotationState rotation, bool clockwise) =>
        (rotation, clockwise) switch
        {
            (RotationState.Spawn, true) => RotationState.Right,
            (RotationState.Right, true) => RotationState.Reverse,
            (RotationState.Reverse, true) => RotationState.Left,
            (RotationState.Left, true) => RotationState.Spawn,
            (RotationState.Spawn, false) => RotationState.Left,
            (RotationState.Left, false) => RotationState.Reverse,
            (RotationState.Reverse, false) => RotationState.Right,
            (RotationState.Right, false) => RotationState.Spawn,
            _ => throw new ArgumentOutOfRangeException(nameof(rotation), rotation, null)
        };

    private static IReadOnlyList<Cell> Offsets(params Cell[] offsets) => Array.AsReadOnly(offsets);
}
