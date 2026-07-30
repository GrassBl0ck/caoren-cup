namespace CS2MiniGames.Tetris.Core;

public sealed record TetrisGameOptions(
    int InitialFallIntervalMs = 800,
    int MinimumFallIntervalMs = 80,
    int LockDelayMs = 500,
    int MaxLockResets = 15,
    int HorizontalRepeatDelayMs = 150,
    int HorizontalRepeatIntervalMs = 50,
    int LinesPerLevel = 10);
