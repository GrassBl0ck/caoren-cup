using CaorenCup.Contracts;
using CaorenCup.Features;

namespace CaorenCup.Core;

/// <summary>
/// 统一管理模式选择、CVar 托管、Warmup 和 Live 生命周期。
/// 具体玩法由已注册的 Gamemode provider 提供。
/// </summary>
public sealed class GameModeRuntime : ICaorenCupGameModeRuntime
{
    private const int DefaultWarmupSeconds = 15;
    private readonly ManagedCvarScope _managedCvars;
    private readonly Action<float, Action> _scheduleTimer;
    private readonly Action<string> _executeServerCommand;
    private readonly Func<string?> _getCurrentMap;
    private readonly Action<string> _notify;
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private ActiveGameModeState _state = new(null, GameModeLifecycleState.Lobby, DefaultWarmupSeconds, 0);
    private ICaorenCupGameModeProvider? _activeProvider;
    private string? _cvarOwner;

    public GameModeRuntime(
        ManagedCvarScope managedCvars,
        Action<float, Action> scheduleTimer,
        Action<string> executeServerCommand,
        Func<string?> getCurrentMap,
        Action<string> notify)
    {
        _managedCvars = managedCvars;
        _scheduleTimer = scheduleTimer;
        _executeServerCommand = executeServerCommand;
        _getCurrentMap = getCurrentMap;
        _notify = notify;
    }

    public ActiveGameModeState State => _state;

    public IReadOnlyList<GameModeDescriptor> AvailableModes =>
        CaorenCupGameModeRegistry.Snapshot()
            .Select(provider => provider.Descriptor)
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public GameModeResult SelectMode(string modeId)
    {
        var normalized = NormalizeModeId(modeId);
        if (string.IsNullOrWhiteSpace(normalized))
            return GameModeResult.Fail("模式 ID 不能为空。");
        if (CaorenCupPracticeState.IsPracticeActive)
            return GameModeResult.Fail("当前处于 MatchZy Practice，请先使用 /sleep 退出练习模式。");
        if (_state.Lifecycle is GameModeLifecycleState.Warmup or GameModeLifecycleState.Live)
            return GameModeResult.Fail("Warmup 或正式比赛开始后不能切换模式。");

        var provider = CaorenCupGameModeRegistry.Find(normalized);
        if (provider is null)
            return GameModeResult.Fail($"模式未加载或不存在：{normalized}");
        if (!provider.Descriptor.Available)
            return GameModeResult.Fail($"模式当前不可用：{normalized}");

        if (_activeProvider is not null && !string.Equals(_activeProvider.Descriptor.Id, normalized, StringComparison.OrdinalIgnoreCase))
            _managedCvars.ResetOwner(_cvarOwner ?? string.Empty);

        _activeProvider = provider;
        _cvarOwner = $"gamemode:{provider.Descriptor.Id}";
        _overrides.Clear();
        _state = _state with
        {
            ModeId = provider.Descriptor.Id,
            Lifecycle = GameModeLifecycleState.Lobby,
            WarmupSeconds = provider.Descriptor.DefaultWarmupSeconds,
            Error = null,
            Revision = _state.Revision + 1,
        };
        return GameModeResult.Ok($"已选择模式：{provider.Descriptor.DisplayName}");
    }

    private static string NormalizeModeId(string? modeId) => modeId?.Trim().ToLowerInvariant() switch
    {
        "comp" => "competitive",
        "under" => "undercover",
        "unb" => "unbalanced",
        var normalized => normalized ?? string.Empty,
    };

    public GameModeResult SetOverride(string name, string value, bool webSource)
    {
        if (_activeProvider is null || string.IsNullOrWhiteSpace(_state.ModeId))
            return GameModeResult.Fail("请先选择模式。");
        if (_state.Lifecycle is GameModeLifecycleState.Warmup or GameModeLifecycleState.Live)
            return GameModeResult.Fail("Warmup 或正式比赛开始后不能修改模式 CVar。");

        var definition = _activeProvider.Descriptor.Cvars.FirstOrDefault(item =>
            string.Equals(item.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return GameModeResult.Fail($"模式不允许修改 CVar：{name}");
        var validation = GameModeCvarValidator.Validate(
            _activeProvider.Descriptor,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [definition.Name] = value },
            webSource);
        if (!validation.Success) return validation;

        _overrides[definition.Name] = value;
        return GameModeResult.Ok($"已设置 {definition.Name} = {value}");
    }

    public GameModeResult Start(string? mapName = null, int? warmupSeconds = null)
    {
        if (_activeProvider is null)
            return GameModeResult.Fail("请先选择模式。");
        if (CaorenCupPracticeState.IsPracticeActive)
            return GameModeResult.Fail("当前处于 MatchZy Practice，请先使用 /sleep 退出练习模式。");
        if (_state.Lifecycle is GameModeLifecycleState.Warmup or GameModeLifecycleState.Live)
            return GameModeResult.Fail("当前已有模式正在运行。");

        var seconds = warmupSeconds ?? _activeProvider.Descriptor.DefaultWarmupSeconds;
        if (seconds is < 1 or > 300)
            return GameModeResult.Fail("Warmup 秒数必须在 1 到 300 之间。");

        var request = new GameModeStartRequest(
            _activeProvider.Descriptor.Id,
            mapName ?? _getCurrentMap(),
            seconds,
            new Dictionary<string, string>(_overrides, StringComparer.OrdinalIgnoreCase));
        var context = CreateContext();
        var validation = _activeProvider.ValidateStart(request, context);
        if (!validation.Success)
            return Fail(validation.Message);

        var cvarValidation = GameModeCvarValidator.Validate(
            _activeProvider.Descriptor,
            request.Overrides,
            webSource: true);
        if (!cvarValidation.Success)
            return Fail(cvarValidation.Message);

        _executeServerCommand("exec grass/default_common.cfg");
        if (!string.IsNullOrWhiteSpace(_activeProvider.Descriptor.ConfigFileName))
            _executeServerCommand($"exec grass/{_activeProvider.Descriptor.ConfigFileName}");

        var warmup = _activeProvider.OnWarmupStarted(context);
        if (!warmup.Success)
            return Fail(warmup.Message);

        _managedCvars.ResetOwner(_cvarOwner ?? string.Empty);
        foreach (var definition in _activeProvider.Descriptor.Cvars)
        {
            var value = request.Overrides.TryGetValue(definition.Name, out var overrideValue)
                ? overrideValue
                : definition.DefaultValue;
            _managedCvars.Set(_cvarOwner!, definition.Name, value, definition.DefaultValue);
        }

        _executeServerCommand("mp_warmup_start");
        _state = _state with
        {
            Lifecycle = GameModeLifecycleState.Warmup,
            WarmupSeconds = seconds,
            Error = null,
            Revision = _state.Revision + 1,
        };
        _notify($"已启动 {_activeProvider.Descriptor.DisplayName}，Warmup {seconds} 秒。");
        _scheduleTimer(seconds, EnterLive);
        return GameModeResult.Ok("模式已进入 Warmup。");
    }

    public GameModeResult Stop()
    {
        if (_activeProvider is null)
        {
            _state = _state with { Lifecycle = GameModeLifecycleState.Stopped, Revision = _state.Revision + 1 };
            return GameModeResult.Ok("当前没有正在运行的模式。");
        }

        var result = _activeProvider.OnStopped(CreateContext());
        _managedCvars.ResetOwner(_cvarOwner ?? string.Empty);
        _state = _state with
        {
            Lifecycle = result.Success ? GameModeLifecycleState.Stopped : GameModeLifecycleState.Error,
            Error = result.Success ? null : result.Message,
            Revision = _state.Revision + 1,
        };
        if (result.Success) _notify($"已停止 {_activeProvider.Descriptor.DisplayName}，模式 CVar 已恢复。");
        return result;
    }

    public string GetStatusText()
    {
        var mode = _activeProvider?.Descriptor.DisplayName ?? "未选择";
        var error = string.IsNullOrWhiteSpace(_state.Error) ? string.Empty : $"，错误：{_state.Error}";
        return $"模式：{mode} | 状态：{_state.Lifecycle} | Warmup：{_state.WarmupSeconds} 秒{error}";
    }

    private void EnterLive()
    {
        if (_state.Lifecycle != GameModeLifecycleState.Warmup || _activeProvider is null) return;
        var result = _activeProvider.OnLiveStarted(CreateContext());
        if (!result.Success)
        {
            Fail(result.Message);
            return;
        }

        _state = _state with
        {
            Lifecycle = GameModeLifecycleState.Live,
            Error = null,
            Revision = _state.Revision + 1,
        };
        _notify($"{_activeProvider.Descriptor.DisplayName} Warmup 结束，已进入正式比赛。");
    }

    private GameModeResult Fail(string message)
    {
        _managedCvars.ResetOwner(_cvarOwner ?? string.Empty);
        _state = _state with
        {
            Lifecycle = GameModeLifecycleState.Error,
            Error = message,
            Revision = _state.Revision + 1,
        };
        return GameModeResult.Fail(message);
    }

    private GameModeRuntimeContext CreateContext() => new()
    {
        ExecuteServerCommand = _executeServerCommand,
        GetCurrentMap = _getCurrentMap,
        Notify = _notify,
    };
}
