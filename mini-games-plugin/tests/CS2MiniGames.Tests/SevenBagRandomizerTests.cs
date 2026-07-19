using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class SevenBagRandomizerTests
{
    [Fact]
    public void EveryConsecutiveBagContainsAllSevenTetrominoes()
    {
        IPieceSource bag = new SevenBagRandomizer(new Random(12345));

        var pieces = Enumerable.Range(0, 14)
            .Select(_ => bag.Next())
            .ToArray();
        var expected = Enum.GetValues<TetrominoType>();

        Assert.Equal(expected, pieces.Take(7).OrderBy(type => type));
        Assert.Equal(expected, pieces.Skip(7).Take(7).OrderBy(type => type));
    }

    [Fact]
    public void ResetDiscardsThePartialBag()
    {
        IPieceSource bag = new SevenBagRandomizer(new Random(12345));
        _ = bag.Next();
        _ = bag.Next();
        _ = bag.Next();

        bag.Reset();

        var piecesAfterReset = Enumerable.Range(0, 7)
            .Select(_ => bag.Next())
            .OrderBy(type => type);
        Assert.Equal(Enum.GetValues<TetrominoType>(), piecesAfterReset);
    }
}
