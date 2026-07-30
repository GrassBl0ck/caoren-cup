using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CS2MiniGames.Persistence;

public sealed class LeaderboardRepository : IDisposable
{
    private readonly SqliteConnection _connection;

    public LeaderboardRepository(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
        _connection = new SqliteConnection(connectionString);
    }

    public void Initialize()
    {
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS leaderboard(
                game TEXT NOT NULL,
                steam_id TEXT NOT NULL,
                player_name TEXT NOT NULL,
                score INTEGER NOT NULL,
                lines INTEGER NOT NULL,
                level INTEGER NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(game,steam_id)
            );
            """;
        command.ExecuteNonQuery();
    }

    public void SaveIfHigher(LeaderboardEntry entry)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO leaderboard(game, steam_id, player_name, score, lines, level, updated_utc)
            VALUES(@game, @steam_id, @player_name, @score, @lines, @level, @updated_utc)
            ON CONFLICT(game, steam_id) DO UPDATE SET
                player_name = excluded.player_name,
                score = excluded.score,
                lines = excluded.lines,
                level = excluded.level,
                updated_utc = excluded.updated_utc
            WHERE excluded.score > leaderboard.score;
            """;
        command.Parameters.AddWithValue("@game", entry.Game);
        command.Parameters.AddWithValue(
            "@steam_id",
            entry.SteamId.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@player_name", entry.PlayerName);
        command.Parameters.AddWithValue("@score", entry.Score);
        command.Parameters.AddWithValue("@lines", entry.Lines);
        command.Parameters.AddWithValue("@level", entry.Level);
        command.Parameters.AddWithValue(
            "@updated_utc",
            entry.UpdatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public LeaderboardEntry? GetPlayerBest(string game, ulong steamId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT game, steam_id, player_name, score, lines, level, updated_utc
            FROM leaderboard
            WHERE game = @game AND steam_id = @steam_id;
            """;
        command.Parameters.AddWithValue("@game", game);
        command.Parameters.AddWithValue(
            "@steam_id",
            steamId.ToString(CultureInfo.InvariantCulture));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public IReadOnlyList<LeaderboardEntry> GetTop(string game, int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT game, steam_id, player_name, score, lines, level, updated_utc
            FROM leaderboard
            WHERE game = @game
            ORDER BY score DESC, lines DESC, updated_utc ASC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@game", game);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        var entries = new List<LeaderboardEntry>();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public void Dispose() => _connection.Dispose();

    private static LeaderboardEntry ReadEntry(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            ulong.Parse(reader.GetString(1), NumberStyles.None, CultureInfo.InvariantCulture),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            DateTimeOffset.ParseExact(
                    reader.GetString(6),
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)
                .ToUniversalTime());
}
