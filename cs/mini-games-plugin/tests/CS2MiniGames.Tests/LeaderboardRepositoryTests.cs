using CS2MiniGames.Persistence;
using Microsoft.Data.Sqlite;

namespace CS2MiniGames.Tests;

public sealed class LeaderboardRepositoryTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _databasePath;

    public LeaderboardRepositoryTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "CS2MiniGames.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);
        _databasePath = Path.Combine(_temporaryDirectory, "leaderboard.db");
    }

    [Fact]
    public void InitializeCreatesTheExactLeaderboardSchema()
    {
        using var repository = new LeaderboardRepository(_databasePath);

        repository.Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(leaderboard);";
        using var reader = command.ExecuteReader();

        var columns = new List<(string Name, string Type, bool NotNull, int PrimaryKeyOrder)>();
        while (reader.Read())
        {
            columns.Add((
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) == 1,
                checked((int)reader.GetInt64(5))));
        }

        Assert.Equal(
            [
                ("game", "TEXT", true, 1),
                ("steam_id", "TEXT", true, 2),
                ("player_name", "TEXT", true, 0),
                ("score", "INTEGER", true, 0),
                ("lines", "INTEGER", true, 0),
                ("level", "INTEGER", true, 0),
                ("updated_utc", "TEXT", true, 0),
            ],
            columns);
    }

    [Fact]
    public void SaveIfHigherInsertsAndReturnsTheFirstPlayerResult()
    {
        var updated = new DateTimeOffset(2026, 7, 17, 8, 9, 10, 123, TimeSpan.FromHours(8))
            .AddTicks(4567);
        var entry = new LeaderboardEntry(
            "tetris'quoted",
            76561198000000001UL,
            "Player ' One",
            12345,
            42,
            5,
            updated);
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();

        repository.SaveIfHigher(entry);

        var saved = repository.GetPlayerBest(entry.Game, entry.SteamId);
        Assert.NotNull(saved);
        Assert.Equal(entry with { UpdatedUtc = updated.ToUniversalTime() }, saved);
        Assert.Equal(TimeSpan.Zero, saved.UpdatedUtc.Offset);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT steam_id, updated_utc FROM leaderboard;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("76561198000000001", reader.GetString(0));
        Assert.Equal(updated.ToUniversalTime().ToString("O"), reader.GetString(1));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    public void SaveIfHigherLeavesEveryFieldUnchangedForLowerOrEqualScores(int rejectedScore)
    {
        var original = new LeaderboardEntry(
            "tetris",
            76561198000000002UL,
            "Original",
            100,
            10,
            2,
            new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero));
        var rejected = original with
        {
            PlayerName = "Rejected",
            Score = rejectedScore,
            Lines = 999,
            Level = 99,
            UpdatedUtc = original.UpdatedUtc.AddHours(1),
        };
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();
        repository.SaveIfHigher(original);

        repository.SaveIfHigher(rejected);

        Assert.Equal(original, repository.GetPlayerBest(original.Game, original.SteamId));
    }

    [Fact]
    public void SaveIfHigherReplacesAllResultFieldsForAStrictlyHigherScore()
    {
        var original = new LeaderboardEntry(
            "tetris",
            76561198000000003UL,
            "Original",
            100,
            10,
            2,
            new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero));
        var higher = original with
        {
            PlayerName = "New name",
            Score = 101,
            Lines = 8,
            Level = 3,
            UpdatedUtc = original.UpdatedUtc.AddMinutes(5),
        };
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();
        repository.SaveIfHigher(original);

        repository.SaveIfHigher(higher);

        Assert.Equal(higher, repository.GetPlayerBest(original.Game, original.SteamId));
    }

    [Fact]
    public void RepositoryKeepsTheSamePlayerIsolatedByGame()
    {
        const ulong steamId = 76561198000000004UL;
        var updated = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        var tetris = new LeaderboardEntry("tetris", steamId, "Tetris player", 500, 20, 3, updated);
        var snake = new LeaderboardEntry("snake", steamId, "Snake player", 75, 0, 1, updated.AddMinutes(1));
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();
        repository.SaveIfHigher(tetris);
        repository.SaveIfHigher(snake);

        Assert.Equal(tetris, repository.GetPlayerBest("tetris", steamId));
        Assert.Equal(snake, repository.GetPlayerBest("snake", steamId));
    }

    [Fact]
    public void GetPlayerBestReturnsNullWhenThePlayerDoesNotExist()
    {
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();

        var missing = repository.GetPlayerBest("tetris", 76561198000000005UL);

        Assert.Null(missing);
    }

    [Fact]
    public void GetTopOrdersByScoreThenLinesThenOldestUpdateAndIsolatesGames()
    {
        var time = new DateTimeOffset(2026, 7, 17, 1, 0, 0, TimeSpan.Zero);
        var expected = new[]
        {
            new LeaderboardEntry("tetris", 11, "Highest score", 300, 1, 1, time.AddMinutes(4)),
            new LeaderboardEntry("tetris", 12, "Older tied score", 200, 5, 1, time.AddMinutes(1)),
            new LeaderboardEntry("tetris", 13, "Newer tied score", 200, 5, 1, time.AddMinutes(3)),
            new LeaderboardEntry("tetris", 14, "Fewer lines", 200, 3, 1, time),
        };
        var otherGame = new LeaderboardEntry("snake", 15, "Other game", 9999, 999, 99, time);
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();
        foreach (var entry in expected.Reverse().Append(otherGame))
        {
            repository.SaveIfHigher(entry);
        }

        var top = repository.GetTop("tetris", 10);

        Assert.Equal(expected, top);
    }

    [Fact]
    public void GetTopLimitsTheResultToTenEntries()
    {
        var time = new DateTimeOffset(2026, 7, 17, 1, 0, 0, TimeSpan.Zero);
        using var repository = new LeaderboardRepository(_databasePath);
        repository.Initialize();
        for (ulong index = 1; index <= 12; index++)
        {
            repository.SaveIfHigher(new LeaderboardEntry(
                "tetris",
                index,
                $"Player {index}",
                checked((int)index),
                0,
                1,
                time.AddMinutes((int)index)));
        }

        var top = repository.GetTop("tetris", 10);

        Assert.Equal(10, top.Count);
        Assert.Equal(Enumerable.Range(3, 10).Reverse(), top.Select(entry => entry.Score));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_temporaryDirectory))
        {
            return;
        }

        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private SqliteConnection OpenConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
