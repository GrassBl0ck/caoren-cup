using CaorenCupPlugin;
using Xunit;

namespace CaorenCupPlugin.Tests;

public sealed class DuelGameSessionTests
{
    private static DuelParticipant T(string id = "t1") => new(id, "T玩家", DuelTeam.Terrorist);
    private static DuelParticipant Ct(string id = "ct1") => new(id, "CT玩家", DuelTeam.CounterTerrorist);

    [Fact]
    public void Start_requires_both_teams()
    {
        var session = new DuelGameSession();
        Assert.False(session.TryStart([T()], false, out var error));
        Assert.Equal("T 和 CT 双方都必须至少有一名真人玩家。", error);
    }

    [Fact]
    public void Web_mode_requires_explicit_takeover()
    {
        var session = new DuelGameSession();
        session.EnterWebManaged(new DuelGameConfig(8, 16, 12, 1, "none"));
        Assert.False(session.TryStart([T(), Ct()], false, out _));
        Assert.True(session.TryStart([T(), Ct()], true, out _));
        Assert.Equal(DuelControlMode.GameManaged, session.ControlMode);
    }

    [Fact]
    public void Round_boundaries_skip_zero_length_stage_and_finish_at_total()
    {
        var session = new DuelGameSession(new DuelGameConfig(0, 30, 0, 1, "none"));
        Assert.True(session.TryStart([T(), Ct()], false, out _));
        Assert.Equal(DuelGameStage.Rifle, session.CurrentStage);
        for (var i = 0; i < 29; i++)
        {
            session.MarkRoundStarted();
            Assert.False(session.RecordRoundEnd(DuelTeam.Terrorist).Finished);
        }

        session.MarkRoundStarted();
        var result = session.RecordRoundEnd(DuelTeam.CounterTerrorist);
        Assert.True(result.Finished);
        Assert.Equal(29, result.ScoreT);
        Assert.Equal(1, result.ScoreCt);
    }

    [Fact]
    public void Disconnect_pauses_and_reconnect_does_not_auto_resume()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);
        Assert.True(session.UpdateConnectedPlayers(new HashSet<string> { "ct1" }));
        Assert.Equal(DuelLifecycle.Paused, session.Lifecycle);
        session.UpdateConnectedPlayers(new HashSet<string> { "t1", "ct1" });
        Assert.Equal(DuelLifecycle.Paused, session.Lifecycle);
        Assert.True(session.TryResume(out _));
        Assert.Equal(DuelLifecycle.Running, session.Lifecycle);
    }

    [Fact]
    public void Resume_only_requires_one_online_participant_per_side()
    {
        var session = new DuelGameSession();
        session.TryStart([T("t1"), T("t2"), Ct("ct1"), Ct("ct2")], false, out _);
        session.UpdateConnectedPlayers(new HashSet<string> { "t1", "ct1" });
        Assert.True(session.TryResume(out _));
    }

    [Fact]
    public void Duplicate_round_end_does_not_double_score()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);
        session.MarkRoundStarted();
        session.RecordRoundEnd(DuelTeam.Terrorist);
        session.RecordRoundEnd(DuelTeam.Terrorist);
        Assert.Equal(1, session.ScoreT);
    }
}
