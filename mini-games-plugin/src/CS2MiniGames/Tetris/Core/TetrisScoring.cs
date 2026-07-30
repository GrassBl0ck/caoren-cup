namespace CS2MiniGames.Tetris.Core;

public static class TetrisScoring
{
    public static int ScoreForLines(int clearedLines, int currentLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(currentLevel, 0);

        var baseScore = clearedLines switch
        {
            1 => 100,
            2 => 300,
            3 => 500,
            4 => 800,
            _ => 0
        };

        return baseScore * currentLevel;
    }

    public static int LevelForLines(int totalLines, int linesPerLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(linesPerLevel, 0);
        return (totalLines / linesPerLevel) + 1;
    }

    public static int FallInterval(int level, int initialMs, int minimumMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(level, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialMs, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumMs, 0);

        var interval = (int)Math.Round(initialMs * Math.Pow(0.85, level - 1));
        return Math.Max(minimumMs, interval);
    }
}
