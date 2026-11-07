using CaorenCup.FunCommands;
using CaorenCup.Features;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class HeadSizeTests
{
    [Theory]
    [InlineData(-20, 0.09090909f)]
    [InlineData(-10, 0.33333333f)]
    [InlineData(-5, 0.6f)]
    [InlineData(0, 1f)]
    [InlineData(5, 1.5f)]
    [InlineData(10, 2f)]
    [InlineData(20, 2.66666667f)]
    public void Curve_matches_agreed_checkpoints(long netKills, float expected) =>
        Assert.Equal(expected, HeadSizeRules.ScaleAt(netKills, 1), 6);

    [Fact]
    public void Curve_is_increasing_and_strength_changes_steepness()
    {
        for (var x = -50; x < 50; x++)
        {
            Assert.True(HeadSizeRules.ScaleAt(x + 1, 1) > HeadSizeRules.ScaleAt(x, 1));
            Assert.Equal(HeadSizeRules.ScaleAt(x * 2, 1), HeadSizeRules.ScaleAt(x, 2));
        }
        Assert.Equal(1, HeadSizeRules.ScaleAt(0, double.MaxValue));
        Assert.Equal(1, HeadSizeRules.ScaleAt(0, double.Epsilon));
        Assert.InRange(HeadSizeRules.ScaleAt(long.MinValue, double.MaxValue), float.Epsilon, 1);
        Assert.InRange(HeadSizeRules.ScaleAt(long.MaxValue, double.MaxValue), 1, 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    [InlineData("1e-999")]
    [InlineData("1,5")]
    [InlineData("no")]
    public void Invalid_strength_is_rejected(string value) =>
        Assert.False(HeadSizeRules.TryParse(["1", value], out _, out _));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("20.00000001")]
    [InlineData("21")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("1e-60")]
    [InlineData("1,5")]
    public void Invalid_manual_scale_is_rejected(string value) =>
        Assert.False(HeadSizeRules.TryParse(["2", "ct", value], out _, out _));

    [Fact]
    public void Parser_handles_defaults_decimal_limits_and_spaced_names()
    {
        Assert.Equal(new HeadSizeCommand(HeadSizeMode.Automatic), Parse("1"));
        Assert.Equal(0.5, Parse("1", "0.5").Strength);
        Assert.Equal(HeadSizeMode.Off, Parse("0").Mode);
        Assert.Equal(20, Parse("2", "CT", "20").Scale);
        Assert.Equal(0.0001f, Parse("2", "T", "0.0001").Scale);
        Assert.Equal("草人 Player", Parse("2", "草人 Player", "1.25").Target);
        Assert.Equal("草人 Player", Parse("2", "草人", "Player", "1.25").Target);
    }

    [Fact]
    public void Parser_rejects_missing_extra_or_blank_arguments()
    {
        string[][] invalid = [[], ["0", "extra"], ["1", "1", "extra"], ["2"],
            ["2", "t"], ["2", " ", "2"], ["3"], ["2", "t", "2", "extra"]];
        foreach (var args in invalid)
        {
            Assert.False(HeadSizeRules.TryParse(args, out _, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void Automatic_uses_existing_and_updated_scoreboard_and_does_not_rewrite_unchanged_values()
    {
        var runtime = new HeadSizeRuntime();
        var pawn = new FakePawn();
        var player = Player(1, "Alice", 2, pawn, 10);
        Assert.True(runtime.Execute(Parse("1"), [player]).Success);
        Assert.Equal(2, pawn.Scale);
        runtime.Refresh([player]);
        Assert.Single(pawn.Writes);
        runtime.Refresh([player with { NetKills = -5 }]);
        Assert.Equal(0.6f, pawn.Scale);
        runtime.Refresh([player with { NetKills = null }]);
        Assert.Equal(0.6f, pawn.Scale); // 计分板暂不可用时不伪造 0 净击杀。
    }

    [Fact]
    public void Manual_preserves_other_rules_and_individual_override_wins_over_team()
    {
        var runtime = new HeadSizeRuntime();
        var alice = Player(1, "Alice", 2, new FakePawn());
        var bob = Player(2, "Bob", 3, new FakePawn());
        HeadSizePlayer[] players = [alice, bob];
        Assert.True(runtime.Execute(Parse("2", "t", "2"), players).Success);
        Assert.True(runtime.Execute(Parse("2", "ali", "4"), players).Success);
        Assert.True(runtime.Execute(Parse("2", "CT", "3"), players).Success);
        Assert.True(runtime.Execute(Parse("2", "t", "0.5"), players).Success);
        Assert.Equal(4, alice.Pawn!.Scale);
        Assert.Equal(3, bob.Pawn!.Scale);
        Assert.Equal(1, runtime.PlayerOverrides);
        Assert.Equal(2, runtime.TeamOverrides);
        runtime.Refresh([alice with { Team = 3 }, bob with { Team = 2 }]);
        Assert.Equal(4, alice.Pawn.Scale);
        Assert.Equal(0.5f, bob.Pawn.Scale);
    }

    [Fact]
    public void Team_rule_follows_new_members_and_leaving_an_unconfigured_team_restores_one()
    {
        var runtime = new HeadSizeRuntime();
        runtime.Execute(Parse("2", "t", "20"), []);
        var player = Player(1, "New", 2, new FakePawn());
        runtime.Refresh([player]);
        Assert.Equal(20, player.Pawn!.Scale);
        runtime.Refresh([player with { Team = 3 }]);
        Assert.Equal(1, player.Pawn.Scale);
        runtime.Refresh([player with { Team = 1 }]);
        Assert.Equal(1, player.Pawn.Scale);
    }

    [Fact]
    public void Ambiguous_or_missing_match_preserves_mode_strength_and_pawns()
    {
        var runtime = new HeadSizeRuntime();
        var alice = Player(1, "Alice", 2, new FakePawn(), 10);
        var alice2 = Player(2, "Alice Two", 3, new FakePawn(), -5);
        HeadSizePlayer[] players = [alice, alice2];
        runtime.Execute(Parse("1", "2"), players);
        var before = players.Select(p => p.Pawn!.Scale).ToArray();
        var reply = runtime.Execute(Parse("2", "ALICE", "10"), players);
        Assert.False(reply.Success);
        Assert.Contains(reply.Messages, line => line.Contains("Alice Two"));
        Assert.False(runtime.Execute(Parse("2", "unknown", "10"), players).Success);
        Assert.Equal(HeadSizeMode.Automatic, runtime.Mode);
        Assert.Equal(2, runtime.Strength);
        Assert.Equal(before, players.Select(p => p.Pawn!.Scale).ToArray());
        Assert.Equal(0, runtime.PlayerOverrides);
    }

    [Fact]
    public void Switching_modes_resets_before_manual_target_and_clears_overrides_before_auto()
    {
        var runtime = new HeadSizeRuntime();
        var pawn = new FakePawn();
        var alice = Player(1, "Alice", 2, pawn, 10);
        var bob = Player(2, "Bob", 3, new FakePawn(), -5);
        HeadSizePlayer[] players = [alice, bob];
        runtime.Execute(Parse("1"), players);
        pawn.Writes.Clear();
        runtime.Execute(Parse("2", "ali", "20"), players);
        Assert.Equal(new float[] { 1, 20 }, pawn.Writes);
        Assert.Equal(1, bob.Pawn!.Scale);
        runtime.Execute(Parse("1"), players);
        Assert.Equal(2, pawn.Scale);
        Assert.Equal(0.6f, bob.Pawn.Scale);
        Assert.Equal(0, runtime.PlayerOverrides);
        runtime.Execute(Parse("2", "ct", "3"), players);
        Assert.Equal(1, pawn.Scale);
        Assert.Equal(0, runtime.PlayerOverrides);
    }

    [Fact]
    public void Respawn_round_and_reconnect_reapply_but_slot_reuse_does_not_copy_personal_setting()
    {
        var runtime = new HeadSizeRuntime();
        var original = Player(765, "Alice", 2, new FakePawn());
        runtime.Execute(Parse("2", "alice", "5"), [original]);
        runtime.Refresh([]); // 断线不清空真人覆盖。
        var reconnected = original with { Pawn = new FakePawn() };
        runtime.Refresh([reconnected]);
        Assert.Equal(5, reconnected.Pawn.Scale);
        var nextRound = reconnected with { Pawn = new FakePawn() };
        runtime.Refresh([nextRound]);
        Assert.Equal(5, nextRound.Pawn.Scale);
        var other = Player(999, "Alice", 2, new FakePawn());
        runtime.Refresh([other]);
        Assert.Equal(1, other.Pawn!.Scale);
        Assert.Equal(HeadSizeIdentity.Create(765, 100), HeadSizeIdentity.Create(765, 200));
        Assert.NotEqual(HeadSizeIdentity.Create(0, 100, true), HeadSizeIdentity.Create(0, 200, true));
    }

    [Fact]
    public void Stop_clears_all_rules_restores_valid_pawns_and_late_refresh_cannot_reactivate()
    {
        var runtime = new HeadSizeRuntime();
        var alice = Player(1, "Alice", 2, new FakePawn());
        runtime.Execute(Parse("2", "t", "2"), [alice]);
        runtime.Execute(Parse("2", "Alice", "4"), [alice]);
        Assert.True(runtime.Stop([alice]).Success);
        Assert.Equal(1, alice.Pawn!.Scale);
        Assert.Equal(HeadSizeMode.Off, runtime.Mode);
        Assert.Equal(0, runtime.TeamOverrides);
        Assert.Equal(0, runtime.PlayerOverrides);
        var subsequent = alice with { Pawn = new FakePawn(1.2f) };
        runtime.Refresh([subsequent]);
        Assert.Equal(1.2f, subsequent.Pawn.Scale); // 关闭状态没有后台写入。
        runtime.Execute(Parse("2", "ct", "2"), [alice]);
        Assert.Equal(1, alice.Pawn.Scale);
        runtime.Execute(Parse("0"), [subsequent]);
        Assert.Equal(1, subsequent.Pawn.Scale);
    }

    [Fact]
    public void Invalid_pawn_is_ignored_and_a_write_failure_restores_others_and_disables()
    {
        var runtime = new HeadSizeRuntime();
        var invalid = new FakePawn { IsValid = false };
        var good = new FakePawn();
        var broken = new FakePawn { ThrowOnWrite = true };
        HeadSizePlayer[] players = [Player(1, "Invalid", 2, invalid), Player(2, "Good", 2, good), Player(3, "Bad", 2, broken)];
        var reply = runtime.Execute(Parse("2", "t", "3"), players);
        Assert.False(reply.Success);
        Assert.NotNull(reply.Update.Error);
        Assert.Empty(invalid.Writes);
        Assert.Equal(1, good.Scale);
        Assert.Equal(HeadSizeMode.Off, runtime.Mode);
    }

    [Fact]
    public void Selected_dead_player_receives_override_on_respawn_and_new_bot_does_not_inherit_it()
    {
        var runtime = new HeadSizeRuntime();
        var bot = new HeadSizePlayer(HeadSizeIdentity.Create(0, 123, true), "Bot Alice", 2, 0, null);
        Assert.True(runtime.Execute(Parse("2", "Bot Alice", "2.5"), [bot]).Success);
        var respawned = bot with { Pawn = new FakePawn() };
        runtime.Refresh([respawned]);
        Assert.Equal(2.5f, respawned.Pawn.Scale);
        var replacement = bot with { Identity = HeadSizeIdentity.Create(0, 456, true), Pawn = new FakePawn() };
        runtime.Refresh([replacement]);
        Assert.Equal(1, replacement.Pawn.Scale);
    }

    [Fact]
    public void Shutdown_continues_restoring_other_pawns_when_one_cannot_be_restored()
    {
        var runtime = new HeadSizeRuntime();
        var bad = new FakePawn();
        var good = new FakePawn();
        HeadSizePlayer[] players = [Player(1, "Bad", 2, bad), Player(2, "Good", 2, good)];
        runtime.Execute(Parse("2", "t", "3"), players);
        bad.ThrowOnWrite = true;
        Assert.False(runtime.Stop(players).Success);
        Assert.Equal(1, good.Scale);
        Assert.Equal(HeadSizeMode.Off, runtime.Mode);
        Assert.Equal(0, runtime.TeamOverrides);
        bad.ThrowOnWrite = false;
        Assert.True(runtime.Execute(Parse("0"), players).Success);
        Assert.Equal(1, bad.Scale);
    }

    [Fact]
    public void Help_is_compact_and_feature_registration_permissions_and_output_are_wired()
    {
        var feature = new HeadSizeFeature();
        var help = feature.GetHelpEntry();
        Assert.Contains("/hd 0", help);
        Assert.Contains("/hd 1", help);
        Assert.Contains("/hd 2", help);
        Assert.Contains("互斥", help);
        Assert.Contains("个人优先", help);
        // Core 将整个 GetHelpEntry 当作单条聊天消息，说明详情另走 /info 的分行输出。
        Assert.InRange(Encoding.UTF8.GetByteCount(CaorenCupUtils.FormatHelpMenuLine(help)), 1, 240);
        Assert.Contains("/reset_plu", feature.GetFeatureDescription());
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../game-plugin"));
        var source = File.ReadAllText(Path.Combine(root, "Features/HeadSizeFeature.cs"));
        var handler = source[source.IndexOf("private void OnCommand(", StringComparison.Ordinal)..source.IndexOf("private void OnTick(", StringComparison.Ordinal)];
        Assert.Contains("AdminManager.PlayerHasPermissions(player, \"@css/root\")", handler);
        Assert.True(handler.IndexOf("PlayerHasPermissions", StringComparison.Ordinal) < handler.IndexOf("_runtime.Execute", StringComparison.Ordinal));
        Assert.DoesNotContain("ReplyToCommand(", handler);
        Assert.Contains("CaorenCup.Contracts.CaorenCupChat.Reply(player, message)", source);
        Assert.Contains("CaorenCupUtils.PrintToChatAll", source);
        Assert.Contains("RegisterListener<Listeners.OnServerPostEntityThink>(OnTick)", source);
        Assert.Contains("new HeadSizeFeature()", File.ReadAllText(Path.Combine(root, "CaorenCupPlugin.cs")));
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void Cold_start_reset_does_not_probe_schema_before_a_player_pawn_exists()
    {
        var path = Path.Combine(Path.GetDirectoryName(SourcePath())!, "../game-plugin/Features/HeadSizeFeature.cs");
        var source = File.ReadAllText(path);
        var reset = source[source.IndexOf("private HeadSizeUpdate ResetAll()", StringComparison.Ordinal)..source.IndexOf("private bool EnsureSchema()", StringComparison.Ordinal)];
        var pawnGate = reset.IndexOf("if (!players.Any(p => p.Pawn is { IsValid: true }))", StringComparison.Ordinal);
        Assert.True(pawnGate >= 0 && pawnGate < reset.IndexOf("EnsureSchema()", StringComparison.Ordinal),
            "冷启动必须先检查实际 pawn，避免在网络序列化元数据就绪前查询并永久缓存 networked=false。");
    }

    private static HeadSizeCommand Parse(params string[] args)
    {
        Assert.True(HeadSizeRules.TryParse(args, out var command, out var error), error);
        return command;
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(8, 9, 8)]
    [InlineData(1, 1, 1)]
    [InlineData(9, 9, 9)]
    public void Native_gate_enables_big_head_and_restores_only_its_original_bit(int initial, int enabled, int restored)
    {
        var gate = new HeadSizeNativeGate();
        var state = new FakeMutators(100, initial);
        gate.Enable(state);
        Assert.Equal(enabled, state.Flags);
        var writes = state.Writes;
        gate.Enable(state);
        Assert.Equal(writes, state.Writes); // 无变化时不反复发网络通知。
        state.Flags |= 16; // 其他系统在运行期间添加的状态必须保留。
        gate.Restore(state);
        Assert.Equal(restored | 16, state.Flags);
        Assert.False(gate.IsActive);
    }

    [Fact]
    public void Native_gate_reenables_after_engine_reset_without_losing_original_state()
    {
        var gate = new HeadSizeNativeGate();
        var state = new FakeMutators(100, 8);
        gate.Enable(state);
        state.Flags = 24;
        gate.Enable(state);
        Assert.Equal(25, state.Flags);
        gate.Restore(state);
        Assert.Equal(24, state.Flags);
    }

    [Fact]
    public void Native_gate_does_not_restore_old_state_into_a_replaced_map_entity()
    {
        var gate = new HeadSizeNativeGate();
        gate.Enable(new FakeMutators(100, 0));
        var replacement = new FakeMutators(200, 9);
        gate.Restore(replacement);
        Assert.Equal(9, replacement.Flags);
        Assert.Equal(0, replacement.Writes);
        Assert.False(gate.IsActive);
        gate.Enable(replacement);
        gate.Restore(replacement);
        Assert.Equal(9, replacement.Flags);
    }

    [Fact]
    public void Native_gate_keeps_restore_pending_after_a_write_failure()
    {
        var gate = new HeadSizeNativeGate();
        var state = new FakeMutators(100, 0);
        gate.Enable(state);
        state.ThrowOnWrite = true;
        Assert.Throws<InvalidOperationException>(() => gate.Restore(state));
        Assert.True(gate.IsActive);
        state.ThrowOnWrite = false;
        gate.Restore(state);
        Assert.Equal(0, state.Flags);
        Assert.False(gate.IsActive);
    }

    [Fact]
    public void Native_gate_noop_shutdown_does_not_access_native_state()
    {
        var gate = new HeadSizeNativeGate();
        gate.Restore(null);
        Assert.False(gate.IsActive);
        gate.Enable(new FakeMutators(100, 0));
        gate.Restore(null); // 地图实体已经删除，不再持有旧指针或在下一图恢复。
        Assert.False(gate.IsActive);
    }

    private sealed class FakeMutators(uint identity, int flags) : IHeadSizeMutators
    {
        public uint Identity => identity;
        public int Flags { get; set; } = flags;
        public int Writes { get; private set; }
        public bool ThrowOnWrite { get; set; }
        public void SetFlags(int value)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("fake mutator write failure");
            Flags = value;
            Writes++;
        }
    }

    [Fact]
    public void Refresh_reapplies_target_after_each_native_reset_and_stops_writing_when_disabled()
    {
        var runtime = new HeadSizeRuntime();
        var pawn = new FakePawn();
        HeadSizePlayer[] players = [Player(1, "CT player", 3, pawn)];
        runtime.Execute(Parse("2", "ct", "2"), players);
        for (var frame = 0; frame < 64; frame++)
        {
            pawn.ResetByEngine(1);
            Assert.Equal(1, runtime.Refresh(players).Changed);
            Assert.Equal(2, pawn.Scale);
            Assert.Equal(0, runtime.Refresh(players).Changed);
        }
        runtime.Stop(players);
        pawn.ResetByEngine(1.5f);
        Assert.Equal(0, runtime.Refresh(players).Changed);
        Assert.Equal(1.5f, pawn.Scale);
    }

    private static HeadSizePlayer Player(ulong steamId, string name, byte team, FakePawn pawn, long netKills = 0)
        => new(HeadSizeIdentity.Create(steamId, 100), name, team, netKills, pawn);

    private sealed class FakePawn(float scale = 1) : IHeadSizePawn
    {
        public bool IsValid { get; set; } = true;
        public bool ThrowOnWrite { get; set; }
        public float Scale { get; private set; } = scale;
        public List<float> Writes { get; } = [];
        public void ResetByEngine(float value) => Scale = value;
        public void SetScale(float value)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("fake write failure");
            Scale = value;
            Writes.Add(value);
        }
    }
}
