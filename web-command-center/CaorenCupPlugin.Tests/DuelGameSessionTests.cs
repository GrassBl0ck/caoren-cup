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

    [Fact]
    public void Paused_session_does_not_count_round_end()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);
        session.MarkRoundStarted();
        session.Pause("管理员暂停");

        var result = session.RecordRoundEnd(DuelTeam.Terrorist);

        Assert.False(result.Counted);
        Assert.Equal(0, session.CompletedRounds);
        Assert.Equal(0, session.ScoreT);
        Assert.Equal(0, session.ScoreCt);
    }

    [Fact]
    public void Finished_session_does_not_count_another_round_end()
    {
        var session = new DuelGameSession(new DuelGameConfig(0, 30, 0, 1, "none"));
        session.TryStart([T(), Ct()], false, out _);
        for (var round = 0; round < session.Config.TotalRounds; round++)
        {
            session.MarkRoundStarted();
            session.RecordRoundEnd(DuelTeam.Terrorist);
        }

        var result = session.RecordRoundEnd(DuelTeam.CounterTerrorist);

        Assert.False(result.Counted);
        Assert.Equal(DuelLifecycle.Finished, session.Lifecycle);
        Assert.Equal(30, session.CompletedRounds);
        Assert.Equal(30, session.ScoreT);
        Assert.Equal(0, session.ScoreCt);
    }

    [Fact]
    public void Clear_is_idempotent_and_returns_session_to_none_idle()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);
        session.MarkRoundStarted();
        session.RecordRoundEnd(DuelTeam.Terrorist);

        session.Clear();
        session.Clear();

        Assert.Equal(DuelControlMode.None, session.ControlMode);
        Assert.Equal(DuelLifecycle.Idle, session.Lifecycle);
        Assert.Empty(session.Participants);
        Assert.Equal(0, session.CompletedRounds);
        Assert.Equal(0, session.ScoreT);
        Assert.Equal(0, session.ScoreCt);
    }

    [Theory]
    [InlineData(8, 16, 5, 1, "none")]
    [InlineData(8, 16, 12, 0.2, "none")]
    [InlineData(8, 16, 12, 1, "unknown")]
    public void Update_config_rejects_invalid_values(int pistol, int rifle, int sniper, double roundTimeMinutes, string utilityMode)
    {
        var session = new DuelGameSession();

        Assert.False(session.TryUpdateConfig(new DuelGameConfig(pistol, rifle, sniper, roundTimeMinutes, utilityMode), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Update_config_accepts_valid_boundary_values()
    {
        var session = new DuelGameSession();

        Assert.True(session.TryUpdateConfig(new DuelGameConfig(0, 30, 0, 0.25, "random3"), out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(new DuelGameConfig(0, 30, 0, 0.25, "random3"), session.Config);
    }

    [Fact]
    public void Update_config_is_rejected_while_game_is_running()
    {
        var session = new DuelGameSession();
        session.TryStart([T(), Ct()], false, out _);

        Assert.False(session.TryUpdateConfig(new DuelGameConfig(0, 30, 0, 0.25, "random3"), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Reset_config_restores_defaults()
    {
        var session = new DuelGameSession();
        session.TryUpdateConfig(new DuelGameConfig(0, 30, 0, 0.25, "random3"), out _);

        session.ResetConfig();

        Assert.Equal(new DuelGameConfig(), session.Config);
    }

    [Fact]
    public void Update_config_rejects_non_numeric_round_time()
    {
        var session = new DuelGameSession();

        Assert.False(session.TryUpdateConfig(new DuelGameConfig(8, 16, 12, double.NaN, "none"), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
