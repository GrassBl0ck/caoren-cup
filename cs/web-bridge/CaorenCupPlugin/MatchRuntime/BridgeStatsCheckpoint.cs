namespace CaorenCupPlugin.MatchRuntime;

public sealed record BridgePlayerStats(string Name, int Kills, int Deaths, int Assists, int Damage);

public sealed class BridgeStatsCheckpoint
{
    private readonly Dictionary<string, BridgePlayerStats> _players;

    private BridgeStatsCheckpoint(int roundNumber, long runtimeEpoch, Dictionary<string, BridgePlayerStats> players)
    {
        RoundNumber = roundNumber;
        RuntimeEpoch = runtimeEpoch;
        _players = players;
    }

    public int RoundNumber { get; }

    public long RuntimeEpoch { get; }

    public static BridgeStatsCheckpoint Capture(
        int roundNumber,
        long runtimeEpoch,
        IReadOnlyDictionary<string, BridgePlayerStats> players) => new(
            roundNumber,
            runtimeEpoch,
            players.ToDictionary(pair => pair.Key, pair => pair.Value with { }, StringComparer.Ordinal));

    public void RestoreTo(IDictionary<string, BridgePlayerStats> target)
    {
        target.Clear();
        foreach (var pair in _players) target[pair.Key] = pair.Value with { };
    }
}
