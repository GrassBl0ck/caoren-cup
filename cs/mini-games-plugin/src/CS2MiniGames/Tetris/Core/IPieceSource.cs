namespace CS2MiniGames.Tetris.Core;

public interface IPieceSource
{
    TetrominoType Next();

    void Reset();
}
