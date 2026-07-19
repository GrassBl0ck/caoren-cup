using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisScoringTests
{
    [Theory]
    [InlineData(1, 1, 100)]
    [InlineData(2, 1, 300)]
    [InlineData(3, 1, 500)]
    [InlineData(4, 1, 800)]
    [InlineData(1, 3, 300)]
    [InlineData(4, 5, 4000)]
    public void ScoresSupportedLineCountsAtTheCurrentLevel(
        int clearedLines,
        int currentLevel,
        int expected)
    {
        Assert.Equal(expected, TetrisScoring.ScoreForLines(clearedLines, currentLevel));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public void UnsupportedLineCountsScoreZero(int clearedLines)
    {
        Assert.Equal(0, TetrisScoring.ScoreForLines(clearedLines, 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 3)]
    public void LevelStartsAtOneAndRisesEveryConfiguredLineCount(int totalLines, int expected)
    {
        Assert.Equal(expected, TetrisScoring.LevelForLines(totalLines, 10));
    }

    [Theory]
    [InlineData(1, 800)]
    [InlineData(2, 680)]
    [InlineData(3, 578)]
    [InlineData(4, 491)]
    [InlineData(15, 82)]
    [InlineData(16, 80)]
    [InlineData(30, 80)]
    public void FallIntervalDecaysExponentiallyWithoutFallingBelowTheMinimum(int level, int expected)
    {
        Assert.Equal(expected, TetrisScoring.FallInterval(level, 800, 80));
    }

    [Fact]
    public void RejectsNonPositiveLevels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TetrisScoring.ScoreForLines(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TetrisScoring.FallInterval(0, 800, 80));
    }

    [Fact]
    public void RejectsNonPositiveConfiguredValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TetrisScoring.LevelForLines(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TetrisScoring.FallInterval(1, 0, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => TetrisScoring.FallInterval(1, 800, 0));
    }

    [Fact]
    public void OptionsExposeTheSpecifiedDefaults()
    {
        var options = new TetrisGameOptions();

        Assert.Equal(800, options.InitialFallIntervalMs);
        Assert.Equal(80, options.MinimumFallIntervalMs);
        Assert.Equal(500, options.LockDelayMs);
        Assert.Equal(15, options.MaxLockResets);
        Assert.Equal(150, options.HorizontalRepeatDelayMs);
        Assert.Equal(50, options.HorizontalRepeatIntervalMs);
        Assert.Equal(10, options.LinesPerLevel);
    }
}
