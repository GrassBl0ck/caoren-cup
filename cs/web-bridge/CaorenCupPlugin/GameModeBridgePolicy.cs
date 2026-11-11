using CaorenCup.Contracts;

namespace CaorenCupPlugin;

public static class GameModeBridgePolicy
{
    public static object? ToHeartbeatState(ActiveGameModeState? state) => state is null ? null : new
    {
        state.ModeId, Lifecycle = state.Lifecycle.ToString(), state.WarmupSeconds, state.Revision, state.Error,
    };
    public static GameModeResult Apply(ICaorenCupGameModeRuntime? runtime, string type, string modeId,
        IReadOnlyDictionary<string, string> overrides, int? warmupSeconds = null, string? mapName = null)
    {
        if (runtime is null) return GameModeResult.Fail("模式管理尚未加载。");
        if (type == "GAME_MODE_STOP") return runtime.Stop();
        if (type is not ("GAME_MODE_SELECT" or "GAME_MODE_START")) return GameModeResult.Fail("未知模式操作。");
        if (!GameModeCommandPolicy.IsAllowedMode(modeId)) return GameModeResult.Fail("未知模式。");
        var descriptor = runtime.AvailableModes.FirstOrDefault(mode => mode.Id == modeId && mode.Available);
        if (descriptor is null) return GameModeResult.Fail($"模式未加载：{modeId}");
        if (warmupSeconds is < 1 or > 300) return GameModeResult.Fail("热身秒数必须在1至300之间。");
        var validation = GameModeCvarValidator.Validate(descriptor, overrides, webSource: true);
        if (!validation.Success) return validation;
        var selected = runtime.SelectMode(modeId);
        if (!selected.Success) return selected;
        foreach (var item in overrides)
        {
            var result = runtime.SetOverride(item.Key, item.Value, webSource: true);
            if (!result.Success) return result;
        }
        return type == "GAME_MODE_START" ? runtime.Start(mapName, warmupSeconds) : selected;
    }

    public static bool MatchesRandomHeartbeat(PluginHeartbeatResponse? response, ActiveGameModeState? current)
    {
        if (current?.ModeId != "random" && response?.GameModeId != "random") return true;
        return current is not null && response is not null && response.GameModeId == current.ModeId && response.GameModeRevision == current.Revision;
    }
}
