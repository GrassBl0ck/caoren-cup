namespace CaorenCupPlugin.MatchRuntime;

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
    IReadOnlyList<MatchSeat> Seats);

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
    string? RoundKey = null);

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
