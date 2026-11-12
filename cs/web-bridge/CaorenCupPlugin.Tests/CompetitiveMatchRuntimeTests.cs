using CaorenCupPlugin.MatchRuntime;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class CompetitiveMatchRuntimeTests
{
    [Fact]
    public void Load_and_live_confirmation_follow_the_pregame_state_machine()
    {
        var runtime = CreateLoadedRuntime();

        Assert.Equal(MatchStage.Staging, runtime.Snapshot.Stage);
        Assert.True(runtime.BeginWarmup().Ok);
        Assert.Equal(MatchStage.Warmup, runtime.Snapshot.Stage);
        Assert.True(runtime.BeginReadyCheck().Ok);
        Assert.Equal(MatchStage.ReadyCheck, runtime.Snapshot.Stage);
        Assert.True(runtime.BeginGoingLive().Ok);
        Assert.Equal(MatchStage.GoingLive, runtime.Snapshot.Stage);
        Assert.True(runtime.ConfirmLive().Ok);
        Assert.Equal(MatchStage.FirstHalf, runtime.Snapshot.Stage);
        Assert.Equal(EngineSide.CounterTerrorist, runtime.Snapshot.TeamAEngineSide);
    }

    [Fact]
    public void Twelfth_regulation_round_requires_halftime_side_swap()
    {
        var runtime = CreateLiveRuntime();

        for (var round = 0; round < 12; round++) PlayRound(runtime, round % 2 == 0 ? RosterSide.A : RosterSide.B);

        Assert.Equal(MatchStage.Halftime, runtime.Snapshot.Stage);
        Assert.True(runtime.Snapshot.SideSwapPending);
        Assert.False(runtime.OpenRound().Ok);
        Assert.True(runtime.ConfirmSideSwap().Ok);
        Assert.Equal(MatchStage.SecondHalf, runtime.Snapshot.Stage);
        Assert.Equal(EngineSide.Terrorist, runtime.Snapshot.TeamAEngineSide);
    }

    [Fact]
    public void Regulation_completes_as_soon_as_one_roster_reaches_thirteen()
    {
        var runtime = CreateLiveRuntime();

        for (var round = 0; round < 12; round++) PlayRound(runtime, RosterSide.A);
        runtime.ConfirmSideSwap();
        var result = PlayRound(runtime, RosterSide.A);

        Assert.True(result.MatchFinished);
        Assert.Equal(MatchStage.Completed, runtime.Snapshot.Stage);
        Assert.Equal(13, runtime.Snapshot.ScoreA);
        Assert.Equal(0, runtime.Snapshot.ScoreB);
    }

    [Fact]
    public void Twelve_all_enters_mr3_and_tied_overtime_starts_another_stage()
    {
        var runtime = CreateRegulationTie();

        Assert.Equal(MatchStage.Overtime, runtime.Snapshot.Stage);
        Assert.Equal(1, runtime.Snapshot.OvertimeIndex);

        PlayRound(runtime, RosterSide.A);
        PlayRound(runtime, RosterSide.B);
        PlayRound(runtime, RosterSide.A);
        Assert.Equal(MatchStage.OvertimeHalftime, runtime.Snapshot.Stage);
        runtime.ConfirmSideSwap();
        PlayRound(runtime, RosterSide.B);
        PlayRound(runtime, RosterSide.A);
        PlayRound(runtime, RosterSide.B);

        Assert.Equal(15, runtime.Snapshot.ScoreA);
        Assert.Equal(15, runtime.Snapshot.ScoreB);
        Assert.Equal(2, runtime.Snapshot.OvertimeIndex);
        Assert.Equal(MatchStage.OvertimeHalftime, runtime.Snapshot.Stage);
        Assert.True(runtime.Snapshot.SideSwapPending);
        runtime.ConfirmSideSwap();
        Assert.Equal(MatchStage.Overtime, runtime.Snapshot.Stage);
    }

    [Fact]
    public void Overtime_winner_is_decided_after_a_complete_six_round_stage()
    {
        var runtime = CreateRegulationTie();

        PlayRound(runtime, RosterSide.A);
        PlayRound(runtime, RosterSide.A);
        PlayRound(runtime, RosterSide.B);
        runtime.ConfirmSideSwap();
        PlayRound(runtime, RosterSide.A);
        Assert.Equal(MatchStage.Overtime, runtime.Snapshot.Stage);
        PlayRound(runtime, RosterSide.B);
        var result = PlayRound(runtime, RosterSide.A);

        Assert.True(result.MatchFinished);
        Assert.Equal(MatchStage.Completed, runtime.Snapshot.Stage);
        Assert.Equal(16, runtime.Snapshot.ScoreA);
        Assert.Equal(14, runtime.Snapshot.ScoreB);
    }

    [Fact]
    public void Overtime_clinches_as_soon_as_the_stage_win_target_is_reached()
    {
        var runtime = CreateRegulationTie();

        PlayRound(runtime, RosterSide.A);
        PlayRound(runtime, RosterSide.A);
        PlayRound(runtime, RosterSide.A);
        runtime.ConfirmSideSwap();
        var result = PlayRound(runtime, RosterSide.A);

        Assert.True(result.MatchFinished);
        Assert.Equal(MatchStage.Completed, runtime.Snapshot.Stage);
        Assert.Equal(16, runtime.Snapshot.ScoreA);
        Assert.Equal(12, runtime.Snapshot.ScoreB);
    }

    [Fact]
    public void Duplicate_round_completion_does_not_score_twice()
    {
        var runtime = CreateLiveRuntime();
        var opened = runtime.OpenRound();

        var first = runtime.CompleteRound(opened.RoundKey!, RosterSide.A);
        var duplicate = runtime.CompleteRound(opened.RoundKey!, RosterSide.A);

        Assert.True(first.Ok);
        Assert.False(duplicate.Ok);
        Assert.Equal("DUPLICATE_ROUND", duplicate.Code);
        Assert.Equal(1, runtime.Snapshot.ScoreA);
        Assert.Equal(0, runtime.Snapshot.ScoreB);
    }

    [Fact]
    public void Round_keys_are_stable_unique_and_bound_to_the_runtime_epoch()
    {
        var runtime = CreateLiveRuntime();

        var first = runtime.OpenRound();
        runtime.CompleteRound(first.RoundKey!, RosterSide.A);
        var second = runtime.OpenRound();

        Assert.Equal("match-1:1:round-1", first.RoundKey);
        Assert.Equal("match-1:1:round-2", second.RoundKey);
        Assert.NotEqual(first.RoundKey, second.RoundKey);
    }

    [Fact]
    public void Invalid_transitions_fail_without_mutating_state()
    {
        var runtime = CreateLoadedRuntime();

        var result = runtime.ConfirmLive();

        Assert.False(result.Ok);
        Assert.Equal("INVALID_STAGE", result.Code);
        Assert.Equal(MatchStage.Staging, runtime.Snapshot.Stage);
        Assert.Equal(0, runtime.Snapshot.LastEventSequence);
    }

    [Fact]
    public void Events_are_monotonic_and_carry_match_identity()
    {
        var runtime = CreateLiveRuntime();
        var opened = runtime.OpenRound();
        runtime.CompleteRound(opened.RoundKey!, RosterSide.A);

        Assert.NotEmpty(runtime.Events);
        Assert.Equal(Enumerable.Range(1, runtime.Events.Count).Select(value => (long)value), runtime.Events.Select(item => item.Sequence));
        Assert.All(runtime.Events, item =>
        {
            Assert.Equal("match-1", item.MatchId);
            Assert.Equal(7, item.Revision);
            Assert.Equal(1, item.RuntimeEpoch);
        });
        Assert.Contains(runtime.Events, item => item.Type == "match_started");
        Assert.Contains(runtime.Events, item => item.Type == "round_completed" && item.RoundKey == opened.RoundKey);
    }

    [Fact]
    public void Abort_is_idempotent_and_closes_an_open_round_without_scoring()
    {
        var runtime = CreateLiveRuntime();
        runtime.OpenRound();

        var first = runtime.Abort("administrator");
        var second = runtime.Abort("administrator");

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal("ALREADY_ABORTED", second.Code);
        Assert.Equal(MatchStage.Aborted, runtime.Snapshot.Stage);
        Assert.Null(runtime.Snapshot.CurrentRoundKey);
        Assert.Equal(0, runtime.Snapshot.ScoreA);
        Assert.Equal(0, runtime.Snapshot.ScoreB);
    }

    [Fact]
    public void Restore_replaces_match_state_only_with_a_newer_matching_epoch()
    {
        var runtime = CreateLiveRuntime();
        PlayRound(runtime, RosterSide.A);
        var target = runtime.Snapshot with
        {
            RuntimeEpoch = 5,
            Stage = MatchStage.SecondHalf,
            ScoreA = 8,
            ScoreB = 7,
            CompletedRounds = 15,
            TeamAEngineSide = EngineSide.Terrorist,
            CurrentRoundKey = null,
            LastEventSequence = 0,
        };

        var restored = runtime.Restore(target);
        var stale = runtime.Restore(target with { RuntimeEpoch = 4 });
        var next = runtime.OpenRound();

        Assert.True(restored.Ok);
        Assert.False(stale.Ok);
        Assert.Equal("STALE_RESTORE_EPOCH", stale.Code);
        Assert.Equal(8, runtime.Snapshot.ScoreA);
        Assert.Equal(7, runtime.Snapshot.ScoreB);
        Assert.Equal(MatchStage.SecondHalf, runtime.Snapshot.Stage);
        Assert.Equal(EngineSide.Terrorist, runtime.Snapshot.TeamAEngineSide);
        Assert.Equal("match-1:5:round-16", next.RoundKey);
        Assert.Single(runtime.Events);
        Assert.Equal("round_started", runtime.Events[0].Type);
    }

    private static CompetitiveMatchRuntime CreateLoadedRuntime()
    {
        var runtime = new CompetitiveMatchRuntime();
        var seats = Enumerable.Range(1, 5)
            .Select(index => new MatchSeat($"seat-a{index}", RosterSide.A, $"7656119800000000{index}", $"A{index}", index == 1))
            .Concat(Enumerable.Range(1, 5)
                .Select(index => new MatchSeat($"seat-b{index}", RosterSide.B, $"7656119810000000{index}", $"B{index}", index == 1)))
            .ToArray();
        var definition = new MatchDefinition(
            "match-1",
            7,
            "de_mirage",
            "A队",
            "B队",
            EngineSide.CounterTerrorist,
            seats);

        Assert.True(runtime.Load(definition).Ok);
        return runtime;
    }

    private static CompetitiveMatchRuntime CreateLiveRuntime()
    {
        var runtime = CreateLoadedRuntime();
        runtime.BeginWarmup();
        runtime.BeginReadyCheck();
        runtime.BeginGoingLive();
        runtime.ConfirmLive();
        return runtime;
    }

    private static CompetitiveMatchRuntime CreateRegulationTie()
    {
        var runtime = CreateLiveRuntime();
        for (var round = 0; round < 12; round++) PlayRound(runtime, round % 2 == 0 ? RosterSide.A : RosterSide.B);
        runtime.ConfirmSideSwap();
        for (var round = 0; round < 12; round++) PlayRound(runtime, round % 2 == 0 ? RosterSide.A : RosterSide.B);
        return runtime;
    }

    private static MatchRoundResult PlayRound(CompetitiveMatchRuntime runtime, RosterSide winner)
    {
        var opened = runtime.OpenRound();
        Assert.True(opened.Ok, opened.Message);
        var result = runtime.CompleteRound(opened.RoundKey!, winner);
        Assert.True(result.Ok, result.Message);
        return result;
    }
}
