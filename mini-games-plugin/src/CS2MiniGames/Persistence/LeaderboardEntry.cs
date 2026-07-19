namespace CS2MiniGames.Persistence;

public sealed record LeaderboardEntry(
    string Game,
    ulong SteamId,
    string PlayerName,
    int Score,
    int Lines,
    int Level,
    DateTimeOffset UpdatedUtc);
