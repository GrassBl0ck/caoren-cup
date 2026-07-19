using CounterStrikeSharp.API.Core;

namespace CS2MiniGames;

public sealed class MiniGamesConfig : BasePluginConfig
{
    public int InitialFallIntervalMs { get; set; } = 800;

    public int MinimumFallIntervalMs { get; set; } = 80;

    public int LockDelayMs { get; set; } = 500;

    public int MaxLockResets { get; set; } = 15;

    public int HorizontalRepeatDelayMs { get; set; } = 150;

    public int HorizontalRepeatIntervalMs { get; set; } = 50;

    public int LinesPerLevel { get; set; } = 10;

    public MiniGamesConfig Normalize(Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);

        var initialFallIntervalMs = Clamp(
            nameof(InitialFallIntervalMs),
            InitialFallIntervalMs,
            100,
            5_000,
            warn);
        var minimumFallIntervalMs = Clamp(
            nameof(MinimumFallIntervalMs),
            MinimumFallIntervalMs,
            20,
            Math.Min(1_000, initialFallIntervalMs),
            warn);

        return new MiniGamesConfig
        {
            Version = Version,
            InitialFallIntervalMs = initialFallIntervalMs,
            MinimumFallIntervalMs = minimumFallIntervalMs,
            LockDelayMs = Clamp(nameof(LockDelayMs), LockDelayMs, 100, 2_000, warn),
            MaxLockResets = Clamp(nameof(MaxLockResets), MaxLockResets, 0, 50, warn),
            HorizontalRepeatDelayMs = Clamp(
                nameof(HorizontalRepeatDelayMs),
                HorizontalRepeatDelayMs,
                50,
                1_000,
                warn),
            HorizontalRepeatIntervalMs = Clamp(
                nameof(HorizontalRepeatIntervalMs),
                HorizontalRepeatIntervalMs,
                20,
                500,
                warn),
            LinesPerLevel = Clamp(nameof(LinesPerLevel), LinesPerLevel, 1, 50, warn)
        };
    }

    private static int Clamp(
        string field,
        int value,
        int minimum,
        int maximum,
        Action<string> warn)
    {
        var normalized = Math.Clamp(value, minimum, maximum);
        if (normalized != value)
        {
            warn($"{field} 的值 {value} 超出范围，已调整为 {normalized}。");
        }

        return normalized;
    }
}
