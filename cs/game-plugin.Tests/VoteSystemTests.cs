using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class VoteSystemTests
{
    private static VoteSystem Start(params string[] players)
    {
        var vote = new VoteSystem();
        Assert.Equal(VoteStartStatus.Started, vote.TryStart("测试问题", new[] { "第一项", "第二项" }, players, 100));
        return vote;
    }

    [Fact]
    public void All_eligible_voters_finish_early_and_result_has_counts()
    {
        var vote = Start("a", "b", "c");
        var id = vote.Active!.Id;
        Assert.Equal(VoteCastStatus.Accepted, vote.Cast(id, "a", "A", 101));
        Assert.Equal(VoteCastStatus.Accepted, vote.Cast(id, "b", "b", 102));
        Assert.NotNull(vote.Active);
        vote.Cast(id, "c", "A", 103);
        Assert.Null(vote.Active);
        Assert.Equal(VoteOutcome.Winner, vote.LastResult!.Outcome);
        Assert.Equal("A", vote.LastResult.WinnerOptionId);
        Assert.Equal(new[] { 2, 1 }, vote.LastResult.Options.Select(option => option.Votes));
        Assert.Equal(VoteEndReason.AllVoted, vote.LastResult.EndReason);
        Assert.Equal(113, vote.ResultVisibleUntil);
    }

    [Fact]
    public void Eligibility_is_locked_duplicate_votes_and_administrators_are_rejected()
    {
        var vote = Start("a", "b"); var id = vote.Active!.Id;
        Assert.Equal(VoteCastStatus.NotEligible, vote.Cast(id, "late", "A", 101));
        Assert.Equal(VoteCastStatus.Administrator, vote.Cast(id, "a", "A", 101, true));
        Assert.Equal(VoteCastStatus.Accepted, vote.Cast(id, "a", "A", 102));
        Assert.Equal(VoteCastStatus.AlreadyVoted, vote.Cast(id, "a", "B", 103));
        Assert.Equal("A", vote.GetChoice("a"));
        vote.Advance(130);
        Assert.Equal(2, vote.LastResult!.EligiblePlayerCount);
        Assert.Equal(1, vote.LastResult.ParticipatedPlayerCount);
        Assert.Equal("A", vote.LastResult.WinnerOptionId);
    }

    [Fact]
    public void Deadline_cast_is_rejected_and_timeout_does_not_accept_late_vote()
    {
        var vote = Start("a"); var id = vote.Active!.Id;
        Assert.Equal(VoteCastStatus.DeadlinePassed, vote.Cast(id, "a", "A", 130));
        Assert.Equal(VoteOutcome.NoVotes, vote.LastResult!.Outcome);
        Assert.Null(vote.LastResult.WinnerOptionId);
    }

    [Fact]
    public void Tie_has_no_unique_winner()
    {
        var vote = Start("a", "b"); var id = vote.Active!.Id;
        vote.Cast(id, "a", "A", 101); vote.Cast(id, "b", "B", 102);
        Assert.Equal(VoteOutcome.Tied, vote.LastResult!.Outcome);
        Assert.Null(vote.LastResult.WinnerOptionId);
    }

    [Fact]
    public void New_vote_waits_until_result_display_ends_and_stale_ids_cannot_cast()
    {
        var vote = Start("a"); var old = vote.Active!.Id;
        Assert.Equal(VoteStartStatus.AlreadyRunning, vote.TryStart("下一轮", new[] { "x", "y" }, new[] { "a" }, 101));
        vote.Cast(old, "a", "A", 102);
        Assert.Equal(VoteStartStatus.ResultStillDisplayed, vote.TryStart("下一轮", new[] { "x", "y" }, new[] { "a" }, 111.99));
        Assert.Equal(VoteStartStatus.Started, vote.TryStart("下一轮", new[] { "x", "y" }, new[] { "a" }, 112));
        Assert.Equal(VoteCastStatus.StaleVote, vote.Cast(old, "a", "A", 113));
        Assert.Null(vote.GetChoice("a"));
    }

    [Theory]
    [InlineData(VoteEndReason.AdministratorCancelled)]
    [InlineData(VoteEndReason.MapChanged)]
    [InlineData(VoteEndReason.PluginUnloaded)]
    [InlineData(VoteEndReason.Disabled)]
    public void Cancellation_never_produces_executable_winner(VoteEndReason reason)
    {
        var vote = Start("a", "b"); vote.Cast(vote.Active!.Id, "a", "A", 101);
        Assert.True(vote.Cancel(reason, 102));
        Assert.Equal(VoteOutcome.Cancelled, vote.LastResult!.Outcome);
        Assert.Null(vote.LastResult.WinnerOptionId);
        Assert.False(vote.Cancel(reason, 103));
    }

    [Fact]
    public void Reset_clears_vote_result_and_cooldown()
    {
        var vote = Start("a", "b");
        Assert.Equal(VoteOutcome.Cancelled, vote.Reset(VoteEndReason.MapChanged, 105)!.Outcome);
        Assert.Null(vote.Active); Assert.Null(vote.LastResult); Assert.Equal(0, vote.ResultVisibleUntil);
    }

    [Fact]
    public void Invalid_definitions_and_empty_eligibility_are_rejected()
    {
        var vote = new VoteSystem();
        Assert.Equal(VoteStartStatus.InvalidDefinition, vote.TryStart("q", new[] { "same", "SAME" }, new[] { "a" }, 100));
        Assert.Equal(VoteStartStatus.InvalidDefinition, vote.TryStart("q", new[] { "one" }, new[] { "a" }, 100));
        Assert.Equal(VoteStartStatus.NoEligiblePlayers, vote.TryStart("q", new[] { "x", "y" }, Array.Empty<string>(), 100));
    }

    [Fact]
    public void Draft_requires_question_count_and_distinct_options_before_preview()
    {
        var draft = new VoteDraft();
        Assert.True(draft.Accept("问题", out _));
        Assert.False(draft.Accept("5", out _)); Assert.True(draft.Accept("2", out _));
        Assert.True(draft.Accept("选项一", out _)); Assert.False(draft.Accept("选项一", out _));
        Assert.False(draft.Ready); Assert.True(draft.Accept("选项二", out _)); Assert.True(draft.Ready);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void All_supported_option_counts_are_preserved_and_result_is_read_only(int count)
    {
        var vote = new VoteSystem();
        var texts = Enumerable.Range(0, count).Select(i => "选项 " + i).ToArray();
        Assert.Equal(VoteStartStatus.Started, vote.TryStart("q", texts, new[] { "a" }, 100, "future-preset"));
        var lastId = ((char)('A' + count - 1)).ToString();
        vote.Cast(vote.Active!.Id, "a", lastId, 101);
        Assert.Equal(lastId, vote.LastResult!.WinnerOptionId);
        Assert.Equal(count, vote.LastResult.Options.Count);
        Assert.Equal("future-preset", vote.LastResult.PresetKey);
        Assert.Throws<NotSupportedException>(() => ((IList<VoteOptionResult>)vote.LastResult.Options).Clear());
    }

    [Fact]
    public void Invalid_choice_does_not_consume_vote_and_completion_is_not_repeated()
    {
        var vote = Start("a"); var id = vote.Active!.Id;
        Assert.Equal(VoteCastStatus.InvalidOption, vote.Cast(id, "a", "D", 101));
        Assert.Null(vote.GetChoice("a"));
        vote.Cast(id, "a", "B", 102);
        var result = vote.LastResult;
        vote.Advance(200);
        Assert.Same(result, vote.LastResult);
        Assert.Equal(VoteCastStatus.NoActiveVote, vote.Cast(id, "a", "A", 201));
    }
}
