using CaorenCup.FunCommands;
using System.Runtime.CompilerServices;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class FriendsVineTests
{
    [Fact]
    public void Command_feedback_uses_shared_private_chat_instead_of_raw_replies()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(TestSourcePath())!, "../game-plugin/Features/FriendsVineFeature.cs")));
        var start = source.IndexOf("private void OnCommand(", StringComparison.Ordinal);
        var end = source.IndexOf("private static void Reply(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var commandBody = source[start..end];
        Assert.DoesNotContain("ReplyToCommand(", commandBody);
        Assert.DoesNotContain(".PrintToChat(", commandBody);
        Assert.DoesNotContain("[FV]", source);
        Assert.Contains("CaorenCupUtils.PrintToChat(player, message)", source);
        Assert.Contains("CaorenCupUtils.Tag", source);
    }

    private static string TestSourcePath([CallerFilePath] string path = "") => path;

    [Theory]
    [InlineData(0, 0.8f)]
    [InlineData(0.5, 1f)]
    [InlineData(1, 1.2f)]
    [InlineData(1.5, 1f)]
    [InlineData(2, 0.8f)]
    [InlineData(101, 1.2f)]
    public void Full_cycle_returns_to_minimum(double elapsed, float expected)
    {
        Assert.Equal(expected, new FriendsVineCycle(0.8f, 1.2f, 2).MultiplierAt(elapsed), 5);
    }

    [Theory]
    [InlineData("0", "1", "2")]
    [InlineData("-1", "1", "2")]
    [InlineData("2", "1", "2")]
    [InlineData("0.8", "1.2", "0")]
    [InlineData("0.8", "1.2", "-1")]
    [InlineData("NaN", "1.2", "2")]
    [InlineData("0.8", "Infinity", "2")]
    [InlineData("0.8", "1.2", "Infinity")]
    [InlineData("0.8", "1.2", "NaN")]
    [InlineData("0.8", "1.2", "1e999")]
    [InlineData("0,8", "1.2", "2")]
    public void Invalid_parameters_are_rejected(string min, string max, string period) =>
        Assert.False(FriendsVineCycle.TryParse(min, max, period, out _));

    [Fact]
    public void Decimal_parameters_parse_and_equal_bounds_are_allowed()
    {
        Assert.True(FriendsVineCycle.TryParse("0.8", "1.2", "2", out var cycle));
        Assert.Equal(new FriendsVineCycle(0.8f, 1.2f, 2), cycle);
        Assert.True(FriendsVineCycle.TryParse("1", "1", "2", out var fixedCycle));
        Assert.Equal(1, fixedCycle!.MultiplierAt(0.4));
    }

    [Fact]
    public void Applies_to_selected_team_relative_to_original_and_stop_restores_everyone()
    {
        var runtime = new FriendsVineRuntime();
        var t = new FakePawn(1, 1.5f);
        var ct = new FakePawn(2, 1);
        runtime.Start(2, new(0.8f, 1.2f, 2), 10);
        runtime.Update([(t, 2), (ct, 3)], 10);
        Assert.Equal(1.2f, t.Scale, 5);
        Assert.Equal(0, ct.Writes);
        runtime.Update([(t, 2), (ct, 3)], 11);
        Assert.Equal(1.8f, t.Scale, 5);
        runtime.Start(3, new(0.5f, 2, 4), 11);
        runtime.Update([(t, 2), (ct, 3)], 13);
        Assert.Equal(2, ct.Scale);
        runtime.StopAll();
        Assert.False(runtime.IsActive);
        Assert.Equal(1.5f, t.Scale);
        Assert.Equal(1, ct.Scale);
        runtime.Update([(t, 2), (ct, 3)], 14);
        Assert.Equal(1.5f, t.Scale);
        Assert.Equal(1, ct.Scale);
    }

    [Fact]
    public void Repeated_start_keeps_original_baseline()
    {
        var runtime = new FriendsVineRuntime();
        var pawn = new FakePawn(1, 2);
        runtime.Start(2, new(0.5f, 1, 2), 0);
        runtime.Update([(pawn, 2)], 0);
        Assert.Equal(1, pawn.Scale);
        runtime.Start(2, new(0.8f, 1.2f, 2), 0);
        runtime.Update([(pawn, 2)], 0);
        Assert.Equal(1.6f, pawn.Scale, 5);
        runtime.StopAll();
        Assert.Equal(2, pawn.Scale);
    }

    [Fact]
    public void Death_pauses_and_respawn_joins_existing_cycle()
    {
        var runtime = new FriendsVineRuntime();
        var pawn = new FakePawn(1, 1.5f);
        runtime.Start(2, new(0.5f, 2, 2), 0);
        runtime.Update([(pawn, 2)], 0);
        runtime.Update([], 0.5);
        Assert.Equal(1.5f, pawn.Scale);
        Assert.True(runtime.IsActive);
        var writes = pawn.Writes;
        runtime.Update([], 0.75);
        Assert.Equal(writes, pawn.Writes);
        runtime.Update([(pawn, 2)], 1);
        Assert.Equal(3, pawn.Scale);
        runtime.StopAll();
        Assert.Equal(1.5f, pawn.Scale);
    }

    [Fact]
    public void Team_changes_restore_baseline_and_use_current_team_settings()
    {
        var runtime = new FriendsVineRuntime();
        var pawn = new FakePawn(1, 2);
        runtime.Start(2, new(0.5f, 1, 2), 0);
        runtime.Start(3, new(1.5f, 2, 2), 0);
        runtime.Update([(pawn, 2)], 0);
        runtime.Update([(pawn, 3)], 0);
        Assert.Equal(3, pawn.Scale);
        runtime.Update([(pawn, 1)], 1);
        Assert.Equal(2, pawn.Scale);
    }

    [Fact]
    public void Disconnected_entity_is_not_written_and_new_entity_gets_own_baseline()
    {
        var runtime = new FriendsVineRuntime();
        var oldPawn = new FakePawn(1, 1);
        var newPawn = new FakePawn(32769, 2);
        runtime.Start(2, new(0.5f, 1, 2), 0);
        runtime.Update([(oldPawn, 2)], 0);
        oldPawn.IsValid = false;
        var writes = oldPawn.Writes;
        runtime.Update([(newPawn, 2)], 0);
        runtime.StopAll();
        Assert.Equal(writes, oldPawn.Writes);
        Assert.Equal(2, newPawn.Scale);
    }

    [Fact]
    public void Map_cleanup_restores_pawns_but_keeps_effect_for_new_map()
    {
        var runtime = new FriendsVineRuntime();
        var pawn = new FakePawn(1, 2);
        runtime.Start(2, new(0.5f, 2, 2), 0);
        runtime.Update([(pawn, 2)], 0);
        runtime.RestorePawns();
        Assert.Equal(2, pawn.Scale);
        Assert.True(runtime.IsActive);
        runtime.Update([(pawn, 2)], 1);
        Assert.Equal(4, pawn.Scale);
    }

    [Fact]
    public void Overflowing_scale_is_never_written()
    {
        var runtime = new FriendsVineRuntime();
        var pawn = new FakePawn(1, 2);
        runtime.Start(2, new(1, float.MaxValue, 2), 0);
        runtime.Update([(pawn, 2)], 1);
        Assert.Equal(2, pawn.Scale);
    }

    private sealed class FakePawn(uint id, float scale) : IFriendsVinePawn
    {
        public uint Id => id;
        public bool IsValid { get; set; } = true;
        public float Scale { get; private set; } = scale;
        public int Writes { get; private set; }
        public void SetScale(float value)
        {
            Assert.True(IsValid);
            Assert.True(float.IsFinite(value) && value > 0);
            Scale = value;
            Writes++;
        }
    }
}
