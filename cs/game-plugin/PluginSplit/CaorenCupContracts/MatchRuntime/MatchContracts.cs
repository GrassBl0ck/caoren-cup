using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaorenCup.Contracts.MatchRuntime;

public enum MatchStage
{
    Disabled,
    Staging,
    Warmup,
    ReadyCheck,
    GoingLive,
    FirstHalf,
    Halftime,
    SecondHalf,
    OvertimeHalftime,
    Overtime,
    Recovering,
    Completed,
    Aborted,
}

public enum MatchPauseState
{
    Running,
    TacticalPaused,
    TechnicalPaused,
    RestorePending,
    Restoring,
}

public enum RosterSide { A, B }

public enum EngineSide { Terrorist, CounterTerrorist }

public sealed record MatchSeat(
    string SeatId,
    RosterSide Roster,
    string SteamId,
    string Name,
    bool IsCaptain);

public sealed record MatchDefinition(
    string MatchId,
    int Revision,
    string MapName,
    string TeamAName,
    string TeamBName,
    EngineSide TeamAInitialSide,
    IReadOnlyList<MatchSeat> Seats,
    string ModeId = "competitive",
    int? WarmupSeconds = null);

public sealed record MatchCommandResult(bool Ok, string Code, string Message = "");

public sealed record MatchRoundOpenResult(bool Ok, string Code, string Message = "", string? RoundKey = null);

public sealed record MatchRoundResult(
    bool Ok,
    string Code,
    string Message = "",
    bool MatchFinished = false,
    bool SideSwapRequired = false);

public sealed record MatchEvent(
    long Sequence,
    string Type,
    string MatchId,
    int Revision,
    long RuntimeEpoch,
    string? RoundKey = null,
    int? RoundNumber = null,
    EngineSide? TeamAEngineSide = null,
    EngineSide? WinningEngineSide = null,
    JsonElement? Players = null,
    long? OccurredAtUnixMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MatchSeat? AddedSeat = null);

public sealed record BridgePlayerStats(string Name, int Kills, int Deaths, int Assists, int Damage);

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

public sealed record MatchRuntimeSnapshot(
    string? MatchId,
    int Revision,
    long RuntimeEpoch,
    MatchStage Stage,
    MatchPauseState PauseState,
    int ScoreA,
    int ScoreB,
    int CompletedRounds,
    int OvertimeIndex,
    int OvertimeRoundInStage,
    EngineSide TeamAEngineSide,
    bool SideSwapPending,
    string? CurrentRoundKey,
    long LastEventSequence)
{
    public IReadOnlyList<MatchReadySeat>? ReadySeats { get; init; }
    public long? WarmupEndsAtUnixMs { get; init; }

    public static MatchRuntimeSnapshot Empty { get; } = new(
        null,
        0,
        0,
        MatchStage.Disabled,
        MatchPauseState.Running,
        0,
        0,
        0,
        0,
        0,
        EngineSide.CounterTerrorist,
        false,
        null,
        0);
}
