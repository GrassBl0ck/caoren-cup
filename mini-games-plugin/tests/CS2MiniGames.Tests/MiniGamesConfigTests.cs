namespace CS2MiniGames.Tests;

public sealed class MiniGamesConfigTests
{
    [Fact]
    public void DefaultsMatchTheTetrisMvpSettings()
    {
        var config = new MiniGamesConfig();

        Assert.Equal(800, config.InitialFallIntervalMs);
        Assert.Equal(80, config.MinimumFallIntervalMs);
        Assert.Equal(500, config.LockDelayMs);
        Assert.Equal(15, config.MaxLockResets);
        Assert.Equal(150, config.HorizontalRepeatDelayMs);
        Assert.Equal(50, config.HorizontalRepeatIntervalMs);
        Assert.Equal(10, config.LinesPerLevel);
    }

    [Fact]
    public void NormalizeClampsEveryOutOfRangeFieldAndWarnsOncePerCorrection()
    {
        var config = new MiniGamesConfig
        {
            InitialFallIntervalMs = 99,
            MinimumFallIntervalMs = 5_001,
            LockDelayMs = 2_001,
            MaxLockResets = 51,
            HorizontalRepeatDelayMs = 49,
            HorizontalRepeatIntervalMs = 501,
            LinesPerLevel = 0
        };
        var warnings = new List<string>();

        var normalized = config.Normalize(warnings.Add);

        Assert.Equal(100, normalized.InitialFallIntervalMs);
        Assert.Equal(100, normalized.MinimumFallIntervalMs);
        Assert.Equal(2_000, normalized.LockDelayMs);
        Assert.Equal(50, normalized.MaxLockResets);
        Assert.Equal(50, normalized.HorizontalRepeatDelayMs);
        Assert.Equal(500, normalized.HorizontalRepeatIntervalMs);
        Assert.Equal(1, normalized.LinesPerLevel);
        Assert.Equal(7, warnings.Count);
        Assert.Equal(7, warnings.Distinct().Count());
    }

    [Fact]
    public void NormalizeUsesEachDocumentedBoundaryWithoutWarnings()
    {
        var lower = new MiniGamesConfig
        {
            InitialFallIntervalMs = 100,
            MinimumFallIntervalMs = 20,
            LockDelayMs = 100,
            MaxLockResets = 0,
            HorizontalRepeatDelayMs = 50,
            HorizontalRepeatIntervalMs = 20,
            LinesPerLevel = 1
        };
        var upper = new MiniGamesConfig
        {
            InitialFallIntervalMs = 5_000,
            MinimumFallIntervalMs = 1_000,
            LockDelayMs = 2_000,
            MaxLockResets = 50,
            HorizontalRepeatDelayMs = 1_000,
            HorizontalRepeatIntervalMs = 500,
            LinesPerLevel = 50
        };
        var warnings = new List<string>();

        Assert.Equivalent(lower, lower.Normalize(warnings.Add));
        Assert.Equivalent(upper, upper.Normalize(warnings.Add));
        Assert.Empty(warnings);
    }

    [Fact]
    public void NormalizeReturnsACopyAndLimitsMinimumFallIntervalToInitial()
    {
        var config = new MiniGamesConfig
        {
            InitialFallIntervalMs = 400,
            MinimumFallIntervalMs = 700
        };
        var warnings = new List<string>();

        var normalized = config.Normalize(warnings.Add);

        Assert.NotSame(config, normalized);
        Assert.Equal(700, config.MinimumFallIntervalMs);
        Assert.Equal(400, normalized.MinimumFallIntervalMs);
        Assert.Single(warnings);
    }
}
