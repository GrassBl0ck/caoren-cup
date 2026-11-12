namespace CaorenCupPlugin.MatchRuntime;

internal enum RoundTransition
{
    None,
    RegulationHalftime,
    EnterOvertime,
    OvertimeHalftime,
    NextOvertime,
    Completed,
}

internal sealed record RoundSettlement(
    bool Ok,
    string Code,
    RoundTransition Transition = RoundTransition.None,
    string Message = "");

internal sealed class RoundCoordinator
{
    private readonly HashSet<string> _completedRoundKeys = new(StringComparer.Ordinal);
    private int _roundSequence;

    public int ScoreA { get; private set; }

    public int ScoreB { get; private set; }

    public int CompletedRounds { get; private set; }

    public int OvertimeIndex { get; private set; }

    public int OvertimeRoundInStage { get; private set; }

    public string? CurrentRoundKey { get; private set; }

    public string OpenRound(string matchId, long runtimeEpoch)
    {
        if (CurrentRoundKey is not null) return CurrentRoundKey;
        _roundSequence++;
        CurrentRoundKey = $"{matchId}:{runtimeEpoch}:round-{_roundSequence}";
        return CurrentRoundKey;
    }

    public RoundSettlement CompleteRound(string roundKey, RosterSide winner)
    {
        if (_completedRoundKeys.Contains(roundKey))
            return new(false, "DUPLICATE_ROUND", Message: "该正式回合已经结算。");
        if (!string.Equals(CurrentRoundKey, roundKey, StringComparison.Ordinal))
            return new(false, "ROUND_NOT_OPEN", Message: "回合标识与当前正式回合不一致。");

        _completedRoundKeys.Add(roundKey);
        CurrentRoundKey = null;
        CompletedRounds++;
        if (winner == RosterSide.A) ScoreA++;
        else ScoreB++;

        if (OvertimeIndex == 0)
        {
            if (ScoreA >= 13 || ScoreB >= 13)
                return new(true, "OK", RoundTransition.Completed);
            if (CompletedRounds == 12)
                return new(true, "OK", RoundTransition.RegulationHalftime);
            if (CompletedRounds == 24 && ScoreA == 12 && ScoreB == 12)
            {
                OvertimeIndex = 1;
                OvertimeRoundInStage = 0;
                return new(true, "OK", RoundTransition.EnterOvertime);
            }
            return new(true, "OK");
        }

        OvertimeRoundInStage++;
        var overtimeWinTarget = checked(13 + OvertimeIndex * 3);
        if (ScoreA >= overtimeWinTarget || ScoreB >= overtimeWinTarget)
            return new(true, "OK", RoundTransition.Completed);
        if (OvertimeRoundInStage == 3)
            return new(true, "OK", RoundTransition.OvertimeHalftime);
        if (OvertimeRoundInStage < 6)
            return new(true, "OK");
        if (ScoreA != ScoreB)
            return new(true, "OK", RoundTransition.Completed);

        OvertimeIndex++;
        OvertimeRoundInStage = 0;
        return new(true, "OK", RoundTransition.NextOvertime);
    }

    public void CancelOpenRound() => CurrentRoundKey = null;

    public void Restore(MatchRuntimeSnapshot snapshot)
    {
        ScoreA = Math.Max(0, snapshot.ScoreA);
        ScoreB = Math.Max(0, snapshot.ScoreB);
        CompletedRounds = Math.Max(0, snapshot.CompletedRounds);
        OvertimeIndex = Math.Max(0, snapshot.OvertimeIndex);
        OvertimeRoundInStage = Math.Max(0, snapshot.OvertimeRoundInStage);
        CurrentRoundKey = null;
        _roundSequence = CompletedRounds;
        _completedRoundKeys.Clear();
    }
}
