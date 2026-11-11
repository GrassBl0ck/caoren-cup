using CaorenCup.Contracts;
using Xunit;

namespace CaorenCupPlugin.Tests;

public class GameModeBridgePolicyTests
{
    [Fact]
    public void HeartbeatSerializesLifecycleAsWebProtocolString()
    {
        var state = new ActiveGameModeState("random", GameModeLifecycleState.Live, 15, 4);
        var json = System.Text.Json.JsonSerializer.Serialize(GameModeBridgePolicy.ToHeartbeatState(state), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"lifecycle\":\"Live\"", json);
    }
    [Fact]
    public void MissingOrRejectedProviderNeverStartsPreviousMode()
    {
        var runtime = new FakeRuntime();
        Assert.False(GameModeBridgePolicy.Apply(runtime, "GAME_MODE_START", "random", new Dictionary<string, string>()).Success);
        Assert.Equal(0, runtime.Starts);
        runtime.Loaded = true;
        runtime.RejectSelection = true;
        Assert.False(GameModeBridgePolicy.Apply(runtime, "GAME_MODE_START", "random", new Dictionary<string, string>()).Success);
        Assert.Equal(0, runtime.Starts);
    }

    [Fact]
    public void FixedRandomRulesCannotBeOverriddenAndValidStartForwardsWarmup()
    {
        var runtime = new FakeRuntime { Loaded = true };
        Assert.False(GameModeBridgePolicy.Apply(runtime, "GAME_MODE_START", "random", new Dictionary<string, string> { ["mp_maxrounds"] = "13" }).Success);
        Assert.Equal(0, runtime.Selections);
        Assert.True(GameModeBridgePolicy.Apply(runtime, "GAME_MODE_START", "random", new Dictionary<string, string>(), 20, "de_dust2").Success);
        Assert.Equal(1, runtime.Starts);
        Assert.Equal(20, runtime.Warmup);
        Assert.Equal("de_dust2", runtime.Map);
    }

    [Fact]
    public void RandomRejectsOlderModeOrRevisionHeartbeat()
    {
        var current = new ActiveGameModeState("random", GameModeLifecycleState.Live, 15, 4);
        Assert.False(GameModeBridgePolicy.MatchesRandomHeartbeat(new() { GameModeId = "competitive", GameModeRevision = 4 }, current));
        Assert.False(GameModeBridgePolicy.MatchesRandomHeartbeat(new() { GameModeId = "random", GameModeRevision = 3 }, current));
        Assert.False(GameModeBridgePolicy.MatchesRandomHeartbeat(new(), current));
        Assert.True(GameModeBridgePolicy.MatchesRandomHeartbeat(new() { GameModeId = "random", GameModeRevision = 4 }, current));
        Assert.True(GameModeBridgePolicy.MatchesRandomHeartbeat(new(), new("competitive", GameModeLifecycleState.Live, 15, 4)));
    }

    private sealed class FakeRuntime : ICaorenCupGameModeRuntime
    {
        public bool Loaded, RejectSelection;
        public int Starts, Selections;
        public int? Warmup;
        public string? Map;
        public ActiveGameModeState State => new("competitive", GameModeLifecycleState.Lobby, 15, 1);
        public IReadOnlyList<GameModeDescriptor> AvailableModes => Loaded ? [new("random", "随机乱斗", "Random", [], null, 15,
            [new("mp_maxrounds", GameModeCvarType.Integer, "24", "24", "24", WebEditable: false, GameEditable: false)])] : [];
        public GameModeResult SelectMode(string modeId) { Selections++; return RejectSelection ? GameModeResult.Fail("blocked") : GameModeResult.Ok("selected"); }
        public GameModeResult SetOverride(string name, string value, bool webSource) => GameModeResult.Ok("set");
        public GameModeResult Start(string? mapName = null, int? warmupSeconds = null) { Starts++; Map = mapName; Warmup = warmupSeconds; return GameModeResult.Ok("started"); }
        public GameModeResult Stop() => GameModeResult.Ok("stopped");
        public string GetStatusText() => "test";
    }
}
