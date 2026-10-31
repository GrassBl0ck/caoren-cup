namespace CaorenCup.Contracts;

public enum GameModeLifecycleState
{
    Lobby,
    Warmup,
    Live,
    Stopped,
    Error,
}

public enum GameModeCvarType
{
    Integer,
    Decimal,
    Boolean,
    Text,
}

public sealed record GameModeCvarDefinition(
    string Name,
    GameModeCvarType Type,
    string DefaultValue,
    string? MinimumValue = null,
    string? MaximumValue = null,
    bool WebEditable = true,
    bool GameEditable = true,
    bool RestoreOnStop = true,
    bool WarmupOnly = false);

public sealed record GameModeDescriptor(
    string Id,
    string DisplayName,
    string ModuleKey,
    IReadOnlyList<string> SupportedMaps,
    string? RequiredMap,
    int DefaultWarmupSeconds,
    IReadOnlyList<GameModeCvarDefinition> Cvars,
    bool Available = true,
    string? ConfigFileName = null)
{
    public bool SupportsMap(string? mapName) =>
        string.IsNullOrWhiteSpace(mapName) ||
        SupportedMaps.Count == 0 ||
        SupportedMaps.Any(map => string.Equals(map, mapName, StringComparison.OrdinalIgnoreCase));
}

public sealed record GameModeStartRequest(
    string ModeId,
    string? MapName,
    int WarmupSeconds,
    IReadOnlyDictionary<string, string> Overrides);

public sealed record GameModeResult(bool Success, string Message)
{
    public static GameModeResult Ok(string message) => new(true, message);
    public static GameModeResult Fail(string message) => new(false, message);
}

public sealed class GameModeRuntimeContext
{
    public required Action<string> ExecuteServerCommand { get; init; }
    public required Func<string?> GetCurrentMap { get; init; }
    public required Action<string> Notify { get; init; }
}

public interface ICaorenCupGameModeProvider
{
    GameModeDescriptor Descriptor { get; }
    GameModeResult ValidateStart(GameModeStartRequest request, GameModeRuntimeContext context);
    GameModeResult OnWarmupStarted(GameModeRuntimeContext context);
    GameModeResult OnLiveStarted(GameModeRuntimeContext context);
    GameModeResult OnStopped(GameModeRuntimeContext context);
}


public interface ICaorenCupGameModeRuntime
{
    ActiveGameModeState State { get; }
    IReadOnlyList<GameModeDescriptor> AvailableModes { get; }
    GameModeResult SelectMode(string modeId);
    GameModeResult SetOverride(string name, string value, bool webSource);
    GameModeResult Start(string? mapName = null, int? warmupSeconds = null);
    GameModeResult Stop();
    string GetStatusText();
}
public sealed record ActiveGameModeState(
    string? ModeId,
    GameModeLifecycleState Lifecycle,
    int WarmupSeconds,
    long Revision,
    string? Error = null);

public static class CaorenCupGameModeRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ICaorenCupGameModeProvider> Providers =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ICaorenCupGameModeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Descriptor.Id))
            throw new ArgumentException("Game mode id is required.", nameof(provider));

        lock (Gate)
        {
            if (Providers.TryGetValue(provider.Descriptor.Id, out var existing) &&
                !ReferenceEquals(existing, provider))
                throw new InvalidOperationException($"Game mode id already registered: {provider.Descriptor.Id}");

            Providers[provider.Descriptor.Id] = provider;
        }
    }

    public static void Unregister(ICaorenCupGameModeProvider provider)
    {
        lock (Gate)
        {
            foreach (var item in Providers.Where(item => ReferenceEquals(item.Value, provider)).ToArray())
                Providers.Remove(item.Key);
        }
    }

    public static ICaorenCupGameModeProvider? Find(string? modeId)
    {
        if (string.IsNullOrWhiteSpace(modeId)) return null;
        lock (Gate)
            return Providers.TryGetValue(modeId.Trim(), out var provider) ? provider : null;
    }

    public static ICaorenCupGameModeProvider[] Snapshot()
    {
        lock (Gate) return Providers.Values.ToArray();
    }
}

public static class GameModeCvarValidator
{
    public static GameModeResult Validate(
        GameModeDescriptor descriptor,
        IReadOnlyDictionary<string, string> overrides,
        bool webSource)
    {
        var definitions = descriptor.Cvars.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides)
        {
            if (!definitions.TryGetValue(item.Key, out var definition))
                return GameModeResult.Fail($"模式 {descriptor.Id} 不允许修改 CVar：{item.Key}");
            if (webSource && !definition.WebEditable)
                return GameModeResult.Fail($"CVar {item.Key} 不允许网页修改。");
            if (!webSource && !definition.GameEditable)
                return GameModeResult.Fail($"CVar {item.Key} 不允许游戏内修改。");
            if (!TryValidateValue(definition, item.Value, out var reason))
                return GameModeResult.Fail($"CVar {item.Key} 值无效：{reason}");
        }

        return GameModeResult.Ok("CVar 校验通过。");
    }

    public static bool TryValidateValue(GameModeCvarDefinition definition, string value, out string reason)
    {
        reason = string.Empty;
        switch (definition.Type)
        {
            case GameModeCvarType.Integer:
                if (!int.TryParse(value, out var integer))
                {
                    reason = "必须是整数";
                    return false;
                }
                if (int.TryParse(definition.MinimumValue, out var minInteger) && integer < minInteger)
                {
                    reason = $"不能小于 {minInteger}";
                    return false;
                }
                if (int.TryParse(definition.MaximumValue, out var maxInteger) && integer > maxInteger)
                {
                    reason = $"不能大于 {maxInteger}";
                    return false;
                }
                return true;
            case GameModeCvarType.Decimal:
                if (!double.TryParse(value, out var decimalValue))
                {
                    reason = "必须是数字";
                    return false;
                }
                if (double.TryParse(definition.MinimumValue, out var minDecimal) && decimalValue < minDecimal)
                {
                    reason = $"不能小于 {minDecimal}";
                    return false;
                }
                if (double.TryParse(definition.MaximumValue, out var maxDecimal) && decimalValue > maxDecimal)
                {
                    reason = $"不能大于 {maxDecimal}";
                    return false;
                }
                return true;
            case GameModeCvarType.Boolean:
                if (value is "0" or "1" or "true" or "false") return true;
                reason = "必须是 0、1、true 或 false";
                return false;
            case GameModeCvarType.Text:
                if (value.IndexOfAny([';', '\n', '\r']) >= 0)
                {
                    reason = "不能包含命令分隔符";
                    return false;
                }
                return !string.IsNullOrWhiteSpace(value);
            default:
                reason = "未知类型";
                return false;
        }
    }
}
