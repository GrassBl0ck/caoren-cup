using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaorenCupPlugin.MatchRuntime;

public sealed record MatchStateIdentity(string MatchId, int Revision, long RuntimeEpoch);

public sealed record RoundRestorePoint(
    int RoundNumber,
    string RoundKey,
    string ValveBackupFile,
    MatchRuntimeSnapshot MatchSnapshot,
    string AbilityCheckpointId,
    string StatsCheckpointId,
    IReadOnlyList<SubstitutionRecord> Substitutions,
    PauseSnapshot PauseSnapshot,
    IReadOnlyList<string> DemoSegments,
    IReadOnlyDictionary<string, BridgePlayerStats>? BridgeStats = null);

public sealed record MatchStateFile(
    int SchemaVersion,
    DateTimeOffset SavedAt,
    string TeamAName,
    string TeamBName,
    MatchRuntimeSnapshot Snapshot,
    IReadOnlyList<MatchEvent> Events,
    IReadOnlyList<RoundRestorePoint> RestorePoints);

public sealed class MatchStateStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private readonly string _path;
    private readonly string _previousPath;

    public MatchStateStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _previousPath = path + ".previous";
    }

    public async Task SaveAsync(MatchStateFile state)
    {
        Validate(state);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(_path)) File.Copy(_path, _previousPath, overwrite: true);
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task<MatchStateFile?> LoadAsync(MatchStateIdentity expected)
    {
        var primary = await TryLoadAsync(_path, expected);
        if (primary is not null) return primary;
        return await TryLoadAsync(_previousPath, expected);
    }

    private static async Task<MatchStateFile?> TryLoadAsync(string path, MatchStateIdentity expected)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<MatchStateFile>(json, JsonOptions);
            if (state is null) return null;
            Validate(state);
            return Matches(state.Snapshot, expected) ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool Matches(MatchRuntimeSnapshot snapshot, MatchStateIdentity expected) =>
        string.Equals(snapshot.MatchId, expected.MatchId, StringComparison.Ordinal)
        && snapshot.Revision == expected.Revision
        && snapshot.RuntimeEpoch == expected.RuntimeEpoch;

    private static void Validate(MatchStateFile state)
    {
        if (state.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported match state schema: {state.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(state.Snapshot.MatchId)
            || state.Snapshot.Revision <= 0
            || state.Snapshot.RuntimeEpoch <= 0)
            throw new InvalidDataException("Match state identity is invalid.");

        long previousSequence = 0;
        foreach (var matchEvent in state.Events)
        {
            if (matchEvent.Sequence <= previousSequence)
                throw new InvalidDataException("Match event sequence must be strictly increasing.");
            if (!string.Equals(matchEvent.MatchId, state.Snapshot.MatchId, StringComparison.Ordinal)
                || matchEvent.Revision != state.Snapshot.Revision
                || matchEvent.RuntimeEpoch != state.Snapshot.RuntimeEpoch)
                throw new InvalidDataException("Match event identity does not match the snapshot.");
            previousSequence = matchEvent.Sequence;
        }

        if (state.RestorePoints.Any(point => point.RoundNumber <= 0
            || string.IsNullOrWhiteSpace(point.RoundKey)
            || string.IsNullOrWhiteSpace(point.ValveBackupFile)))
            throw new InvalidDataException("Round restore point is invalid.");
    }
}
