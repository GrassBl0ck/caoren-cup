namespace CS2MiniGames.Tetris.Core;

public sealed class TetrisBoard
{
    public const int Width = 10;
    public const int TotalHeight = 22;
    public const int HiddenRows = 2;

    private readonly TetrominoType?[,] _cells = new TetrominoType?[TotalHeight, Width];

    public bool CanPlace(ActivePiece piece)
    {
        foreach (var cell in TetrominoCatalog.GetCells(piece.Type, piece.Rotation))
        {
            var x = piece.X + cell.X;
            var y = piece.Y + cell.Y;
            if (x < 0 || x >= Width || y < 0 || y >= TotalHeight || _cells[y, x].HasValue)
            {
                return false;
            }
        }

        return true;
    }

    public void Lock(ActivePiece piece)
    {
        foreach (var cell in TetrominoCatalog.GetCells(piece.Type, piece.Rotation))
        {
            _cells[piece.Y + cell.Y, piece.X + cell.X] = piece.Type;
        }
    }

    public int ClearFullLines()
    {
        var cleared = 0;
        for (var y = TotalHeight - 1; y >= 0;)
        {
            if (!IsFull(y))
            {
                y--;
                continue;
            }

            for (var sourceY = y - 1; sourceY >= 0; sourceY--)
            {
                for (var x = 0; x < Width; x++)
                {
                    _cells[sourceY + 1, x] = _cells[sourceY, x];
                }
            }

            for (var x = 0; x < Width; x++)
            {
                _cells[0, x] = null;
            }

            cleared++;
        }

        return cleared;
    }

    public TetrominoType? GetCell(int x, int y) => _cells[y, x];

    private bool IsFull(int y)
    {
        for (var x = 0; x < Width; x++)
        {
            if (!_cells[y, x].HasValue)
            {
                return false;
            }
        }

        return true;
    }
}
