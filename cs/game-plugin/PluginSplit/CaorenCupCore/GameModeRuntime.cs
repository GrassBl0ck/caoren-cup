using CaorenCup.Contracts;
using CaorenCup.Features;

namespace CaorenCup.Core;

/// <summary>
/// 统一管理模式选择、CVar 托管、Warmup 和 Live 生命周期。
/// 具体玩法由已注册的 Gamemode provider 提供。
/// </summary>
public sealed class GameModeRuntime : ICaorenCupGameModeRuntime, IDisposable
{
    private const int DefaultWarmupSeconds = 15;
    private const int RoundRestoreTimeoutSeconds = 12;
    private const string AuthorityConflict = "其他模式正在控制比赛，请先结束该模式。";
    private const string CleanupRequired = "当前模式尚未恢复服务器设置，请先重试停止。";
    private readonly ManagedCvarScope _managedCvars;
    private readonly Action<float, Action> _scheduleTimer;
    private readonly Action<string> _executeServerCommand;
    private readonly Func<string?> _getCurrentMap;
    private readonly Action<string> _notify;
    private readonly Action? _resetFeatures;
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private ActiveGameModeState _state = new(null, GameModeLifecycleState.Lobby, DefaultWarmupSeconds, 0);
    private ICaorenCupGameModeProvider? _activeProvider;
    private bool _providerNeedsStop;
    private string? _cvarOwner;
    private string? _roundBackupPrefix;
    private string? _roundBackupMap;
    private sealed record PendingRoundRestore(int Round);
    private PendingRoundRestore? _pendingRoundRestore;

    public GameModeRuntime(
        ManagedCvarScope managedCvars,
        Action<float, Action> scheduleTimer,
        Action<string> executeServerCommand,
        Func<string?> getCurrentMap,
        Action<string> notify,
        Action? resetFeatures = null)
    {
        _managedCvars = managedCvars;
        _scheduleTimer = scheduleTimer;
        _executeServerCommand = executeServerCommand;
        _getCurrentMap = getCurrentMap;
        _notify = notify;
        _resetFeatures = resetFeatures;
    }

    public ActiveGameModeState State => _state;
    internal string? MatchId => _state.MatchId;
    internal long RoundTimeline => _state.RoundTimeline;
    internal bool RoundRestorePending => _pendingRoundRestore is not null;

    private bool OwnsMatch => CaorenCupMatchAuthority.IsOwnedBy(this);
    internal bool CleanupPending => _state.Lifecycle == GameModeLifecycleState.Error && OwnsMatch;

    internal string? GetRoundBackupFileName(int round)
    {
        if (_state.Lifecycle != GameModeLifecycleState.Live || round < 1 || _roundBackupPrefix is null
            || !string.Equals(_roundBackupMap, _getCurrentMap(), StringComparison.OrdinalIgnoreCase))
            return null;

        // The engine's round00 file holds the start of the first played round.
        return $"{_roundBackupPrefix}_round{round - 1:D2}.txt";
    }

    internal GameModeResult RequestRoundRestore(int round)
    {
        if (RoundRestorePending)
            return GameModeResult.Fail("正在等待 CS2 载入上一条回合恢复请求。");
        var fileName = GetRoundBackupFileName(round);
        if (fileName is null || !OwnsMatch)
            return GameModeResult.Fail("当前没有可恢复回合的正式对局。");

        var pending = new PendingRoundRestore(round);
        _pendingRoundRestore = pending;
        try
        {
            _scheduleTimer(RoundRestoreTimeoutSeconds, () =>
            {
                if (!ReferenceEquals(_pendingRoundRestore, pending)) return;
                _pendingRoundRestore = null;
                _notify($"等待 CS2 载入第 {round} 回合超时。管理员可以重试。");
            });
            _executeServerCommand($"mp_backup_restore_load_file {fileName}");
        }
        catch (Exception ex)
        {
            _pendingRoundRestore = null;
            return GameModeResult.Fail($"提交回合恢复失败：{ex.Message}");
        }

        var message = $"管理员已请求恢复至第 {round} 回合开局，等待 CS2 载入备份。";
        _notify(message);
        return GameModeResult.Ok(message);
    }

    internal bool ConfirmRoundRestore(int round)
    {
        if (_pendingRoundRestore?.Round != round || _state.Lifecycle != GameModeLifecycleState.Live || !OwnsMatch)
            return false;

        _pendingRoundRestore = null;
        _state = _state with { RoundTimeline = _state.RoundTimeline + 1 };
        _notify($"CS2 已恢复第 {round} 回合。");
        return true;
    }

    public IReadOnlyList<GameModeDescriptor> AvailableModes =>
        CaorenCupGameModeRegistry.Snapshot()
            .Select(provider => provider.Descriptor)
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public GameModeResult SelectMode(string modeId)
    {
        if (CaorenCupMatchAuthority.IsOwnedByAnother(this)) return GameModeResult.Fail(AuthorityConflict);
        if (CleanupPending) return GameModeResult.Fail(CleanupRequired);
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
            MatchId = null,
            RoundTimeline = 0,
            Lifecycle = GameModeLifecycleState.Lobby,
            WarmupSeconds = provider.Descriptor.DefaultWarmupSeconds,
            Error = null,
            Revision = _state.Revision + 1,
        };
        var message = $"已选择模式：{provider.Descriptor.DisplayName}，尚未启动。";
        _notify(message);
        return GameModeResult.Ok(message);
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
        if (CaorenCupMatchAuthority.IsOwnedByAnother(this)) return GameModeResult.Fail(AuthorityConflict);
        if (CleanupPending) return GameModeResult.Fail(CleanupRequired);
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
        var message = $"模式 {_activeProvider.Descriptor.DisplayName} 参数 {definition.Name} = {value}，将在模式启动时应用。";
        _notify(message);
        return GameModeResult.Ok(message);
    }

    public GameModeResult Start(string? mapName = null, int? warmupSeconds = null)
    {
        if (CaorenCupMatchAuthority.IsOwnedByAnother(this)) return GameModeResult.Fail(AuthorityConflict);
        if (CleanupPending) return GameModeResult.Fail(CleanupRequired);
        if (_activeProvider is null)
            return GameModeResult.Fail("请先选择模式。");
        if (CaorenCupPracticeState.IsPracticeActive)
            return GameModeResult.Fail("当前处于 MatchZy Practice，请先使用 /sleep 退出练习模式。");
        if (_state.Lifecycle is GameModeLifecycleState.Warmup or GameModeLifecycleState.Live)
            return GameModeResult.Fail("当前已有模式正在运行。");

        var seconds = warmupSeconds ?? _activeProvider.Descriptor.DefaultWarmupSeconds;
        if (seconds is < 1 or > 300)
            return GameModeResult.Fail("Warmup 秒数必须在 1 到 300 之间。");

        if (!CaorenCupMatchAuthority.TryAcquireCore(this))
            return GameModeResult.Fail(AuthorityConflict);

        _state = _state with { MatchId = Guid.NewGuid().ToString("N"), RoundTimeline = 0 };
        try { return BeginWarmup(_activeProvider, mapName, seconds); }
        catch (Exception ex) { return FailAndRestore($"启动模式失败：{ex.Message}"); }
    }

    private GameModeResult BeginWarmup(ICaorenCupGameModeProvider provider, string? mapName, int seconds)
    {
        var request = new GameModeStartRequest(
            provider.Descriptor.Id,
            mapName ?? _getCurrentMap(),
            seconds,
            new Dictionary<string, string>(_overrides, StringComparer.OrdinalIgnoreCase));
        var context = CreateContext();
        var validation = provider.ValidateStart(request, context);
        if (!validation.Success)
            return FailAndRestore(validation.Message);

        var cvarValidation = GameModeCvarValidator.Validate(provider.Descriptor, request.Overrides, webSource: true);
        if (!cvarValidation.Success)
            return FailAndRestore(cvarValidation.Message);

        _executeServerCommand("exec grass/default_common.cfg");
        if (!string.IsNullOrWhiteSpace(provider.Descriptor.ConfigFileName))
            _executeServerCommand($"exec grass/{provider.Descriptor.ConfigFileName}");

        _providerNeedsStop = true;
        var warmup = provider.OnWarmupStarted(context);
        if (!warmup.Success)
            return FailAndRestore(warmup.Message);

        _managedCvars.ResetOwner(_cvarOwner ?? string.Empty);
        foreach (var definition in provider.Descriptor.Cvars)
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
        var warmupRevision = _state.Revision;
        _scheduleTimer(seconds, () => OnWarmupExpired(warmupRevision));
        _notify($"已启动 {provider.Descriptor.DisplayName}，Warmup {seconds} 秒。");
        return GameModeResult.Ok("模式已进入 Warmup。");
    }

    public GameModeResult Stop()
    {
        if (CaorenCupMatchAuthority.IsOwnedByAnother(this)) return GameModeResult.Fail(AuthorityConflict);
        if (_activeProvider is null)
        {
            _state = _state with { Lifecycle = GameModeLifecycleState.Stopped, Revision = _state.Revision + 1 };
            return GameModeResult.Ok("当前没有正在运行的模式。");
        }

        if (!CaorenCupMatchAuthority.TryAcquireCore(this))
            return GameModeResult.Fail(AuthorityConflict);

        _pendingRoundRestore = null;
        var result = RestoreModeSettings(_activeProvider);
        if (!result.Success) return SetError(result.Message);

        _state = _state with { Lifecycle = GameModeLifecycleState.Stopped, Error = null, Revision = _state.Revision + 1 };
        CaorenCupMatchAuthority.Release(this);
        _notify($"已停止 {_activeProvider.Descriptor.DisplayName}，模式 CVar 已恢复。");
        return result;
    }

    public string GetStatusText()
    {
        var mode = _activeProvider?.Descriptor.DisplayName ?? "未选择";
        var error = string.IsNullOrWhiteSpace(_state.Error) ? string.Empty : $"，错误：{_state.Error}";
        return $"模式：{mode} | 状态：{_state.Lifecycle} | Warmup：{_state.WarmupSeconds} 秒{error}";
    }

    internal GameModeResult? FinishLiveMatch() =>
        OwnsMatch && _state.Lifecycle == GameModeLifecycleState.Live ? Stop() : null;

    public void Dispose()
    {
        if (!OwnsMatch) return;
        try
        {
            var result = Stop();
            if (!result.Success) Console.Error.WriteLine($"[草人杯] {result.Message}");
        }
        finally { CaorenCupMatchAuthority.Release(this); }
    }

    private void OnWarmupExpired(long revision)
    {
        if (!OwnsMatch || _state.Revision != revision
            || _state.Lifecycle != GameModeLifecycleState.Warmup || _activeProvider is null) return;

        GameModeResult result;
        try { result = EnterLive(_activeProvider); }
        catch (Exception ex) { result = GameModeResult.Fail(ex.Message); }
        if (!result.Success)
        {
            var failure = FailAndRestore(result.Message);
            _notify($"模式进入正式比赛失败：{failure.Message}");
        }
    }

    private GameModeResult EnterLive(ICaorenCupGameModeProvider provider)
    {
        // exec 的配置可能晚于 Start 中的设值执行。正式开局前再次应用已校验参数，
        // 不重建托管快照，停止时仍恢复启动前的值。
        foreach (var definition in provider.Descriptor.Cvars)
        {
            var value = _overrides.TryGetValue(definition.Name, out var overrideValue)
                ? overrideValue
                : definition.DefaultValue;
            _executeServerCommand($"{definition.Name} {value}");
        }
        _roundBackupPrefix = $"caoren_backup_{MatchId}";
        _roundBackupMap = _getCurrentMap();
        _executeServerCommand("mp_backup_round_auto 1");
        _executeServerCommand($"mp_backup_round_file {_roundBackupPrefix}");
        _executeServerCommand("mp_backup_round_file_pattern %prefix%_round%round%.txt");
        var result = provider.OnLiveStarted(CreateContext());
        if (!result.Success) return result;

        _state = _state with
        {
            Lifecycle = GameModeLifecycleState.Live,
            Error = null,
            Revision = _state.Revision + 1,
        };
        _notify($"{provider.Descriptor.DisplayName} Warmup 结束，已进入正式比赛。");
        return result;
    }

    private GameModeResult FailAndRestore(string message)
    {
        var cleanup = RestoreModeSettings(_providerNeedsStop ? _activeProvider : null);
        if (!cleanup.Success) return SetError($"{message}。{cleanup.Message}");

        var result = SetError(message);
        CaorenCupMatchAuthority.Release(this);
        return result;
    }

    private GameModeResult RestoreModeSettings(ICaorenCupGameModeProvider? provider)
    {
        try
        {
            var result = provider?.OnStopped(CreateContext()) ?? GameModeResult.Ok("模式设置已恢复。");
            if (result.Success) _providerNeedsStop = false;
            _managedCvars.ResetOwner(_cvarOwner ?? string.Empty);
            return result;
        }
        catch (Exception ex)
        {
            return GameModeResult.Fail($"模式设置恢复失败，请重试停止：{ex.Message}");
        }
    }

    private GameModeResult SetError(string message)
    {
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
        ExecuteServerCommand = command =>
        {
            // 开赛重置必须先清理旧托管项，再建立本模式快照。
            // 排入控制台队列会在 Set 之后 ResetAll，并把旧值追加到新值后面。
            if (_resetFeatures is not null && command.Trim().Equals("css_reset_plu", StringComparison.OrdinalIgnoreCase))
                _resetFeatures();
            else
                _executeServerCommand(command);
        },
        GetCurrentMap = _getCurrentMap,
        Notify = _notify,
    };
}
