namespace CS2MiniGames.Tetris.Core;

public sealed class SevenBagRandomizer : IPieceSource
{
    private readonly Random _random;
    private readonly Queue<TetrominoType> _pieces = new();

    public SevenBagRandomizer(Random random)
    {
        _random = random;
    }

    public TetrominoType Next()
    {
        if (_pieces.Count == 0)
        {
            FillBag();
        }

        return _pieces.Dequeue();
    }

    public void Reset() => _pieces.Clear();

    private void FillBag()
    {
        var pieces = Enum.GetValues<TetrominoType>();

        for (var i = pieces.Length - 1; i > 0; i--)
        {
            var swapIndex = _random.Next(i + 1);
            (pieces[i], pieces[swapIndex]) = (pieces[swapIndex], pieces[i]);
        }

        foreach (var piece in pieces)
        {
            _pieces.Enqueue(piece);
        }
    }
}
