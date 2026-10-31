namespace CS2MiniGames.Tetris.Core;

public readonly record struct ActivePiece(
    TetrominoType Type,
    RotationState Rotation,
    int X,
    int Y)
{
    public ActivePiece Move(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public ActivePiece RotateTo(RotationState rotation) => this with { Rotation = rotation };
}
