using System.Globalization;
using System.Text;
using CaorenCup.Contracts;

namespace CaorenCup.Features.InGameMenu;

public sealed class PlayerStatsMatchSession
{
    private PlayerStatsSessionInfo? _current;
    private bool _ignoreLiveUntilLifecycleLeavesLive;

    public PlayerStatsTracker Tracker { get; } = new();

    public bool IsActive => _current is not null;

    public PlayerStatsSessionInfo? Current => _current;

    public PlayerStatsMatchExport? Observe(
        ActiveGameModeState state,
        string? mapName,
        DateTimeOffset now)
    {
        if (state.Lifecycle == GameModeLifecycleState.Live)
        {
            if (_ignoreLiveUntilLifecycleLeavesLive) return null;

            var normalizedMode = string.IsNullOrWhiteSpace(state.ModeId) ? "unknown" : state.ModeId.Trim();
            var normalizedMap = string.IsNullOrWhiteSpace(mapName) ? "unknown" : mapName.Trim();
            if (_current is null)
            {
                Start(normalizedMode, normalizedMap, now);
                return null;
            }

            if (!string.Equals(_current.ModeId, normalizedMode, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_current.MapName, normalizedMap, StringComparison.OrdinalIgnoreCase))
            {
                var previous = End("mode-change", now);
                Start(normalizedMode, normalizedMap, now);
                return previous;
            }

            return null;
        }

        _ignoreLiveUntilLifecycleLeavesLive = false;
        return End(ToEndReason(state.Lifecycle), now);
    }

    public void SuppressLiveUntilLifecycleLeavesLive() => _ignoreLiveUntilLifecycleLeavesLive = true;

    public PlayerStatsMatchExport? End(string reason, DateTimeOffset endedAt)
    {
        if (_current is null) return null;

        var export = new PlayerStatsMatchExport(
            _current.ModeId,
            _current.MapName,
            _current.StartedAt,
            endedAt,
            string.IsNullOrWhiteSpace(reason) ? "stopped" : reason,
            Tracker.Snapshot().ToArray());
        Tracker.Reset();
        _current = null;
        return export;
    }

    private void Start(string modeId, string mapName, DateTimeOffset startedAt)
    {
        Tracker.Reset();
        _current = new PlayerStatsSessionInfo(modeId, mapName, startedAt);
    }

    private static string ToEndReason(GameModeLifecycleState lifecycle) => lifecycle switch
    {
        GameModeLifecycleState.Stopped => "stopped",
        GameModeLifecycleState.Error => "error",
        GameModeLifecycleState.Lobby => "lobby",
        GameModeLifecycleState.Warmup => "warmup",
        _ => "ended",
    };
}

public sealed record PlayerStatsSessionInfo(
    string ModeId,
    string MapName,
    DateTimeOffset StartedAt);

public sealed record PlayerStatsMatchExport(
    string ModeId,
    string MapName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string EndReason,
    IReadOnlyList<PlayerStatsRow> Rows);

public static class PlayerStatsCsvExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static string Export(PlayerStatsMatchExport export, string directory)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("CSV 目录不能为空。", nameof(directory));

        Directory.CreateDirectory(directory);
        var timestamp = export.StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"player-stats_{timestamp}_{SafeSegment(export.ModeId)}_{SafeSegment(export.MapName)}.csv";
        var path = Path.Combine(directory, fileName);
        var lines = new List<string>
        {
            "SteamId,PlayerName,Team,RoundsParticipated,TotalDamage,AverageDamagePerRound,UtilityDamage,Shots,Hits,HitPercentage",
        };

        foreach (var row in export.Rows)
        {
            lines.Add(string.Join(',',
                Csv(row.SteamId),
                Csv(row.Name),
                row.Team.ToString(CultureInfo.InvariantCulture),
                row.RoundsParticipated.ToString(CultureInfo.InvariantCulture),
                row.TotalDamage.ToString(CultureInfo.InvariantCulture),
                row.AverageDamagePerRound?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
                row.UtilityDamage.ToString(CultureInfo.InvariantCulture),
                row.Shots.ToString(CultureInfo.InvariantCulture),
                row.Hits.ToString(CultureInfo.InvariantCulture),
                row.HitPercentage?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty));
        }

        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", Utf8Bom);
        return Path.GetFullPath(path);
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string SafeSegment(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var chars = source.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray();
        return new string(chars).Trim('_') is { Length: > 0 } result ? result : "unknown";
    }
}
