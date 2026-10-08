using CaorenCup.QOL.PlaySound;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AudioProbeGroupPolicyTests
{
    [Theory]
    [InlineData("join", true)]
    [InlineData("leave", true)]
    [InlineData("group", false)]
    [InlineData("start", false)]
    [InlineData("replace", false)]
    [InlineData("pause", false)]
    [InlineData("resume", false)]
    [InlineData("stop", false)]
    [InlineData("gain", false)]
    [InlineData("resync", false)]
    public void Player_participation_route_does_not_accept_admin_controls(string operation, bool allowed) =>
        Assert.Equal(allowed, AudioProbeGroupPolicy.IsSelfParticipationOperation(operation));
    [Fact]
    public void Only_explicit_members_are_included_and_duplicates_do_not_multiply_sound()
    {
        var group = new AudioProbeGroupEnrollment();
        Assert.False(group.Join(2));
        group.Open(1);
        Assert.True(group.Contains(1));
        Assert.False(group.Contains(2));
        Assert.True(group.Join(2));
        Assert.False(group.Join(2));
        Assert.False(group.Join(0));
        Assert.Equal(2, group.Members.Count);
    }
    [Fact]
    public void Leave_revokes_rejoin_and_close_revokes_all_session_members()
    {
        var group = new AudioProbeGroupEnrollment();
        group.Open(1); group.Join(2);
        Assert.False(group.Leave(1));
        Assert.True(group.Leave(2));
        Assert.False(group.Contains(2));
        group.Join(3); // 连接状态不参与名单，故断线仍有资格，close 后则无资格。
        Assert.True(group.Contains(3));
        group.Close(); group.Open(4);
        Assert.False(group.Contains(1));
        Assert.False(group.Contains(3));
        Assert.Single(group.Members);
    }
    [Theory]
    [InlineData(13f, false, false, 50, 13f)]
    [InlineData(43f, true, false, 1, 13f)]
    [InlineData(60f, true, false, 100, 0f)]
    public void Late_entry_and_unmute_use_current_phase(float elapsed, bool loop, bool paused, int volume, float expected) =>
        Assert.Equal(expected, AudioProbeGroupPolicy.StartPosition(elapsed, loop, paused, volume));
    [Theory]
    [InlineData(13f, false, true, 100)]
    [InlineData(13f, false, false, 0)]
    [InlineData(30f, false, false, 100)]
    [InlineData(31f, false, false, 100)]
    [InlineData(-1f, true, false, 100)]
    [InlineData(float.NaN, true, false, 100)]
    public void Paused_muted_finished_or_invalid_entry_does_not_start(float elapsed, bool loop, bool paused, int volume) =>
        Assert.Null(AudioProbeGroupPolicy.StartPosition(elapsed, loop, paused, volume));
}
