using System.Text.Json;

namespace CaorenCup.Contracts.MatchRuntime;

public interface IMatchRuntimeHost : IMatchServerActions
{
    IReadOnlyList<string> DemoSegments { get; }
    bool WaitForEngineRestore => false;

    void ApplyFormalMatchConfig();

    void ExecuteGoingLive();

    void SwapSides();

    void StartDemo(MatchDefinition definition);

    void PauseMatch();

    void UnpauseMatch();

    bool RestoreFormalMatchConfig();
}

public sealed class FormalMatchRuntime
{
    public const string WebCommandType = "MATCH_RUNTIME_COMMAND";

    private readonly IMatchRuntimeHost _host;
    private readonly TimeProvider _time;
    private readonly bool _useServerAuthority;
    private readonly CompetitiveMatchRuntime _runtime = new();
    private readonly MatchCombatOutbox _combatOutbox = new();
    private DateTimeOffset? _warmupEndsAt;
    private (RestorePlan Plan, DateTimeOffset Deadline)? _pendingNativeRestore;
    private MatchDefinition? _definition;
    private RosterSeatManager? _roster;
    private ReadyCoordinator? _ready;
    private PauseCoordinator? _pauses;
    private RoundBackupManager? _backups;
    private bool _formalConfigApplied;
    private bool _demoStarted;

    public sealed record AbilityRestoreRequest(string CheckpointId, string RoundKey, long RuntimeEpoch);
    public sealed record RuntimeRosterAssignment(string SteamId, EngineSide Side);

    public AbilityRestoreRequest? PendingAbilityRestore { get; private set; }

    private IReadOnlyDictionary<string, BridgePlayerStats>? PendingBridgeStatsRestore { get; set; }

    public IReadOnlyList<RuntimeRosterAssignment> CurrentRosterAssignments =>
        _roster?.Seats.Select(seat => new RuntimeRosterAssignment(seat.CurrentSteamId, seat.ExpectedSide)).ToArray() ?? [];

    public FormalMatchRuntime(IMatchRuntimeHost host, TimeProvider? time = null, bool useServerAuthority = false)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _time = time ?? TimeProvider.System;
        _useServerAuthority = useServerAuthority;
    }

    public MatchRuntimeSnapshot Snapshot => _runtime.Snapshot;
    public string? ModeId => _definition?.ModeId;
    public int ConfiguredWarmupSeconds => _definition?.WarmupSeconds ?? 15;
    public MatchRuntimeSnapshot HeartbeatSnapshot => Snapshot with
    {
        ReadySeats = _ready?.GetSeatStatuses(),
        WarmupEndsAtUnixMs = _warmupEndsAt?.ToUnixTimeMilliseconds(),
    };

    public bool CanAutomaticallyStart(bool abilitySyncConfirmed) =>
        Snapshot.Stage == MatchStage.ReadyCheck
        && _ready?.ValidateStart(abilitySyncConfirmed: abilitySyncConfirmed).Allowed == true;

    public IReadOnlyList<MatchEvent> Events => _runtime.Events;

    public bool RecordCombatEvent(string type, JsonElement payload) => _combatOutbox.Enqueue(Snapshot, type, payload);

    public MatchCombatBatch? ReadCombatBatch() => _combatOutbox.Read(Snapshot);

    public void AcknowledgeCombatBatch(MatchCombatBatch batch, long through) =>
        _combatOutbox.Acknowledge(Snapshot, batch, through);

    public IReadOnlyList<string> DemoSegments => _host.DemoSegments;

    public IReadOnlyCollection<RoundRestorePoint> RestorePoints => _backups?.RestorePoints ?? [];

    public bool IsActive => Snapshot.Stage is not MatchStage.Disabled and not MatchStage.Completed and not MatchStage.Aborted;

    public bool PendingCleanup => !IsActive && (_formalConfigApplied || _demoStarted);

    public bool RequiresAuthority => IsActive || PendingCleanup;

    public MatchCommandResult ApplyWebCommand(JsonElement payload, bool featureEnabled, string? matchController)
    {
        if (!featureEnabled) return Failure("FEATURE_DISABLED", "草人杯正式比赛运行时尚未启用。");
        if (!string.Equals(matchController, "caoren", StringComparison.OrdinalIgnoreCase))
            return Failure("CONTROLLER_CONFLICT", "当前标准竞技仍由 MatchZy 控制。");
        if (payload.ValueKind != JsonValueKind.Object)
            return Failure("INVALID_PAYLOAD", "比赛运行时命令必须是对象。");
        if (_useServerAuthority && !CaorenCupMatchAuthority.TryAcquire(this))
            return Failure("CONTROLLER_CONFLICT", "其他模式正在控制比赛。");

        try { return ApplyCommand(payload); }
        finally { ReleaseServerAuthorityIfIdle(); }
    }

    private MatchCommandResult ApplyCommand(JsonElement payload)
    {
        var action = ReadString(payload, "action").ToLowerInvariant();
        if (_pendingNativeRestore is not null && action is not ("abort" or "observe_player"))
            return Failure("RESTORE_PENDING", "正在等待 CS2 载入回合。");
        if (PendingCleanup && action != "abort")
            return Failure("CVAR_RESTORE_PENDING", "上次比赛仍有待恢复状态，请先重试中止清理。");
        if (action == "load") return Load(payload);
        var identity = ValidateIdentity(payload);
        if (!identity.Ok) return identity;
        var expectedStage = ReadString(payload, "expectedStage");
        var currentStage = JsonNamingPolicy.CamelCase.ConvertName(Snapshot.Stage.ToString());
        if (!string.IsNullOrWhiteSpace(expectedStage)
            && !string.Equals(expectedStage, currentStage, StringComparison.Ordinal))
            return Failure("EXPECTED_STAGE_MISMATCH", "比赛阶段已经变化，请刷新后重试。");

        return action switch
        {
            "begin_warmup" => BeginWarmup(payload),
            "begin_ready_check" => BeginReadyCheck(),
            "begin_going_live" => BeginGoingLive(payload),
            "confirm_side_swap" => ConfirmSideSwap(),
            "observe_player" => ObservePlayer(payload),
            "set_ready" => SetReady(payload, allowBeforeArrival: false),
            "set_ready_from_web" => SetReady(payload, allowBeforeArrival: true),
            "tactical_pause" => TacticalPause(payload),
            "technical_pause" => TechnicalPause(payload),
            "confirm_unpause" => ConfirmUnpause(payload),
            "force_pause" => ForcePause(payload),
            "force_unpause" => ForceUnpause(),
            "substitute" => Substitute(payload),
            "add_pregame_seat" => AddPregameSeat(payload),
            "stop" => StopRequest(payload),
            "restore" => RestoreRound(payload),
            "abort" => Cleanup(ReadString(payload, "reason")),
            _ => Failure("UNKNOWN_ACTION", "未知的比赛运行时操作。"),
        };
    }

    public MatchCommandResult RecoverFromWeb(
        JsonElement definitionPayload,
        MatchRuntimeSnapshot snapshot,
        IEnumerable<RoundRestorePoint>? restorePoints = null)
    {
        if (RequiresAuthority) return Failure("RUNTIME_BUSY", "正式比赛已经激活或仍待清理。");
        if (definitionPayload.ValueKind != JsonValueKind.Object
            || !string.Equals(snapshot.MatchId, ReadString(definitionPayload, "matchId"), StringComparison.Ordinal)
            || snapshot.Revision != ReadInt(definitionPayload, "revision") || snapshot.RuntimeEpoch <= 0
            || snapshot.Stage is MatchStage.Disabled or MatchStage.Completed or MatchStage.Aborted
            || (snapshot.WarmupEndsAtUnixMs is { } timestamp
                && (timestamp < 0 || timestamp > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())))
            return Failure("IDENTITY_MISMATCH", "恢复快照不属于当前正式比赛。");

        if (_useServerAuthority && !CaorenCupMatchAuthority.TryAcquire(this))
            return Failure("CONTROLLER_CONFLICT", "其他模式正在控制比赛。");

        try { return RestoreWebState(definitionPayload, snapshot, restorePoints); }
        finally { ReleaseServerAuthorityIfIdle(); }
    }

    private MatchCommandResult RestoreWebState(
        JsonElement definitionPayload,
        MatchRuntimeSnapshot snapshot,
        IEnumerable<RoundRestorePoint>? restorePoints)
    {
        var loaded = Load(definitionPayload);
        if (!loaded.Ok) return loaded;
        var recovered = _runtime.RecoverFromWeb(snapshot);
        if (!recovered.Ok) return recovered;
        if (_backups is not null && restorePoints is not null)
            foreach (var restorePoint in restorePoints) _backups.CreateRestorePoint(restorePoint);
        _roster?.SetTeamAEngineSide(snapshot.TeamAEngineSide);
        if (snapshot.ReadySeats is { } seats) _ready?.RestoreConfirmations(seats);
        if (snapshot.Stage == MatchStage.Staging) return recovered;

        _formalConfigApplied = true;
        _host.ApplyFormalMatchConfig();
        if (snapshot.Stage is MatchStage.Warmup or MatchStage.ReadyCheck)
        {
            _host.Execute("mp_warmup_start");
            if (snapshot.Stage == MatchStage.Warmup && snapshot.WarmupEndsAtUnixMs is { } deadline)
                _warmupEndsAt = DateTimeOffset.FromUnixTimeMilliseconds(deadline);
            return recovered;
        }

        _pauses!.RequestTechnicalPause("server recovery", _time.GetUtcNow(), atSafeBoundary: true);
        _host.PauseMatch();
        if (_definition is not null)
        {
            _host.StartDemo(_definition);
            _demoStarted = true;
        }
        return recovered;
    }

    public MatchCommandResult OnNativeRoundStart(bool isWarmup, JsonElement? players = null, int? engineRound = null)
    {
        if (!IsActive) return Failure("RUNTIME_INACTIVE", "草人杯正式比赛运行时未激活。");
        if (isWarmup) return Failure("WARMUP_IGNORED", "暖身回合不属于正式比赛。");
        if (Snapshot.Stage == MatchStage.GoingLive)
        {
            var live = _runtime.ConfirmLive();
            if (!live.Ok) return live;
            if (_definition is not null)
            {
                _host.StartDemo(_definition);
                _demoStarted = true;
            }
        }

        var restored = false;
        if (_pendingNativeRestore is { } pending)
        {
            if (engineRound != pending.Plan.Target.RoundNumber)
                return Failure("RESTORE_PENDING", "正在等待 CS2 载入目标回合。");
            _pendingNativeRestore = null;
            var result = FinishRestore(pending.Plan, _backups!.CompleteRestore(pending.Plan, new RestoreTarget(this)));
            if (!result.Ok) return result;
            restored = true;
        }

        var opened = _runtime.OpenRound(players);
        if (opened.Ok) CreateRoundRestorePoint();
        return opened.Ok
            ? new(true, restored ? "ROUND_RESTORED" : "ROUND_OPENED", opened.RoundKey ?? string.Empty)
            : new(false, opened.Code, opened.Message);
    }

    public MatchRoundResult OnNativeRoundEnd(EngineSide winner, JsonElement? players = null)
    {
        if (_pendingNativeRestore is not null)
            return new(false, "RESTORE_PENDING", "正在等待 CS2 载入回合。");
        var roundKey = Snapshot.CurrentRoundKey;
        if (roundKey is null) return new(false, "NO_OPEN_ROUND", "当前没有可结算的正式回合。");
        var rosterWinner = winner == Snapshot.TeamAEngineSide ? RosterSide.A : RosterSide.B;
        var result = _runtime.CompleteRound(roundKey, rosterWinner, players);
        if (result.MatchFinished && _demoStarted)
        {
            _host.StopDemo();
            _demoStarted = false;
        }
        if (result.MatchFinished && _formalConfigApplied && _host.RestoreFormalMatchConfig())
            _formalConfigApplied = false;
        ReleaseServerAuthorityIfIdle();
        return result;
    }

    public MatchCommandResult Cleanup(string reason)
    {
        _warmupEndsAt = null;
        _pendingNativeRestore = null;
        var abort = Snapshot.Stage is MatchStage.Disabled or MatchStage.Completed or MatchStage.Aborted
            ? new MatchCommandResult(true, "ALREADY_INACTIVE")
            : _runtime.Abort(string.IsNullOrWhiteSpace(reason) ? "runtime cleanup" : reason);
        if (_demoStarted)
        {
            _host.StopDemo();
            _demoStarted = false;
        }
        if (_formalConfigApplied && _host.RestoreFormalMatchConfig()) _formalConfigApplied = false;
        if (_formalConfigApplied) return Failure("CVAR_RESTORE_PENDING", "正式比赛 CVar 尚未恢复完整。");
        ReleaseServerAuthorityIfIdle();
        return abort;
    }

    private void ReleaseServerAuthorityIfIdle()
    {
        if (_useServerAuthority && !RequiresAuthority) CaorenCupMatchAuthority.Release(this);
    }

    private MatchCommandResult Load(JsonElement payload)
    {
        try
        {
            var definition = ParseDefinition(payload);
            var loaded = _runtime.Load(definition);
            if (!loaded.Ok) return loaded;
            _definition = definition;
            _warmupEndsAt = null;
            _roster = new RosterSeatManager(definition);
            _ready = new ReadyCoordinator(_roster);
            _pauses = new PauseCoordinator();
            _backups = new RoundBackupManager(_host, () => Snapshot);
            PendingAbilityRestore = null;
            PendingBridgeStatsRestore = null;
            return loaded;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException)
        {
            return Failure("INVALID_PAYLOAD", ex.Message);
        }
    }

    private MatchCommandResult BeginWarmup(JsonElement payload)
    {
        if (Snapshot.Stage != MatchStage.Staging)
            return Failure("INVALID_STAGE", "只能在比赛配置就绪后开始热身。");

        var warmupSeconds = _definition?.WarmupSeconds;
        if (payload.TryGetProperty("warmupSeconds", out var duration))
        {
            if (duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var seconds) || seconds is < 1 or > 300)
                return Failure("INVALID_WARMUP", "热身秒数必须在 1 到 300 之间。");
            warmupSeconds = seconds;
        }

        _formalConfigApplied = true;
        _host.ApplyFormalMatchConfig();
        if (warmupSeconds is { } countdown)
        {
            _host.Execute("mp_warmup_start");
            _warmupEndsAt = _time.GetUtcNow().AddSeconds(countdown);
        }
        return _runtime.BeginWarmup();
    }

    private MatchCommandResult BeginReadyCheck()
    {
        if (_warmupEndsAt is { } deadline && _time.GetUtcNow() < deadline)
            return Failure("WARMUP_IN_PROGRESS", "热身倒计时还未结束。");
        var result = _runtime.BeginReadyCheck();
        if (result.Ok) _warmupEndsAt = null;
        return result;
    }

    public bool TryFinishWarmup() =>
        _warmupEndsAt is { } deadline && _time.GetUtcNow() >= deadline && BeginReadyCheck().Ok;

    private MatchCommandResult BeginGoingLive(JsonElement payload)
    {
        if (_ready is null) return Failure("RUNTIME_NOT_READY", "Ready 服务尚未初始化。");
        var force = payload.TryGetProperty("force", out var forceElement) && forceElement.ValueKind == JsonValueKind.True;
        var abilitySyncConfirmed = !payload.TryGetProperty("abilitySyncConfirmed", out var abilityElement)
            || abilityElement.ValueKind == JsonValueKind.True;
        var gate = _ready.ValidateStart(force, abilitySyncConfirmed);
        if (!gate.Allowed)
            return Failure(gate.AbilitySyncBlocked ? "ABILITY_SYNC_BLOCKED" : "START_GATE_BLOCKED", "正式开赛门禁尚未满足。");
        var result = _runtime.BeginGoingLive();
        if (!result.Ok) return result;
        _host.ExecuteGoingLive();
        return result;
    }

    private MatchCommandResult ObservePlayer(JsonElement payload)
    {
        if (_ready is null) return Failure("RUNTIME_NOT_READY", "Ready 服务尚未初始化。");
        var steamId = ReadString(payload, "steamId");
        var connected = payload.TryGetProperty("connected", out var connectedElement)
            && connectedElement.ValueKind == JsonValueKind.True;
        EngineSide? side = ReadString(payload, "side").ToUpperInvariant() switch
        {
            "CT" => EngineSide.CounterTerrorist,
            "T" => EngineSide.Terrorist,
            _ => null,
        };
        return _ready.ObservePlayer(steamId, side, connected);
    }

    private MatchCommandResult SetReady(JsonElement payload, bool allowBeforeArrival)
    {
        if (_ready is null) return Failure("RUNTIME_NOT_READY", "Ready 服务尚未初始化。");
        if (Snapshot.Stage is not (MatchStage.Staging or MatchStage.Warmup or MatchStage.ReadyCheck))
            return Failure("INVALID_STAGE", "当前阶段不能修改准备状态。");
        if (!payload.TryGetProperty("ready", out var ready) || ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Failure("INVALID_PAYLOAD", "准备状态无效。");
        return _ready.SetReady(ReadString(payload, "steamId"), ready.GetBoolean(), allowBeforeArrival);
    }

    private MatchCommandResult AddPregameSeat(JsonElement payload)
    {
        var rosterName = ReadString(payload, "roster").ToUpperInvariant();
        var seatId = ReadString(payload, "seatId");
        var name = ReadString(payload, "name");
        if (_roster is null || _definition is null || rosterName is not ("A" or "B") ||
            seatId.Length is < 1 or > 128 || name.Length is < 1 or > 64 || _roster.Seats.Count >= 64)
            return Failure("INVALID_ROSTER", "参赛席位信息无效。");
        var seat = new MatchSeat(seatId, ParseRoster(rosterName), ReadString(payload, "steamId"), name, IsCaptain: false);
        if (!_roster.CanAdd(seat)) return Failure("SEAT_CONFLICT", "该玩家已在名单中或尚未绑定。");
        var result = _runtime.AddPregameSeat(seat);
        if (!result.Ok) return result;
        _roster.Add(seat);
        _definition = _definition with { Seats = _definition.Seats.Append(seat).ToArray() };
        return result;
    }

    private MatchCommandResult TacticalPause(JsonElement payload)
    {
        if (_pauses is null) return Failure("RUNTIME_NOT_READY", "暂停服务尚未初始化。");
        if (Snapshot.Stage is not MatchStage.FirstHalf and not MatchStage.SecondHalf and not MatchStage.Overtime)
            return Failure("INVALID_STAGE", "当前阶段不能使用战术暂停。");
        var roster = ParseRoster(ReadString(payload, "roster"));
        var requestedAt = ReadLong(payload, "requestedAt");
        var result = _pauses.RequestTacticalPause(
            roster,
            Snapshot.OvertimeIndex,
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, requestedAt)),
            atSafeBoundary: true);
        if (!result.Ok) return result;
        _host.PauseMatch();
        _runtime.SetPauseState(MatchPauseState.TacticalPaused);
        return result;
    }

    private MatchCommandResult TechnicalPause(JsonElement payload)
    {
        if (_pauses is null) return Failure("RUNTIME_NOT_READY", "暂停服务尚未初始化。");
        if (Snapshot.Stage is not MatchStage.FirstHalf and not MatchStage.SecondHalf and not MatchStage.Overtime
            and not MatchStage.Halftime and not MatchStage.OvertimeHalftime)
            return Failure("INVALID_STAGE", "当前阶段不能使用技术暂停。");
        var requestedAt = ReadLong(payload, "requestedAt");
        var result = _pauses.RequestTechnicalPause(
            ReadString(payload, "reason"),
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, requestedAt)),
            atSafeBoundary: true);
        if (!result.Ok) return result;
        _host.PauseMatch();
        _runtime.SetPauseState(MatchPauseState.TechnicalPaused);
        return result;
    }

    private MatchCommandResult ForceUnpause()
    {
        if (_pauses is null) return Failure("RUNTIME_NOT_READY", "暂停服务尚未初始化。");
        var result = _pauses.ForceUnpause();
        if (!result.Ok) return result;
        _host.UnpauseMatch();
        _runtime.SetPauseState(MatchPauseState.Running);
        return result;
    }

    private MatchCommandResult ConfirmUnpause(JsonElement payload)
    {
        if (_pauses is null) return Failure("RUNTIME_NOT_READY", "暂停服务尚未初始化。");
        var result = _pauses.ConfirmUnpause(ParseRoster(ReadString(payload, "roster")));
        if (result.Code == "UNPAUSED")
        {
            _host.UnpauseMatch();
            _runtime.SetPauseState(MatchPauseState.Running);
        }
        return result;
    }

    private MatchCommandResult ForcePause(JsonElement payload)
    {
        if (_pauses is null) return Failure("RUNTIME_NOT_READY", "暂停服务尚未初始化。");
        if (Snapshot.Stage is not MatchStage.FirstHalf and not MatchStage.SecondHalf and not MatchStage.Overtime
            and not MatchStage.Halftime and not MatchStage.OvertimeHalftime)
            return Failure("INVALID_STAGE", "当前阶段不能强制暂停。");
        var requestedAt = ReadLong(payload, "requestedAt");
        var result = _pauses.ForcePause(
            ReadString(payload, "reason"),
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, requestedAt)));
        if (!result.Ok) return result;
        _host.PauseMatch();
        _runtime.SetPauseState(MatchPauseState.TechnicalPaused);
        return result;
    }

    private MatchCommandResult Substitute(JsonElement payload)
    {
        if (_ready is null) return Failure("RUNTIME_NOT_READY", "席位服务尚未初始化。");
        var safeBoundary = Snapshot.PauseState == MatchPauseState.TechnicalPaused
            || Snapshot.Stage is MatchStage.Halftime or MatchStage.OvertimeHalftime;
        return _ready.Substitute(
            ReadString(payload, "seatId"),
            ReadString(payload, "steamId"),
            ReadString(payload, "name"),
            ReadInt(payload, "effectiveRound"),
            safeBoundary);
    }

    private MatchCommandResult StopRequest(JsonElement payload)
    {
        if (_backups is null) return Failure("RUNTIME_NOT_READY", "回合备份服务尚未初始化。");
        var roster = ParseRoster(ReadString(payload, "roster"));
        var requestedAt = ReadLong(payload, "requestedAt");
        var plan = _backups.RequestStop(
            roster,
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, requestedAt)),
            Math.Max(1, Snapshot.CompletedRounds + 1));
        if (!plan.Ok || plan.Plan is null) return new(plan.Ok, plan.Code, plan.Message);
        return CommitRestore(plan.Plan);
    }

    private MatchCommandResult RestoreRound(JsonElement payload)
    {
        if (_backups is null) return Failure("RUNTIME_NOT_READY", "回合备份服务尚未初始化。");
        var targetRound = ReadInt(payload, "targetRound");
        var plan = _backups.PlanRestore(targetRound);
        return plan.Ok && plan.Plan is not null
            ? CommitRestore(plan.Plan)
            : new(plan.Ok, plan.Code, plan.Message);
    }

    private MatchCommandResult CommitRestore(RestorePlan plan)
    {
        if (_backups is null) return Failure("RUNTIME_NOT_READY", "回合备份服务尚未初始化。");
        var target = new RestoreTarget(this);
        if (!_host.WaitForEngineRestore) return FinishRestore(plan, _backups.CommitRestore(plan, target));

        var request = _backups.BeginRestore(plan, target);
        if (!request.Ok)
        {
            target.EnterTechnicalPause();
            return Failure(request.Code, request.Message);
        }
        _pendingNativeRestore = (plan, _time.GetUtcNow().AddSeconds(12));
        return new(true, "RESTORE_REQUESTED", $"已请求恢复第 {plan.Target.RoundNumber} 回合，等待 CS2 确认。");
    }

    public bool ExpirePendingRestore()
    {
        if (_pendingNativeRestore is not { } pending || _time.GetUtcNow() < pending.Deadline) return false;
        _pendingNativeRestore = null;
        new RestoreTarget(this).EnterTechnicalPause();
        return true;
    }

    private MatchCommandResult FinishRestore(RestorePlan plan, RestoreCommitResult result)
    {
        if (result.Ok)
            PendingBridgeStatsRestore = plan.Target.BridgeStats?.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { },
                StringComparer.Ordinal);
        if (result.Ok && _demoStarted && _definition is not null)
        {
            _host.StopDemo();
            _host.StartDemo(_definition);
        }
        return new(result.Ok, result.Code, result.Message);
    }

    public AbilityRestoreRequest? TakePendingAbilityRestore()
    {
        var pending = PendingAbilityRestore;
        PendingAbilityRestore = null;
        return pending;
    }

    public IReadOnlyDictionary<string, BridgePlayerStats>? TakePendingBridgeStatsRestore()
    {
        var pending = PendingBridgeStatsRestore;
        PendingBridgeStatsRestore = null;
        return pending;
    }

    public void AttachBridgeStatsCheckpoint(int roundNumber, IReadOnlyDictionary<string, BridgePlayerStats> stats) =>
        _backups?.AttachBridgeStats(roundNumber, stats);

    public RosterSide? CaptainRosterForSteamId(string steamId)
    {
        var seat = _roster?.FindSeatBySteamId(steamId);
        return seat is { IsCaptain: true } ? seat.Roster : null;
    }

    public void RefreshRosterPlayers(IEnumerable<(string SteamId, EngineSide Side)> players) =>
        _ready?.ObserveSnapshot(players);

    private void CreateRoundRestorePoint()
    {
        if (_backups is null || _definition is null || Snapshot.CurrentRoundKey is null) return;
        var roundNumber = Math.Max(1, Snapshot.CompletedRounds + 1);
        var prefix = "caoren_" + new string(_definition.MatchId
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .ToArray());
        _host.Execute($"mp_backup_round_file {prefix}");
        _backups.CreateRestorePoint(new RoundRestorePoint(
            roundNumber,
            Snapshot.CurrentRoundKey,
            $"{prefix}_round{roundNumber}.txt",
            Snapshot,
            $"round-{roundNumber}",
            $"stats-{roundNumber}",
            _roster?.Substitutions.ToArray() ?? [],
            _pauses?.Snapshot ?? new PauseSnapshot(MatchPauseState.Running, null, null, null, null, false, []),
            []));
    }

    private sealed class RestoreTarget(FormalMatchRuntime owner) : IMatchRestoreTarget
    {
        public void EnterRestoring() => owner._runtime.SetPauseState(MatchPauseState.Restoring);

        public void RestoreMatchState(MatchRuntimeSnapshot snapshot) => owner._runtime.Restore(snapshot);

        public void RestoreAbility(string checkpointId, long runtimeEpoch)
        {
            var roundNumber = Math.Max(1, owner.Snapshot.CompletedRounds + 1);
            owner.PendingAbilityRestore = new AbilityRestoreRequest(
                checkpointId,
                $"{owner.Snapshot.MatchId}:{runtimeEpoch}:round-{roundNumber}",
                runtimeEpoch);
        }

        public void ReapplyRosterAndCvars()
        {
            owner._host.ApplyFormalMatchConfig();
            owner._roster?.SetTeamAEngineSide(owner.Snapshot.TeamAEngineSide);
        }

        public void EnterTechnicalPause()
        {
            owner._pauses = new PauseCoordinator();
            owner._pauses.RequestTechnicalPause("round restore", DateTimeOffset.UtcNow, atSafeBoundary: true);
            owner._runtime.SetPauseState(MatchPauseState.TechnicalPaused);
        }
    }

    private MatchCommandResult ConfirmSideSwap()
    {
        if (!Snapshot.SideSwapPending) return Failure("INVALID_STAGE", "当前没有待确认换边。");
        _host.SwapSides();
        var result = _runtime.ConfirmSideSwap();
        if (result.Ok && _roster is not null) _roster.SetTeamAEngineSide(Snapshot.TeamAEngineSide);
        return result;
    }

    private MatchCommandResult ValidateIdentity(JsonElement payload)
    {
        var matchId = ReadString(payload, "matchId");
        var revision = ReadInt(payload, "revision");
        var runtimeEpoch = ReadLong(payload, "runtimeEpoch");
        return string.Equals(matchId, Snapshot.MatchId, StringComparison.Ordinal)
            && revision == Snapshot.Revision
            && runtimeEpoch == Snapshot.RuntimeEpoch
            ? new(true, "OK")
            : Failure("IDENTITY_MISMATCH", "运行时命令不属于当前比赛实例。");
    }

    private static MatchDefinition ParseDefinition(JsonElement payload)
    {
        var matchId = ReadString(payload, "matchId");
        var revision = ReadInt(payload, "revision");
        var mapName = ReadString(payload, "mapName");
        var teamAName = ReadString(payload, "teamAName");
        var teamBName = ReadString(payload, "teamBName");
        var initialSide = ParseEngineSide(ReadString(payload, "teamAInitialSide"));
        var mode = ReadString(payload, "modeId");
        if (mode.Length == 0) mode = "competitive";
        if (mode is not ("competitive" or "ability")) throw new InvalidDataException("当前模式暂不支持正式比赛流程。");
        int? warmup = null;
        if (payload.TryGetProperty("warmupSeconds", out var timer))
        {
            if (timer.ValueKind != JsonValueKind.Number || !timer.TryGetInt32(out var seconds) || seconds is < 1 or > 300)
                throw new InvalidDataException("热身秒数必须在 1 到 300 之间。");
            warmup = seconds;
        }
        if (!payload.TryGetProperty("seats", out var seatsElement) || seatsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("比赛配置缺少 seats。");
        var seats = seatsElement.EnumerateArray().Select(item => new MatchSeat(
            ReadString(item, "seatId"),
            ParseRoster(ReadString(item, "roster")),
            ReadString(item, "steamId"),
            ReadString(item, "name"),
            item.TryGetProperty("isCaptain", out var captain) && captain.ValueKind == JsonValueKind.True)).ToArray();
        return new MatchDefinition(matchId, revision, mapName, teamAName, teamBName, initialSide, seats, mode, warmup);
    }

    private static RosterSide ParseRoster(string value) => value.ToUpperInvariant() switch
    {
        "A" => RosterSide.A,
        "B" => RosterSide.B,
        _ => throw new InvalidDataException("席位 roster 只能是 A 或 B。"),
    };

    private static EngineSide ParseEngineSide(string value) => value.ToUpperInvariant() switch
    {
        "CT" => EngineSide.CounterTerrorist,
        "T" => EngineSide.Terrorist,
        _ => throw new InvalidDataException("初始阵营只能是 CT 或 T。"),
    };

    private static string ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var value) ? value : 0;

    private static long ReadLong(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out var value) ? value : 0;

    private static MatchCommandResult Failure(string code, string message) => new(false, code, message);
}
