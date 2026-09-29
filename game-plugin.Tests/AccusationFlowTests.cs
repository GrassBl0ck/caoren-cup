using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AccusationFlowTests
{
    private static AccusationFlow Create() => new("match", new[] {
        new AccusationPlayer("a", "A", "A", "Player"), new AccusationPlayer("b", "B", "A", "Player"),
        new AccusationPlayer("c", "C", "B", "Player"), new AccusationPlayer("d", "D", "B", "Player"),
        new AccusationPlayer("admin", "Admin", "A", "Admin") }, true, true);
    [Fact]
    public void Candidates_and_locked_votes_match_web_rules()
    {
        var flow = Create();
        Assert.Single(flow.Candidates("a", AccusationSide.Own));
        Assert.Equal(2, flow.Candidates("a", AccusationSide.Enemy).Count);
        Assert.Equal(AccusationStatus.Accepted, flow.Submit("match", "a", "b", AccusationSide.Own));
        Assert.Equal(AccusationStatus.AlreadyLocked, flow.Submit("match", "a", "b", AccusationSide.Own));
        Assert.Equal(AccusationStatus.WrongTeam, flow.Submit("match", "a", "c", AccusationSide.Own));
        Assert.Equal(AccusationStatus.InvalidActor, flow.Submit("match", "admin", "c", AccusationSide.Enemy));
        Assert.Equal(AccusationStatus.WrongSession, flow.Submit("old", "a", "c", AccusationSide.Enemy));
        Assert.Equal(AccusationStatus.InvalidSide, flow.Submit("match", "a", "c", (AccusationSide)9));
        var ownPage = AccusationMenuModel.Build(flow, "a", AccusationSide.Own, 99);
        Assert.True(ownPage.Submitted);
        Assert.Equal("B", ownPage.OwnTargetName);
        Assert.Null(ownPage.EnemyTargetName);
        Assert.Equal(0, ownPage.Page);
        Assert.True(ownPage.Progress.Single(p => p.Name == "A").OwnSubmitted);
    }
    [Fact]
    public void Both_tickets_per_online_player_are_required()
    {
        var flow = Create(); Assert.False(flow.OnlinePlayersCompleted);
        foreach (var actor in new[] { "a", "b", "c", "d" })
        {
            flow.Submit("match", actor, flow.Candidates(actor, AccusationSide.Own)[0].PlayerId, AccusationSide.Own);
            flow.Submit("match", actor, flow.Candidates(actor, AccusationSide.Enemy)[0].PlayerId, AccusationSide.Enemy);
        }
        Assert.True(flow.OnlinePlayersCompleted);
    }
    [Fact]
    public void Authoritative_choices_preserve_submission_lock_after_refresh()
    {
        var roster = new[] { new AccusationPlayer("a", "A", "A", "Player"),
            new AccusationPlayer("b", "B", "A", "Player"), new AccusationPlayer("c", "C", "B", "Player") };
        var flow = new AccusationFlow("match", roster, true, true,
            new Dictionary<string, AccusationChoice> { ["a"] = new("b", "c") });
        Assert.Equal(AccusationStatus.AlreadyLocked, flow.Submit("match", "a", "b", AccusationSide.Own));
        var view = AccusationMenuModel.Build(flow, "a", AccusationSide.Enemy, 0);
        Assert.True(view.Submitted);
        Assert.Equal("C", view.EnemyTargetName);
    }
}
